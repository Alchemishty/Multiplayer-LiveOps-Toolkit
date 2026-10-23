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

class PurchaseTests(ShopTestBase):

    def test_purchase_happy_path(self):
        resp = self.client.post(
            "/api/shop/purchase/", {"accessory_id": self.hat.id}, format="json"
        )
        self.assertEqual(resp.status_code, 200)

        self.wallet_gold.refresh_from_db()
        self.assertEqual(self.wallet_gold.amount, 400)

        self.assertTrue(
            UserAccessory.objects.filter(
                player=self.profile, accessory=self.hat
            ).exists()
        )

    def test_purchase_insufficient_funds(self):
        resp = self.client.post(
            "/api/shop/purchase/",
            {"accessory_id": self.expensive.id},
            format="json",
        )
        self.assertEqual(resp.status_code, 400)
        self.assertIn("Not enough", resp.data["message"])

        self.wallet_gold.refresh_from_db()
        self.assertEqual(self.wallet_gold.amount, 500)

        self.assertFalse(
            UserAccessory.objects.filter(
                player=self.profile, accessory=self.expensive
            ).exists()
        )

    def test_purchase_already_owned(self):
        UserAccessory.objects.create(player=self.profile, accessory=self.hat)

        resp = self.client.post(
            "/api/shop/purchase/", {"accessory_id": self.hat.id}, format="json"
        )
        self.assertEqual(resp.status_code, 400)
        self.assertIn("already owned", resp.data["message"].lower())

        self.wallet_gold.refresh_from_db()
        self.assertEqual(self.wallet_gold.amount, 500)

    def test_purchase_multi_currency_cost(self):
        resp = self.client.post(
            "/api/shop/purchase/", {"accessory_id": self.crown.id}, format="json"
        )
        self.assertEqual(resp.status_code, 200)

        self.wallet_gold.refresh_from_db()
        self.wallet_gears.refresh_from_db()
        self.assertEqual(self.wallet_gold.amount, 350)
        self.assertEqual(self.wallet_gears.amount, 7)

    def test_purchase_multi_currency_partial_insufficient(self):
        """If one currency is short, nothing is deducted."""
        self.wallet_gears.amount = 1
        self.wallet_gears.save()

        resp = self.client.post(
            "/api/shop/purchase/", {"accessory_id": self.crown.id}, format="json"
        )
        self.assertEqual(resp.status_code, 400)

        self.wallet_gold.refresh_from_db()
        self.wallet_gears.refresh_from_db()
        self.assertEqual(self.wallet_gold.amount, 500)
        self.assertEqual(self.wallet_gears.amount, 1)

    def test_purchase_nonexistent_accessory(self):
        resp = self.client.post(
            "/api/shop/purchase/", {"accessory_id": 99999}, format="json"
        )
        self.assertEqual(resp.status_code, 404)

    def test_purchase_missing_id(self):
        resp = self.client.post("/api/shop/purchase/", {}, format="json")
        self.assertEqual(resp.status_code, 400)


# ---------------------------------------------------------------------------
# Equip flow
# ---------------------------------------------------------------------------

