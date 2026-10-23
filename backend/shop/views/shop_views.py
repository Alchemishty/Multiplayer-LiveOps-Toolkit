from rest_framework.decorators import api_view, permission_classes, throttle_classes
from rest_framework.response import Response
from rest_framework.permissions import IsAuthenticated
from django.shortcuts import get_object_or_404
from django.db import transaction
from shop.models import Wallet, Accessory, UserAccessory, AccessorySet
from shop.serializers import UserAccessorySerializer
from shop.throttles import EconomyMutationThrottle


@api_view(["GET"])
@permission_classes([IsAuthenticated])
def list_accessories(request):
    """
    List all shop accessories grouped by type, with costs and ownership.

    GET /api/shop/accessories/
    Response: {"accessories": {"head": [{"id": 1, "name": "Crown", "is_owned": false, "costs": [{"currency": "gold", "amount": 100}]}], ...}}

    curl http://localhost:8000/api/shop/accessories/ \\
      -H "Authorization: Bearer <token>"
    """
    profile = request.user.player_profile
    set_only_types = {"head", "torso", "arms", "legs", "feet"}
    accessories = (
        Accessory.objects.prefetch_related("costs", "costs__currency")
        .exclude(type__in=set_only_types)
        .order_by("type")
        .all()
    )

    user_accessories = set(
        UserAccessory.objects.filter(player=profile).values_list(
            "accessory_id", flat=True
        )
    )

    response_data = {"accessories": {}}
    for accessory in accessories:
        if accessory.type not in response_data["accessories"]:
            response_data["accessories"][accessory.type] = []

        costs = [
            {"currency": cost.currency.name, "amount": cost.amount}
            for cost in accessory.costs.all()
        ]

        response_data["accessories"][accessory.type].append(
            {
                "id": accessory.id,
                "name": accessory.name,
                "is_owned": accessory.id in user_accessories,
                "costs": costs,
            }
        )
    return Response(response_data)


@api_view(["POST"])
@permission_classes([IsAuthenticated])
@throttle_classes([EconomyMutationThrottle])
def purchase_accessory(request):
    """
    Purchase an accessory. Validates ownership and all currency balances
    atomically before deducting.

    POST /api/shop/purchase/
    Body: {"accessory_id": 1}
    Success 200: {"message": "Crystal Crown purchased successfully"}
    Error 400: {"message": "Not enough gold"} | {"message": "Accessory already owned"}

    curl -X POST http://localhost:8000/api/shop/purchase/ \\
      -H "Authorization: Bearer <token>" \\
      -H "Content-Type: application/json" \\
      -d '{"accessory_id": 1}'
    """
    profile = request.user.player_profile
    accessory_id = request.data.get("accessory_id")
    if accessory_id is None:
        return Response({"message": "accessory_id is required"}, status=400)

    try:
        accessory_id = int(accessory_id)
    except (TypeError, ValueError):
        return Response({"message": "accessory_id must be an integer"}, status=400)

    accessory = get_object_or_404(Accessory, id=accessory_id)
    costs = list(accessory.costs.select_related("currency").all())

    with transaction.atomic():
        if UserAccessory.objects.select_for_update().filter(
            player=profile, accessory=accessory
        ).exists():
            return Response({"message": "Accessory already owned"}, status=400)

        wallets = {
            w.currency_id: w
            for w in Wallet.objects.select_for_update().filter(player=profile)
        }

        for cost in costs:
            wallet = wallets.get(cost.currency_id)
            if not wallet or wallet.amount < cost.amount:
                return Response(
                    {"message": f"Not enough {cost.currency.name}"}, status=400
                )

        for cost in costs:
            wallet = wallets[cost.currency_id]
            wallet.amount -= cost.amount
            wallet.save()

        UserAccessory.objects.create(player=profile, accessory=accessory)

    return Response({"message": f"{accessory.name} purchased successfully"})


@api_view(["GET"])
@permission_classes([IsAuthenticated])
def user_accessories(request):
    """
    List the current user's owned accessories with full details.

    GET /api/accessories/
    Response: [{"accessory": {"id": 1, "name": "Crown", ...}, "acquired_at": "...", "is_equipped": true}, ...]

    curl http://localhost:8000/api/accessories/ \\
      -H "Authorization: Bearer <token>"
    """
    profile = request.user.player_profile
    accessories = UserAccessory.objects.filter(player=profile).select_related(
        "accessory"
    )
    serializer = UserAccessorySerializer(accessories, many=True)
    return Response(serializer.data)


