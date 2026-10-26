from datetime import date
from unittest.mock import patch

from django.test import TestCase, override_settings
from django.contrib.auth.models import User
from rest_framework.test import APIClient
from rest_framework_simplejwt.tokens import RefreshToken

from shop.models import (
    PlayerProfile, Currency, Wallet, Accessory, AccessoryCost,
    UserAccessory, MonthlyRewardSchedule, DailyAttendance,
    Achievement, UserAchievement,
)


class ShopTestBase(TestCase):
    """Shared setup: user, profile, currencies, wallets, accessories with costs."""

    def setUp(self):
        from django.core.cache import cache
        # Clear throttle cache to avoid rate limiting in tests
        cache.clear()

        self.user = User.objects.create_user(
            username="testplayer", password="testpass"
        )
        self.profile = PlayerProfile.objects.create(
            user=self.user, platform_id="plat_001"
        )

        self.gold = Currency.objects.create(name="gold")
        self.snowflakes = Currency.objects.create(name="snowflakes")
        self.gears = Currency.objects.create(name="gears")

        self.wallet_gold = Wallet.objects.create(
            player=self.profile, currency=self.gold, amount=500
        )
        self.wallet_snow = Wallet.objects.create(
            player=self.profile, currency=self.snowflakes, amount=100
        )
        self.wallet_gears = Wallet.objects.create(
            player=self.profile, currency=self.gears, amount=10
        )

        self.hat = Accessory.objects.create(name="Top Hat", type="headwear")
        AccessoryCost.objects.create(
            accessory=self.hat, currency=self.gold, amount=100
        )

        self.cape = Accessory.objects.create(name="Royal Cape", type="back")
        AccessoryCost.objects.create(
            accessory=self.cape, currency=self.gold, amount=200
        )

        self.crown = Accessory.objects.create(name="Crown", type="headwear")
        AccessoryCost.objects.create(
            accessory=self.crown, currency=self.gold, amount=150
        )
        AccessoryCost.objects.create(
            accessory=self.crown, currency=self.gears, amount=3
        )

        self.expensive = Accessory.objects.create(name="Diamond Armor", type="back")
        AccessoryCost.objects.create(
            accessory=self.expensive, currency=self.gold, amount=9999
        )

        self.client = APIClient()
        token = RefreshToken.for_user(self.user)
        self.client.credentials(
            HTTP_AUTHORIZATION=f"Bearer {token.access_token}"
        )


# ---------------------------------------------------------------------------
# Purchase flow
# ---------------------------------------------------------------------------



# ---------------------------------------------------------------------------
# Equip flow
# ---------------------------------------------------------------------------
# Wallet & currency conversion
# ---------------------------------------------------------------------------

class WalletTests(ShopTestBase):

    def test_get_wallet_balances(self):
        resp = self.client.get("/api/wallet/")
        self.assertEqual(resp.status_code, 200)
        self.assertEqual(len(resp.data["balances"]), 3)

    def test_convert_snowflakes_to_gold(self):
        resp = self.client.post(
            "/api/wallet/convert/",
            {"offer_id": "snowflakes-to-gold-v1", "amount": 20},
            format="json",
        )
        self.assertEqual(resp.status_code, 200)
        self.assertEqual(next(balance["amount"] for balance in resp.data["balances"] if balance["currency_id"] == "gold"), 510)

        self.wallet_snow.refresh_from_db()
        self.wallet_gold.refresh_from_db()
        self.assertEqual(self.wallet_snow.amount, 80)
        self.assertEqual(self.wallet_gold.amount, 510)

    def test_convert_gears_to_gold(self):
        resp = self.client.post(
            "/api/wallet/convert/",
            {"offer_id": "gears-to-gold-v1", "amount": 2},
            format="json",
        )
        self.assertEqual(resp.status_code, 200)
        self.assertEqual(next(balance["amount"] for balance in resp.data["balances"] if balance["currency_id"] == "gold"), 550)

        self.wallet_gears.refresh_from_db()
        self.wallet_gold.refresh_from_db()
        self.assertEqual(self.wallet_gears.amount, 8)
        self.assertEqual(self.wallet_gold.amount, 550)

    def test_convert_insufficient_funds(self):
        resp = self.client.post(
            "/api/wallet/convert/",
            {"offer_id": "snowflakes-to-gold-v1", "amount": 9999},
            format="json",
        )
        self.assertEqual(resp.status_code, 400)

        self.wallet_snow.refresh_from_db()
        self.assertEqual(self.wallet_snow.amount, 100)

    def test_convert_invalid_currency(self):
        resp = self.client.post(
            "/api/wallet/convert/",
            {"offer_id": "unknown-offer", "amount": 1},
            format="json",
        )
        self.assertEqual(resp.status_code, 400)

    def test_convert_zero_amount(self):
        resp = self.client.post(
            "/api/wallet/convert/",
            {"offer_id": "gears-to-gold-v1", "amount": 0},
            format="json",
        )
        self.assertEqual(resp.status_code, 400)