class EquipTests(ShopTestBase):

    def setUp(self):
        super().setUp()
        self.ua_hat = UserAccessory.objects.create(
            player=self.profile, accessory=self.hat
        )
        self.ua_cape = UserAccessory.objects.create(
            player=self.profile, accessory=self.cape
        )
        self.ua_crown = UserAccessory.objects.create(
            player=self.profile, accessory=self.crown
        )

    def test_equip_happy_path(self):
        resp = self.client.post(
            "/api/accessories/update-equipped/",
            {"accessory_ids": [self.hat.id, self.cape.id]},
            format="json",
        )
        self.assertEqual(resp.status_code, 200)

        equipped = set(
            UserAccessory.objects.filter(
                player=self.profile, is_equipped=True
            ).values_list("accessory_id", flat=True)
        )
        self.assertEqual(equipped, {self.hat.id, self.cape.id})

    def test_equip_replaces_previous(self):
        self.ua_hat.is_equipped = True
        self.ua_hat.save()

        resp = self.client.post(
            "/api/accessories/update-equipped/",
            {"accessory_ids": [self.cape.id]},
            format="json",
        )
        self.assertEqual(resp.status_code, 200)

        self.ua_hat.refresh_from_db()
        self.ua_cape.refresh_from_db()
        self.assertFalse(self.ua_hat.is_equipped)
        self.assertTrue(self.ua_cape.is_equipped)

    def test_equip_unowned_fails_without_side_effects(self):
        """If any ID is unowned, nothing changes."""
        self.ua_hat.is_equipped = True
        self.ua_hat.save()

        resp = self.client.post(
            "/api/accessories/update-equipped/",
            {"accessory_ids": [self.cape.id, self.expensive.id]},
            format="json",
        )
        self.assertEqual(resp.status_code, 400)

        self.ua_hat.refresh_from_db()
        self.assertTrue(self.ua_hat.is_equipped)

    def test_get_equipped(self):
        self.ua_hat.is_equipped = True
        self.ua_hat.save()
        self.ua_crown.is_equipped = True
        self.ua_crown.save()

        resp = self.client.get("/api/accessories/get-equipped/")
        self.assertEqual(resp.status_code, 200)
        self.assertCountEqual(
            resp.data["accessory_ids"], [self.hat.id, self.crown.id]
        )

    def test_equip_empty_list_unequips_all(self):
        self.ua_hat.is_equipped = True
        self.ua_hat.save()

        resp = self.client.post(
            "/api/accessories/update-equipped/",
            {"accessory_ids": []},
            format="json",
        )
        self.assertEqual(resp.status_code, 200)

        equipped_count = UserAccessory.objects.filter(
            player=self.profile, is_equipped=True
        ).count()
        self.assertEqual(equipped_count, 0)


# ---------------------------------------------------------------------------
# Equip Security
# ---------------------------------------------------------------------------

class EquipSecurityTests(ShopTestBase):

    def setUp(self):
        super().setUp()
        self.ua_hat = UserAccessory.objects.create(
            player=self.profile, accessory=self.hat
        )
        self.ua_cape = UserAccessory.objects.create(
            player=self.profile, accessory=self.cape
        )

    def test_equip_unowned_id_rejected_atomically(self):
        """Unowned ID in list means nothing changes — atomic rollback."""
        self.ua_hat.is_equipped = True
        self.ua_hat.save()

        resp = self.client.post(
            "/api/accessories/update-equipped/",
            {"accessory_ids": [self.hat.id, 99999]},
            format="json",
        )
        self.assertEqual(resp.status_code, 400)
        self.ua_hat.refresh_from_db()
        self.assertTrue(self.ua_hat.is_equipped)

    def test_equip_duplicate_slot_keeps_last(self):
        """Two headwear items — only the last one in the list is equipped."""
        self.ua_crown = UserAccessory.objects.create(
            player=self.profile, accessory=self.crown
        )
        resp = self.client.post(
            "/api/accessories/update-equipped/",
            {"accessory_ids": [self.hat.id, self.crown.id, self.cape.id]},
            format="json",
        )
        self.assertEqual(resp.status_code, 200)
        equipped = set(
            UserAccessory.objects.filter(
                player=self.profile, is_equipped=True
            ).values_list("accessory_id", flat=True)
        )
        self.assertIn(self.cape.id, equipped)
        self.assertIn(self.crown.id, equipped)
        self.assertNotIn(self.hat.id, equipped)

    def test_equip_nonexistent_accessory_rejected(self):
        resp = self.client.post(
            "/api/accessories/update-equipped/",
            {"accessory_ids": [99999]},
            format="json",
        )
        self.assertEqual(resp.status_code, 400)


# ---------------------------------------------------------------------------
# Shop listing
# ---------------------------------------------------------------------------

