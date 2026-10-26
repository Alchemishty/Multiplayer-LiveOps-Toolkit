from django.conf import settings as django_settings
from django.contrib.auth import authenticate
from django.contrib.auth.models import User
from django.db import transaction
from rest_framework.decorators import api_view, permission_classes, authentication_classes, throttle_classes
from rest_framework.permissions import AllowAny, IsAuthenticated
from rest_framework.response import Response
from rest_framework import status
from rest_framework_simplejwt.tokens import RefreshToken

from shop.throttles import PasswordLoginThrottle

from shop.models import (
    PlayerProfile, Currency, Wallet, UserAccessory, UserAchievement,
    DailyAttendance,
)


def _create_player_profile(user, platform_id=""):
    """Create a PlayerProfile with wallets for a new user."""
    profile = PlayerProfile.objects.create(user=user, platform_id=platform_id)
    for currency in Currency.objects.all():
        Wallet.objects.create(player=profile, currency=currency, amount=0)
    return profile


@api_view(["POST"])
@authentication_classes([])
@permission_classes([AllowAny])
@throttle_classes([PasswordLoginThrottle])
def password_authenticate(request):
    """Authenticate a provisioned Django user; never create users from login input."""
    if not isinstance(request.data, dict):
        return Response({"code": "invalid_credentials", "message": "Username and password are required."}, status=400)
    username = request.data.get("username")
    password = request.data.get("password")
    if (not isinstance(username, str) or not username.strip() or len(username) > 150
            or not isinstance(password, str) or not password or len(password) > 4096):
        return Response({"code": "invalid_credentials", "message": "Username and password are required."}, status=400)

    user = authenticate(request=request, username=username, password=password)
    if user is None or not user.is_active:
        return Response({"code": "authentication_rejected", "message": "Invalid username or password."}, status=401)

    # Serialize first-login provisioning for an existing account. An account
    # from another identity provider retains its profile and economy state.
    with transaction.atomic():
        user = User.objects.select_for_update().get(pk=user.pk)
        try:
            profile = PlayerProfile.objects.get(user=user)
        except PlayerProfile.DoesNotExist:
            profile = _create_player_profile(user, platform_id=f"django_{user.pk}")

    refresh = RefreshToken.for_user(user)
    return Response({
        "access": str(refresh.access_token),
        "refresh": str(refresh),
        "username": user.username,
        "player_id": str(profile.pk),
    })


@api_view(["POST"])
@permission_classes([AllowAny])
def dev_authenticate(request):
    """
    DEBUG-only auth bypass for testing. Creates or retrieves a test user
    with 5000 starting gold, 500 snowflakes, 100 gears.

    POST /api/auth/dev/
    Body: {"username": "testplayer1"}
    Success 200: {"access": "<jwt>", "refresh": "<jwt>", "username": "testplayer1", "player_id": "1"}
    Error 404: returned when DEBUG=False

    curl -X POST http://localhost:8000/api/auth/dev/ \\
      -H "Content-Type: application/json" \\
      -d '{"username": "testplayer1"}'
    """
    if not django_settings.DEBUG:
        return Response(status=status.HTTP_404_NOT_FOUND)

    username = request.data.get("username")
    if not username:
        return Response(
            {"message": "username required"},
            status=status.HTTP_400_BAD_REQUEST,
        )

    user, created = User.objects.get_or_create(
        username=username,
        defaults={"password": ""},
    )

    if not created and (
        not user.is_active
        or user.password != ""
        or not PlayerProfile.objects.filter(user=user, platform_id=f"dev_{username}").exists()
    ):
        return Response({"message": "Development authentication is restricted to development accounts."}, status=401)

    if created:
        profile = PlayerProfile.objects.create(
            user=user, platform_id=f"dev_{username}"
        )
        starting_balances = {"gold": 5000, "snowflakes": 500, "gears": 100}
        for currency in Currency.objects.all():
            Wallet.objects.create(
                player=profile,
                currency=currency,
                amount=starting_balances.get(currency.name, 0),
            )

    refresh = RefreshToken.for_user(user)
    return Response(
        {
            "refresh": str(refresh),
            "access": str(refresh.access_token),
            "username": user.username,
            "player_id": str(user.player_profile.pk),
        }
    )


@api_view(["POST"])
@permission_classes([IsAuthenticated])
def reset_account(request):
    """
    Reset the current player's account to starting state. DEBUG-only.
    Clears all owned accessories, achievements, daily attendance, and
    resets wallet balances to starting amounts.

    POST /api/auth/reset/
    Success 200: {"message": "Account reset successfully"}
    Error 404: returned when DEBUG=False

    curl -X POST http://localhost:8000/api/auth/reset/ \\
      -H "Authorization: Bearer <token>"
    """
    if not django_settings.DEBUG:
        return Response(status=status.HTTP_404_NOT_FOUND)

    profile = request.user.player_profile
    starting_balances = {"gold": 5000, "snowflakes": 500, "gears": 100}

    with transaction.atomic():
        profile = PlayerProfile.objects.select_for_update().get(pk=profile.pk)
        if profile.equipment_revision >= 9223372036854775806:
            return Response({"code": "equipment_configuration"}, status=409)
        profile.equipment_revision += 1
        profile.save(update_fields=["equipment_revision"])
        UserAccessory.objects.filter(player=profile).delete()
        UserAchievement.objects.filter(player=profile).delete()
        DailyAttendance.objects.filter(player=profile).delete()

        for wallet in Wallet.objects.select_for_update().filter(player=profile):
            wallet.amount = starting_balances.get(wallet.currency.name, 0)
            wallet.save()

    return Response({"message": "Account reset successfully"})