@api_view(["POST"])
@permission_classes([IsAuthenticated])
@throttle_classes([EconomyMutationThrottle])
def update_equipped_accessories(request):
    """
    Replace-all equip: set the complete equipped loadout. Validates all IDs
    are owned before making changes. Unequips everything not in the list.

    POST /api/accessories/update-equipped/
    Body: {"accessory_ids": [1, 5, 12]}
    Success 200: {"message": "Equipped accessories updated successfully."}
    Error 400: {"message": "User does not own accessory with ID 99."}

    curl -X POST http://localhost:8000/api/accessories/update-equipped/ \\
      -H "Authorization: Bearer <token>" \\
      -H "Content-Type: application/json" \\
      -d '{"accessory_ids": [1, 5, 12]}'
    """
    profile = request.user.player_profile
    accessory_ids = request.data.get("accessory_ids", [])

    with transaction.atomic():
        owned_accessories_by_id = {
            user_accessory.accessory_id: user_accessory
            for user_accessory in UserAccessory.objects.select_for_update().filter(player=profile)
        }

        for accessory_id in accessory_ids:
            if accessory_id not in owned_accessories_by_id:
                return Response(
                    {"message": f"User does not own accessory with ID {accessory_id}."},
                    status=400,
                )

        # Resolve slot conflicts: last item per type wins
        accessories_by_id = {
            accessory.id: accessory
            for accessory in Accessory.objects.filter(id__in=accessory_ids)
        }

        slot_winners = {}
        for accessory_id in accessory_ids:
            if accessory_id not in accessories_by_id:
                return Response(
                    {"message": f"Accessory with ID {accessory_id} does not exist."},
                    status=400,
                )
            slot_winners[accessories_by_id[accessory_id].type] = accessory_id

        equipped_accessory_ids = set(slot_winners.values())

        UserAccessory.objects.filter(player=profile).update(is_equipped=False)
        UserAccessory.objects.filter(
            player=profile, accessory_id__in=equipped_accessory_ids
        ).update(is_equipped=True)

    return Response({"message": "Equipped accessories updated successfully."})


@api_view(["GET"])
@permission_classes([IsAuthenticated])
def get_equipped_accessories(request):
    """
    Get the IDs of all currently equipped accessories.

    GET /api/accessories/get-equipped/
    Response: {"accessory_ids": [1, 5, 12]}

    curl http://localhost:8000/api/accessories/get-equipped/ \\
      -H "Authorization: Bearer <token>"
    """
    profile = request.user.player_profile
    equipped = UserAccessory.objects.filter(
        player=profile, is_equipped=True
    ).values_list("accessory_id", flat=True)

    return Response({"accessory_ids": list(equipped)})


@api_view(["GET"])
@permission_classes([IsAuthenticated])
def list_sets(request):
    """
    List all accessory sets grouped by category, with costs and ownership.

    GET /api/shop/sets/
    Response: {"sets": {"bundles": [{"id": 1, "name": "Knight Set", "is_owned": false,
               "accessory_ids": [9, 13, 14, 15, 16], "costs": [{"currency": "gold", "amount": 400}]}]}}
    """
    profile = request.user.player_profile
    sets = AccessorySet.objects.prefetch_related(
        "accessories", "costs", "costs__currency"
    ).all()

    owned_accessory_ids = set(
        UserAccessory.objects.filter(player=profile).values_list(
            "accessory_id", flat=True
        )
    )

    response_data = {"sets": {}}
    for accessory_set in sets:
        category = accessory_set.category
        if category not in response_data["sets"]:
            response_data["sets"][category] = []

        accessory_ids = list(
            accessory_set.accessories.values_list("id", flat=True)
        )
        is_owned = all(aid in owned_accessory_ids for aid in accessory_ids)

        costs = [
            {"currency": cost.currency.name, "amount": cost.amount}
            for cost in accessory_set.costs.all()
        ]

        response_data["sets"][category].append(
            {
                "id": accessory_set.id,
                "name": accessory_set.name,
                "is_owned": is_owned,
                "accessory_ids": accessory_ids,
                "costs": costs,
            }
        )

    return Response(response_data)


@api_view(["POST"])
@permission_classes([IsAuthenticated])
@throttle_classes([EconomyMutationThrottle])
def purchase_set(request):
    """
    Purchase an accessory set. Validates ownership of all items and currency
    balances atomically before deducting. Grants all accessories in the set.

    POST /api/shop/purchase-set/
    Body: {"set_id": 1}
    Success 200: {"accessory_ids": [9, 13, 14, 15, 16]}
    Error 400: {"message": "Not enough gold"} | {"message": "Set already fully owned"}
    """
    profile = request.user.player_profile
    set_id = request.data.get("set_id")
    if set_id is None:
        return Response({"message": "set_id is required"}, status=400)

    try:
        set_id = int(set_id)
    except (TypeError, ValueError):
        return Response({"message": "set_id must be an integer"}, status=400)

    accessory_set = get_object_or_404(AccessorySet, id=set_id)
    accessory_ids = list(
        accessory_set.accessories.values_list("id", flat=True)
    )
    costs = list(accessory_set.costs.select_related("currency").all())

    with transaction.atomic():
        owned_ids = set(
            UserAccessory.objects.select_for_update().filter(
                player=profile, accessory_id__in=accessory_ids
            ).values_list("accessory_id", flat=True)
        )

        unowned_ids = [aid for aid in accessory_ids if aid not in owned_ids]
        if not unowned_ids:
            return Response({"message": "Set already fully owned"}, status=400)

        wallets = {
            w.currency_id: w
            for w in Wallet.objects.select_for_update().filter(player=profile)
        }

        for cost in costs:
            wallet = wallets.get(cost.currency_id)
            if not wallet or wallet.amount < cost.amount:
                return Response(
                    {"message": f"Not enough {cost.currency.name}"}, status=400
                )

        for cost in costs:
            wallet = wallets[cost.currency_id]
            wallet.amount -= cost.amount
            wallet.save()

        for accessory_id in unowned_ids:
            UserAccessory.objects.get_or_create(
                player=profile,
                accessory=Accessory.objects.get(id=accessory_id),
            )

    return Response({"accessory_ids": accessory_ids})