class ShopListingTests(ShopTestBase):

    def test_list_accessories_grouped_by_type(self):
        resp = self.client.get("/api/shop/accessories/")
        self.assertEqual(resp.status_code, 200)
        self.assertIn("headwear", resp.data["accessories"])
        self.assertIn("back", resp.data["accessories"])

    def test_list_shows_ownership_status(self):
        UserAccessory.objects.create(player=self.profile, accessory=self.hat)

        resp = self.client.get("/api/shop/accessories/")
        headwear_items = resp.data["accessories"]["headwear"]
        hat_item = next(i for i in headwear_items if i["id"] == self.hat.id)
        crown_item = next(i for i in headwear_items if i["id"] == self.crown.id)

        self.assertTrue(hat_item["is_owned"])
        self.assertFalse(crown_item["is_owned"])

    def test_list_includes_costs(self):
        resp = self.client.get("/api/shop/accessories/")
        headwear_items = resp.data["accessories"]["headwear"]
        crown_item = next(i for i in headwear_items if i["id"] == self.crown.id)
        self.assertEqual(len(crown_item["costs"]), 2)


# ---------------------------------------------------------------------------
# Wallet & currency conversion
# ---------------------------------------------------------------------------

class WalletTests(ShopTestBase):

    def test_get_wallet_balances(self):
        resp = self.client.get("/api/wallet/")
        self.assertEqual(resp.status_code, 200)
        self.assertEqual(len(resp.data), 3)

    def test_convert_snowflakes_to_gold(self):
        resp = self.client.post(
            "/api/shop/convert-currency/",
            {"currency_type": "snowflakes", "amount": 20},
            format="json",
        )
        self.assertEqual(resp.status_code, 200)
        self.assertEqual(resp.data["gold_amount"], 10)

        self.wallet_snow.refresh_from_db()
        self.wallet_gold.refresh_from_db()
        self.assertEqual(self.wallet_snow.amount, 80)
        self.assertEqual(self.wallet_gold.amount, 510)

    def test_convert_gears_to_gold(self):
        resp = self.client.post(
            "/api/shop/convert-currency/",
            {"currency_type": "gears", "amount": 2},
            format="json",
        )
        self.assertEqual(resp.status_code, 200)
        self.assertEqual(resp.data["gold_amount"], 50)

        self.wallet_gears.refresh_from_db()
        self.wallet_gold.refresh_from_db()
        self.assertEqual(self.wallet_gears.amount, 8)
        self.assertEqual(self.wallet_gold.amount, 550)

    def test_convert_insufficient_funds(self):
        resp = self.client.post(
            "/api/shop/convert-currency/",
            {"currency_type": "snowflakes", "amount": 9999},
            format="json",
        )
        self.assertEqual(resp.status_code, 400)

        self.wallet_snow.refresh_from_db()
        self.assertEqual(self.wallet_snow.amount, 100)

    def test_convert_invalid_currency(self):
        resp = self.client.post(
            "/api/shop/convert-currency/",
            {"currency_type": "diamonds", "amount": 1},
            format="json",
        )
        self.assertEqual(resp.status_code, 400)

    def test_convert_zero_amount(self):
        resp = self.client.post(
            "/api/shop/convert-currency/",
            {"currency_type": "gears", "amount": 0},
            format="json",
        )
        self.assertEqual(resp.status_code, 400)


# ---------------------------------------------------------------------------
# Daily login streak
# ---------------------------------------------------------------------------

class DailyLoginCalendarTests(ShopTestBase):

    def test_calendar_auto_generates_rewards_for_month(self):
        resp = self.client.get("/api/daily-login/calendar/?year=2026&month=10")
        self.assertEqual(resp.status_code, 200)
        self.assertEqual(resp.data["year"], 2026)
        self.assertEqual(resp.data["month"], 10)
        self.assertEqual(len(resp.data["days"]), 31)
        self.assertTrue(
            MonthlyRewardSchedule.objects.filter(year=2026, month=10).exists()
        )

    def test_calendar_shows_claimed_days(self):
        DailyAttendance.objects.create(player=self.profile, date=date(2026, 10, 1))
        DailyAttendance.objects.create(player=self.profile, date=date(2026, 10, 2))

        resp = self.client.get("/api/daily-login/calendar/?year=2026&month=10")
        days = resp.data["days"]
        self.assertTrue(days[0]["is_claimed"])
        self.assertTrue(days[1]["is_claimed"])
        self.assertFalse(days[2]["is_claimed"])

    def test_calendar_includes_reward_info(self):
        MonthlyRewardSchedule.objects.create(
            year=2026, month=10, day=1, currency=self.gold, reward_amount=75,
        )
        resp = self.client.get("/api/daily-login/calendar/?year=2026&month=10")
        day1 = resp.data["days"][0]
        self.assertEqual(day1["reward"]["currency"], "gold")
        self.assertEqual(day1["reward"]["amount"], 75)

    def test_calendar_defaults_to_current_month(self):
        resp = self.client.get("/api/daily-login/calendar/")
        self.assertEqual(resp.status_code, 200)
        today = date.today()
        self.assertEqual(resp.data["year"], today.year)
        self.assertEqual(resp.data["month"], today.month)

    def test_calendar_today_field_only_set_for_current_month(self):
        resp = self.client.get("/api/daily-login/calendar/?year=2025&month=1")
        self.assertEqual(resp.data["today"], 0)


