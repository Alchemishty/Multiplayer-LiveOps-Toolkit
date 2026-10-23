import os
import logging
import base64
import hmac
import hashlib
from rest_framework.decorators import api_view, permission_classes, throttle_classes
from rest_framework.response import Response
from rest_framework import status
from rest_framework.permissions import IsAuthenticated
from django.db import transaction
from django.core.cache import cache

from shop.models import Wallet
from shop.serializers import WalletSerializer
from shop.throttles import EconomyMutationThrottle

logger = logging.getLogger(__name__)

CONVERSION_RATES = {
    "snowflakes": {"ratio": 0.5, "label": "2 snowflakes = 1 gold"},
    "gears": {"ratio": 25, "label": "1 gear = 25 gold"},
}

SESSION_REWARD_SECRET = os.environ.get("SESSION_REWARD_SECRET", "")
if not SESSION_REWARD_SECRET:
    SESSION_REWARD_SECRET = "change-me-in-production"
    logger.warning("SESSION_REWARD_SECRET not set — using insecure default. Set this in production!")
SESSION_REWARDS_BY_LEVEL = {1: 15, 2: 40, 3: 120}


@api_view(["GET"])
@permission_classes([IsAuthenticated])
def get_wallet(request):
    """
    Get all currency balances for the current player.

    GET /api/wallet/
    Response: [{"currency": {"id": 1, "name": "gold"}, "amount": 500}, ...]

    curl http://localhost:8000/api/wallet/ \\
      -H "Authorization: Bearer <token>"
    """
    profile = request.user.player_profile
    wallets = Wallet.objects.filter(player=profile).select_related("currency")
    serializer = WalletSerializer(wallets, many=True)
    return Response(serializer.data)


@api_view(["POST"])
@permission_classes([IsAuthenticated])
@throttle_classes([EconomyMutationThrottle])
def convert_currency(request):
    """
    Convert secondary currencies to gold. Uses select_for_update for safe
    concurrent access. Rates: 2 snowflakes = 1 gold, 1 gear = 25 gold.

    POST /api/shop/convert-currency/
    Body: {"currency_type": "snowflakes", "amount": 100}
    Success 200: {"gold_amount": 50}
    Error 400: {"message": "Not enough snowflakes."}

    curl -X POST http://localhost:8000/api/shop/convert-currency/ \\
      -H "Authorization: Bearer <token>" \\
      -H "Content-Type: application/json" \\
      -d '{"currency_type": "snowflakes", "amount": 100}'
    """
    profile = request.user.player_profile
    currency_type = request.data.get("currency_type")
    amount = request.data.get("amount")

    if not currency_type or currency_type not in CONVERSION_RATES:
        return Response(
            {"message": f"Invalid currency type. Use one of: {list(CONVERSION_RATES.keys())}"},
            status=status.HTTP_400_BAD_REQUEST,
        )

    try:
        amount = int(amount)
    except (ValueError, TypeError):
        return Response(
            {"message": "Amount must be a valid integer."},
            status=status.HTTP_400_BAD_REQUEST,
        )

    if amount <= 0:
        return Response(
            {"message": "Amount must be greater than zero."},
            status=status.HTTP_400_BAD_REQUEST,
        )

    rate = CONVERSION_RATES[currency_type]["ratio"]

    with transaction.atomic():
        user_wallets = Wallet.objects.select_for_update().filter(player=profile)

        source_wallet = user_wallets.filter(currency__name=currency_type).first()
        if not source_wallet or source_wallet.amount < amount:
            return Response(
                {"message": f"Not enough {currency_type}."},
                status=status.HTTP_400_BAD_REQUEST,
            )

        gold_wallet = user_wallets.filter(currency__name="gold").first()
        if not gold_wallet:
            return Response(
                {"message": "Gold wallet not found."},
                status=status.HTTP_400_BAD_REQUEST,
            )

        gold_amount = int(amount * rate)
        source_wallet.amount -= amount
        source_wallet.save()
        gold_wallet.amount += gold_amount
        gold_wallet.save()

        logger.info(
            "User %s converted %d %s to %d gold",
            request.user.id, amount, currency_type, gold_amount,
        )

        return Response({"gold_amount": gold_amount}, status=status.HTTP_200_OK)


def verify_reward_token(token, user_id):
    """Verify HMAC-signed session reward token. Returns level (1-3) or None."""
    try:
        decoded = base64.b64decode(token).decode()
        parts = decoded.split(":")
        if len(parts) != 3:
            return None

        token_user_id, levels_str, signature = parts
        if token_user_id != str(user_id):
            return None

        payload = f"{token_user_id}:{levels_str}"
        expected_sig = hmac.new(
            SESSION_REWARD_SECRET.encode(), payload.encode(), hashlib.sha256
        ).digest()
        expected_sig_b64 = base64.b64encode(expected_sig).decode()

        if not hmac.compare_digest(expected_sig_b64, signature):
            return None

        return int(levels_str)
    except Exception:
        return None


@api_view(["POST"])
@permission_classes([IsAuthenticated])
def session_reward(request):
    """
    Claim a post-game session reward. Token is HMAC-signed with the user ID
    and level. Rate-limited to once per 5 minutes per user.
    Rewards: level 1 = 15 gold, level 2 = 40 gold, level 3 = 120 gold.

    POST /api/get-session-reward/
    Body: {"reward_token": "<base64-encoded-hmac-token>"}
    Success 200: {"success": true}
    Error 400: {"message": "Invalid or expired token"}
    Error 429: {"message": "Please wait 5 minutes before claiming another reward."}

    curl -X POST http://localhost:8000/api/get-session-reward/ \\
      -H "Authorization: Bearer <token>" \\
      -H "Content-Type: application/json" \\
      -d '{"reward_token": "dXNlcjE6Mjo8c2lnPg=="}'
    """
    profile = request.user.player_profile
    cache_key = f"session_reward_{request.user.id}"
    if cache.get(cache_key):
        return Response(
            {"message": "Please wait 5 minutes before claiming another reward."},
            status=status.HTTP_429_TOO_MANY_REQUESTS,
        )

    token = request.data.get("reward_token")
    if not token:
        return Response(
            {"message": "Missing reward token"},
            status=status.HTTP_400_BAD_REQUEST,
        )

    level = verify_reward_token(token, request.user.id)
    if level is None or level not in SESSION_REWARDS_BY_LEVEL:
        return Response(
            {"message": "Invalid or expired token"},
            status=status.HTTP_400_BAD_REQUEST,
        )

    with transaction.atomic():
        if cache.get(cache_key):
            return Response(
                {"message": "Please wait 5 minutes before claiming another reward."},
                status=status.HTTP_429_TOO_MANY_REQUESTS,
            )
        cache.set(cache_key, True, 300)

        gold_wallet = (
            Wallet.objects.select_for_update()
            .filter(player=profile, currency__name="gold")
            .first()
        )

        if not gold_wallet:
            return Response(
                {"message": "Gold wallet not found."},
                status=status.HTTP_400_BAD_REQUEST,
            )

        reward = SESSION_REWARDS_BY_LEVEL[level]
        gold_wallet.amount += reward
        gold_wallet.save()

        logger.info(
            "User %s claimed %d gold for level %d",
            request.user.username, reward, level,
        )
        return Response({"success": True})