# ---------------------------------------------------------------------------
# Daily login streak
# ---------------------------------------------------------------------------

class DevAuthTests(TestCase):

    def setUp(self):
        self.gold = Currency.objects.create(name="gold")
        self.snowflakes = Currency.objects.create(name="snowflakes")
        self.gears = Currency.objects.create(name="gears")

    @override_settings(DEBUG=True)
    def test_dev_auth_creates_user_and_returns_tokens(self):
        resp = self.client.post(
            "/api/auth/dev/",
            {"username": "testplayer1"},
            content_type="application/json",
        )
        self.assertEqual(resp.status_code, 200)
        data = resp.json()
        self.assertIn("access", data)
        self.assertIn("refresh", data)
        self.assertEqual(data["username"], "testplayer1")
        self.assertEqual(data["player_id"], str(User.objects.get(username="testplayer1").player_profile.pk))
        user = User.objects.get(username="testplayer1")
        profile = user.player_profile
        self.assertEqual(Wallet.objects.filter(player=profile).count(), 3)

    @override_settings(DEBUG=True)
    def test_dev_auth_returns_existing_user(self):
        self.client.post(
            "/api/auth/dev/",
            {"username": "testplayer1"},
            content_type="application/json",
        )
        resp = self.client.post(
            "/api/auth/dev/",
            {"username": "testplayer1"},
            content_type="application/json",
        )
        self.assertEqual(resp.status_code, 200)
        self.assertEqual(User.objects.filter(username="testplayer1").count(), 1)
        self.assertEqual(resp.json()["player_id"], str(User.objects.get(username="testplayer1").player_profile.pk))

    @override_settings(DEBUG=False)
    def test_dev_auth_blocked_in_production(self):
        resp = self.client.post(
            "/api/auth/dev/",
            {"username": "hacker"},
            content_type="application/json",
        )
        self.assertEqual(resp.status_code, 404)

    @override_settings(DEBUG=True)
    def test_dev_auth_gives_starting_gold(self):
        self.client.post(
            "/api/auth/dev/",
            {"username": "richplayer"},
            content_type="application/json",
        )
        user = User.objects.get(username="richplayer")
        gold_wallet = Wallet.objects.get(player=user.player_profile, currency__name="gold")
        self.assertEqual(gold_wallet.amount, 5000)


# ---------------------------------------------------------------------------
# Auth (unauthenticated access)
# ---------------------------------------------------------------------------

class AuthRequiredTests(TestCase):
    def test_legacy_routes_are_not_exposed(self):
        from django.urls import resolve, Resolver404
        for route in ['/api/auth/', '/api/accessories/', '/api/get-session-reward/']:
            with self.subTest(route=route), self.assertRaises(Resolver404):
                resolve(route)


    def test_shop_requires_auth(self):
        client = APIClient()
        resp = client.get("/api/shop/catalog/")
        self.assertEqual(resp.status_code, 401)

    def test_purchase_requires_auth(self):
        client = APIClient()
        resp = client.post("/api/shop/purchase-offer/", {"offer_id": "item:1"})
        self.assertEqual(resp.status_code, 401)

    def test_wallet_requires_auth(self):
        client = APIClient()
        resp = client.get("/api/wallet/")
        self.assertEqual(resp.status_code, 401)


# ---------------------------------------------------------------------------
# Account reset
# ---------------------------------------------------------------------------