class DailyLoginClaimTests(ShopTestBase):

    @patch("shop.views.rewards_views.date")
    def test_claim_grants_reward_and_creates_attendance(self, mock_date):
        mock_date.today.return_value = date(2026, 10, 15)
        mock_date.side_effect = lambda *a, **kw: date(*a, **kw)
        MonthlyRewardSchedule.objects.create(
            year=2026, month=10, day=15, currency=self.gold, reward_amount=100,
        )

        resp = self.client.post("/api/daily-login/claim/")
        self.assertEqual(resp.status_code, 200)
        self.assertEqual(resp.data["day"], 15)
        self.assertEqual(resp.data["reward"]["currency"], "gold")
        self.assertEqual(resp.data["reward"]["amount"], 100)

        self.wallet_gold.refresh_from_db()
        self.assertEqual(self.wallet_gold.amount, 600)
        self.assertTrue(
            DailyAttendance.objects.filter(
                player=self.profile, date=date(2026, 10, 15)
            ).exists()
        )

    @patch("shop.views.rewards_views.date")
    def test_claim_already_claimed_today(self, mock_date):
        mock_date.today.return_value = date(2026, 10, 15)
        mock_date.side_effect = lambda *a, **kw: date(*a, **kw)
        DailyAttendance.objects.create(player=self.profile, date=date(2026, 10, 15))

        resp = self.client.post("/api/daily-login/claim/")
        self.assertEqual(resp.status_code, 400)
        self.assertIn("Already claimed", resp.data["message"])

    @patch("shop.views.rewards_views.date")
    def test_claim_creates_wallet_if_missing(self, mock_date):
        mock_date.today.return_value = date(2026, 10, 1)
        mock_date.side_effect = lambda *a, **kw: date(*a, **kw)
        new_currency = Currency.objects.create(name="rubies")
        MonthlyRewardSchedule.objects.create(
            year=2026, month=10, day=1, currency=new_currency, reward_amount=5,
        )

        resp = self.client.post("/api/daily-login/claim/")
        self.assertEqual(resp.status_code, 200)
        wallet = Wallet.objects.get(player=self.profile, currency=new_currency)
        self.assertEqual(wallet.amount, 5)

    @patch("shop.views.rewards_views.date")
    def test_claim_two_different_days(self, mock_date):
        mock_date.today.return_value = date(2026, 10, 1)
        mock_date.side_effect = lambda *a, **kw: date(*a, **kw)
        MonthlyRewardSchedule.objects.create(
            year=2026, month=10, day=1, currency=self.gold, reward_amount=50,
        )
        MonthlyRewardSchedule.objects.create(
            year=2026, month=10, day=2, currency=self.gold, reward_amount=75,
        )

        self.client.post("/api/daily-login/claim/")
        mock_date.today.return_value = date(2026, 10, 2)
        self.client.post("/api/daily-login/claim/")

        self.wallet_gold.refresh_from_db()
        self.assertEqual(self.wallet_gold.amount, 625)
        self.assertEqual(
            DailyAttendance.objects.filter(player=self.profile).count(), 2
        )


