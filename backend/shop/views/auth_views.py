import os
import requests
from django.conf import settings as django_settings
from django.contrib.auth.models import User
from django.db import transaction, IntegrityError
from rest_framework.decorators import api_view, permission_classes
from rest_framework.permissions import AllowAny, IsAuthenticated
from rest_framework.response import Response
from rest_framework import status
from rest_framework_simplejwt.tokens import RefreshToken

from shop.models import (
    PlayerProfile, Currency, Wallet, UserAccessory, UserAchievement,
    DailyAttendance,
)


def verify_platform_identity(platform_user_id, nonce):
    """Verify user identity with the platform provider (e.g., Oculus, Steam)."""
    app_id = os.environ.get("PLATFORM_APP_ID", "")
    app_secret = os.environ.get("PLATFORM_APP_SECRET", "")

    if not app_id or not app_secret:
        return False

    verification_url = os.environ.get(
        "PLATFORM_VERIFY_URL",
        "https://graph.oculus.com/user_nonce_validate",
    )
    access_token = f"OC|{app_id}|{app_secret}"

    response = requests.post(
        verification_url,
        data={
            "nonce": nonce,
            "user_id": platform_user_id,
            "access_token": access_token,
        },
    )
    return response.status_code == 200


def _create_player_profile(user, platform_id=""):
    """Create a PlayerProfile with wallets for a new user."""
    profile = PlayerProfile.objects.create(user=user, platform_id=platform_id)
    for currency in Currency.objects.all():
        Wallet.objects.create(player=profile, currency=currency, amount=0)
    return profile


@api_view(["POST"])
@permission_classes([AllowAny])
def authenticate_user(request):
    """
    Authenticate via platform nonce verification. Creates user + player profile
    + wallets on first login. Returns JWT access and refresh tokens.

    POST /api/auth/
    Body: {"platform_user_id": "abc123", "nonce": "xyz", "username": "player1"}
    Success 200: {"access": "<jwt>", "refresh": "<jwt>", "username": "player1", "platform_id": "abc123"}
    Error 401: {"message": "Platform verification failed"}

    curl -X POST http://localhost:8000/api/auth/ \\
      -H "Content-Type: application/json" \\
      -d '{"platform_user_id": "abc123", "nonce": "xyz", "username": "player1"}'
    """
    platform_user_id = request.data.get("platform_user_id")
    nonce = request.data.get("nonce")
    username = request.data.get("username")

    if not platform_user_id or not nonce:
        return Response(
            {"message": "Missing platform_user_id or nonce"},
            status=status.HTTP_400_BAD_REQUEST,
        )

    if not verify_platform_identity(platform_user_id, nonce):
        return Response(
            {"message": "Platform verification failed"},
            status=status.HTTP_401_UNAUTHORIZED,
        )

    try:
        profile = PlayerProfile.objects.select_related("user").get(
            platform_id=platform_user_id
        )
        user = profile.user
    except PlayerProfile.DoesNotExist:
        try:
            user = User.objects.create_user(username=username)
            profile = _create_player_profile(user, platform_id=platform_user_id)
        except IntegrityError:
            profile = PlayerProfile.objects.select_related("user").get(
                platform_id=platform_user_id
            )
            user = profile.user

    refresh = RefreshToken.for_user(user)

    return Response(
        {
            "refresh": str(refresh),
            "access": str(refresh.access_token),
            "username": user.username,
            "platform_id": profile.platform_id,
        }
    )


@api_view(["POST"])
@permission_classes([AllowAny])
def dev_authenticate(request):
    """
    DEBUG-only auth bypass for testing. Creates or retrieves a test user
    with 5000 starting gold, 500 snowflakes, 100 gears.

    POST /api/auth/dev/
    Body: {"username": "testplayer1"}
    Success 200: {"access": "<jwt>", "refresh": "<jwt>", "username": "testplayer1"}
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
        UserAccessory.objects.filter(player=profile).delete()
        UserAchievement.objects.filter(player=profile).delete()
        DailyAttendance.objects.filter(player=profile).delete()

        for wallet in Wallet.objects.select_for_update().filter(player=profile):
            wallet.amount = starting_balances.get(wallet.currency.name, 0)
            wallet.save()

    return Response({"message": "Account reset successfully"})