class ResetAccountTests(ShopTestBase):

    def setUp(self):
        super().setUp()
        self.ua_hat = UserAccessory.objects.create(
            player=self.profile, accessory=self.hat, is_equipped=True
        )
        self.ua_cape = UserAccessory.objects.create(
            player=self.profile, accessory=self.cape
        )
        achievement = Achievement.objects.create(
            key="test-ach", title="Test", description="Test",
            condition_type="one_time_trigger", trigger_event="purchase",
        )
        UserAchievement.objects.create(
            player=self.profile, achievement=achievement,
            current_count=1, is_unlocked=True,
        )
        DailyAttendance.objects.create(
            player=self.profile, date=date(2026, 10, 1)
        )
        self.wallet_gold.amount = 200
        self.wallet_gold.save()
        self.wallet_snow.amount = 30
        self.wallet_snow.save()

    @override_settings(DEBUG=True)
    def test_reset_restores_wallet_balances(self):
        resp = self.client.post("/api/auth/reset/")
        self.assertEqual(resp.status_code, 200)

        self.wallet_gold.refresh_from_db()
        self.wallet_snow.refresh_from_db()
        self.wallet_gears.refresh_from_db()
        self.assertEqual(self.wallet_gold.amount, 5000)
        self.assertEqual(self.wallet_snow.amount, 500)
        self.assertEqual(self.wallet_gears.amount, 100)

    @override_settings(DEBUG=True)
    def test_reset_deletes_owned_accessories(self):
        resp = self.client.post("/api/auth/reset/")
        self.assertEqual(resp.status_code, 200)
        self.assertEqual(
            UserAccessory.objects.filter(player=self.profile).count(), 0
        )

    @override_settings(DEBUG=True)
    def test_reset_deletes_achievements(self):
        resp = self.client.post("/api/auth/reset/")
        self.assertEqual(resp.status_code, 200)
        self.assertEqual(
            UserAchievement.objects.filter(player=self.profile).count(), 0
        )

    @override_settings(DEBUG=True)
    def test_reset_deletes_daily_attendance(self):
        resp = self.client.post("/api/auth/reset/")
        self.assertEqual(resp.status_code, 200)
        self.assertEqual(
            DailyAttendance.objects.filter(player=self.profile).count(), 0
        )

    @override_settings(DEBUG=False)
    def test_reset_blocked_in_production(self):
        resp = self.client.post("/api/auth/reset/")
        self.assertEqual(resp.status_code, 404)

    @override_settings(DEBUG=True)
    def test_reset_does_not_affect_other_players(self):
        other_user = User.objects.create_user(
            username="otherplayer", password="pass"
        )
        other_profile = PlayerProfile.objects.create(
            user=other_user, platform_id="plat_002"
        )
        other_wallet = Wallet.objects.create(
            player=other_profile, currency=self.gold, amount=999
        )
        UserAccessory.objects.create(
            player=other_profile, accessory=self.hat
        )

        self.client.post("/api/auth/reset/")

        other_wallet.refresh_from_db()
        self.assertEqual(other_wallet.amount, 999)
        self.assertEqual(
            UserAccessory.objects.filter(player=other_profile).count(), 1
        )


# ---------------------------------------------------------------------------
# Achievement Validation
# ---------------------------------------------------------------------------

class ThrottleConfigTests(TestCase):
    def test_economy_throttle_scope_configured(self):
        from shop.throttles import EconomyMutationThrottle
        self.assertEqual(EconomyMutationThrottle.scope, "economy_mutation")

    def test_claim_throttle_scope_configured(self):
        from shop.throttles import ClaimThrottle
        self.assertEqual(ClaimThrottle.scope, "claim")

    def test_purchase_throttled_after_burst(self):
        from django.core.cache import cache
        from rest_framework.test import APIClient as FreshClient

        # Clear cache to ensure clean state
        cache.clear()

        user = User.objects.create_user(username="throttleuser", password="pass")
        profile = PlayerProfile.objects.create(user=user, platform_id="throttle_plat")
        gold = Currency.objects.create(name="gold")
        # Give enough gold for 31 purchases
        Wallet.objects.create(player=profile, currency=gold, amount=50000)
        client = FreshClient()
        token = RefreshToken.for_user(user)
        client.credentials(HTTP_AUTHORIZATION=f"Bearer {token.access_token}")

        # Default throttle is 30/min, so make 30 successful purchases
        for i in range(30):
            accessory = Accessory.objects.create(name=f"Test Hat {i}", type="headwear")
            AccessoryCost.objects.create(accessory=accessory, currency=gold, amount=1)
            resp = client.post("/api/shop/purchase-offer/", {"offer_id": f"item:{accessory.id}"}, format="json")
            self.assertEqual(resp.status_code, 200, f"Request {i+1} should succeed")

        # The 31st request should be throttled
        final_accessory = Accessory.objects.create(name="Final Hat", type="headwear")
        AccessoryCost.objects.create(accessory=final_accessory, currency=gold, amount=1)
        resp_throttled = client.post("/api/shop/purchase-offer/", {"offer_id": f"item:{final_accessory.id}"}, format="json")
        self.assertEqual(resp_throttled.status_code, 429)