class DailyLoginRaceTests(ShopTestBase):

    @patch("shop.views.rewards_views.date")
    def test_double_claim_returns_400_on_second(self, mock_date):
        mock_date.today.return_value = date(2026, 10, 15)
        mock_date.side_effect = lambda *a, **kw: date(*a, **kw)
        MonthlyRewardSchedule.objects.create(
            year=2026, month=10, day=15, currency=self.gold, reward_amount=100,
        )
        resp1 = self.client.post("/api/daily-login/claim/")
        self.assertEqual(resp1.status_code, 200)
        resp2 = self.client.post("/api/daily-login/claim/")
        self.assertEqual(resp2.status_code, 400)
        self.wallet_gold.refresh_from_db()
        self.assertEqual(self.wallet_gold.amount, 600)
        self.assertEqual(
            DailyAttendance.objects.filter(
                player=self.profile, date=date(2026, 10, 15)
            ).count(), 1
        )


# ---------------------------------------------------------------------------
# Dev auth (DEBUG-only bypass)
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

    def test_shop_requires_auth(self):
        client = APIClient()
        resp = client.get("/api/shop/accessories/")
        self.assertEqual(resp.status_code, 401)

    def test_purchase_requires_auth(self):
        client = APIClient()
        resp = client.post("/api/shop/purchase/", {"accessory_id": 1})
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

class AchievementValidationTests(ShopTestBase):

    def setUp(self):
        super().setUp()
        self.first_purchase = Achievement.objects.create(
            key="first-purchase", title="Window Shopper",
            description="Make your first purchase",
            condition_type="one_time_trigger",
            trigger_event="purchase", target_count=0,
            is_active=True,
        )
        self.buy_5 = Achievement.objects.create(
            key="buy-5", title="Regular Customer",
            description="Purchase 5 accessories",
            condition_type="threshold_counter",
            trigger_event="purchase", target_count=5,
            is_active=True,
        )
        self.all_headwear = Achievement.objects.create(
            key="all-headwear", title="Hat Trick",
            description="Own every headwear accessory",
            condition_type="collection_completion",
            trigger_event="purchase", target_count=0,
            target_slot="headwear", is_active=True,
        )
        self.login_7 = Achievement.objects.create(
            key="login-7", title="Dedicated Player",
            description="Log in 7 times",
            condition_type="threshold_counter",
            trigger_event="login", target_count=7,
            is_active=True,
        )
        self.spend_gold = Achievement.objects.create(
            key="spend-gold-500", title="Gold Digger",
            description="Spend 500 gold",
            condition_type="threshold_counter",
            trigger_event="currency_spent", target_count=500,
            target_currency="gold", is_active=True,
        )

    def test_unlock_one_time_trigger_with_no_purchases_rejected(self):
        resp = self.client.post(
            "/api/achievements/unlock/",
            {"achievement_id": "first-purchase", "current_count": 1},
            format="json",
        )
        self.assertEqual(resp.status_code, 400)
        self.assertIn("not met", resp.data["message"].lower())

    def test_unlock_one_time_trigger_with_purchase_accepted(self):
        UserAccessory.objects.create(player=self.profile, accessory=self.hat)
        resp = self.client.post(
            "/api/achievements/unlock/",
            {"achievement_id": "first-purchase", "current_count": 1},
            format="json",
        )
        self.assertEqual(resp.status_code, 200)
        ua = UserAchievement.objects.get(
            player=self.profile, achievement=self.first_purchase
        )
        self.assertTrue(ua.is_unlocked)

    def test_unlock_threshold_counter_with_fabricated_count_rejected(self):
        UserAccessory.objects.create(player=self.profile, accessory=self.hat)
        resp = self.client.post(
            "/api/achievements/unlock/",
            {"achievement_id": "buy-5", "current_count": 999},
            format="json",
        )
        self.assertEqual(resp.status_code, 400)

    def test_unlock_threshold_counter_when_met(self):
        for acc in [self.hat, self.cape, self.crown, self.expensive]:
            UserAccessory.objects.create(player=self.profile, accessory=acc)
        fifth = Accessory.objects.create(name="Fifth", type="back")
        UserAccessory.objects.create(player=self.profile, accessory=fifth)
        resp = self.client.post(
            "/api/achievements/unlock/",
            {"achievement_id": "buy-5", "current_count": 5},
            format="json",
        )
        self.assertEqual(resp.status_code, 200)

    def test_unlock_collection_completion_incomplete_rejected(self):
        UserAccessory.objects.create(player=self.profile, accessory=self.hat)
        resp = self.client.post(
            "/api/achievements/unlock/",
            {"achievement_id": "all-headwear", "current_count": 1},
            format="json",
        )
        self.assertEqual(resp.status_code, 400)

    def test_unlock_collection_completion_when_complete(self):
        UserAccessory.objects.create(player=self.profile, accessory=self.hat)
        UserAccessory.objects.create(player=self.profile, accessory=self.crown)
        resp = self.client.post(
            "/api/achievements/unlock/",
            {"achievement_id": "all-headwear", "current_count": 2},
            format="json",
        )
        self.assertEqual(resp.status_code, 200)

    def test_unlock_login_threshold_not_met(self):
        resp = self.client.post(
            "/api/achievements/unlock/",
            {"achievement_id": "login-7", "current_count": 7},
            format="json",
        )
        self.assertEqual(resp.status_code, 400)

    def test_unlock_login_threshold_met(self):
        from datetime import timedelta
        for i in range(7):
            DailyAttendance.objects.create(
                player=self.profile,
                date=date.today() - timedelta(days=i),
            )
        resp = self.client.post(
            "/api/achievements/unlock/",
            {"achievement_id": "login-7", "current_count": 7},
            format="json",
        )
        self.assertEqual(resp.status_code, 200)

    def test_progress_update_uses_server_count(self):
        UserAccessory.objects.create(player=self.profile, accessory=self.hat)
        UserAccessory.objects.create(player=self.profile, accessory=self.cape)
        resp = self.client.post(
            "/api/achievements/progress/update/",
            {"achievement_id": "buy-5", "current_count": 999},
            format="json",
        )
        self.assertEqual(resp.status_code, 200)
        ua = UserAchievement.objects.get(
            player=self.profile, achievement=self.buy_5
        )
        self.assertEqual(ua.current_count, 2)

    def test_unlock_already_unlocked_returns_400(self):
        UserAccessory.objects.create(player=self.profile, accessory=self.hat)
        UserAchievement.objects.create(
            player=self.profile, achievement=self.first_purchase,
            is_unlocked=True, current_count=1,
        )
        resp = self.client.post(
            "/api/achievements/unlock/",
            {"achievement_id": "first-purchase", "current_count": 1},
            format="json",
        )
        self.assertEqual(resp.status_code, 400)
        self.assertIn("already unlocked", resp.data["message"].lower())


# ---------------------------------------------------------------------------
# Throttle Configuration Tests
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
            resp = client.post("/api/shop/purchase/", {"accessory_id": accessory.id}, format="json")
            self.assertEqual(resp.status_code, 200, f"Request {i+1} should succeed")

        # The 31st request should be throttled
        final_accessory = Accessory.objects.create(name="Final Hat", type="headwear")
        AccessoryCost.objects.create(accessory=final_accessory, currency=gold, amount=1)
        resp_throttled = client.post("/api/shop/purchase/", {"accessory_id": final_accessory.id}, format="json")
        self.assertEqual(resp_throttled.status_code, 429)


# ---------------------------------------------------------------------------
# Calendar bounds validation
# ---------------------------------------------------------------------------

class CalendarBoundsTests(ShopTestBase):

    def test_calendar_rejects_invalid_month(self):
        resp = self.client.get("/api/daily-login/calendar/?month=13")
        self.assertEqual(resp.status_code, 400)

    def test_calendar_rejects_month_zero(self):
        resp = self.client.get("/api/daily-login/calendar/?month=0")
        self.assertEqual(resp.status_code, 400)

    def test_calendar_rejects_far_future_year(self):
        resp = self.client.get("/api/daily-login/calendar/?year=9999")
        self.assertEqual(resp.status_code, 400)

    def test_calendar_rejects_non_numeric(self):
        resp = self.client.get("/api/daily-login/calendar/?month=abc")
        self.assertEqual(resp.status_code, 400)

    def test_calendar_accepts_valid_range(self):
        resp = self.client.get("/api/daily-login/calendar/?year=2026&month=6")
        self.assertEqual(resp.status_code, 200)
