from unittest.mock import patch

from django.core.cache import cache
from django.contrib.auth.models import User
from django.test import TestCase, override_settings
from rest_framework.test import APIClient
from rest_framework_simplejwt.tokens import AccessToken

from shop.models import Currency, PlayerProfile, Wallet


class PasswordLoginTests(TestCase):
    def setUp(self):
        cache.clear()
        self.user = User.objects.create_user(username="player", password="A real password 123!")
        Currency.objects.create(name="gold")

    def login(self, username="player", password="A real password 123!"):
        return self.client.post("/api/auth/password/", {"username": username, "password": password}, content_type="application/json")

    @override_settings(DEBUG=False)
    def test_login_verifies_password_and_issues_usable_tokens_in_production(self):
        response = self.login()
        self.assertEqual(response.status_code, 200)
        data = response.json()
        self.assertEqual(data["username"], self.user.username)
        self.assertEqual(data["player_id"], str(self.user.player_profile.pk))
        self.assertEqual(AccessToken(data["access"])["user_id"], self.user.pk)
        self.assertNotIn("password", data)
        self.assertEqual(Wallet.objects.get(player=self.user.player_profile).amount, 0)
        client = APIClient()
        client.credentials(HTTP_AUTHORIZATION="Bearer " + data["access"])
        self.assertEqual(client.get("/api/wallet/").status_code, 200)
        refreshed = self.client.post("/api/auth/refresh/", {"refresh": data["refresh"]}, content_type="application/json")
        self.assertEqual(refreshed.status_code, 200)

    def test_login_reuses_identity_and_preserves_wallet_after_username_change(self):
        first = self.login().json()
        wallet = Wallet.objects.get(player=self.user.player_profile)
        wallet.amount = 350
        wallet.save()
        self.user.username = "renamed"
        self.user.save()
        second = self.login(username="renamed").json()
        self.assertEqual(first["player_id"], second["player_id"])
        self.assertEqual(second["username"], "renamed")
        wallet.refresh_from_db()
        self.assertEqual(wallet.amount, 350)
        self.assertEqual(PlayerProfile.objects.count(), 1)

    def test_wrong_password_unknown_user_and_inactive_user_return_same_rejection(self):
        wrong = self.login(password="wrong")
        unknown = self.login(username="unknown")
        self.user.is_active = False
        self.user.save()
        inactive = self.login()
        for response in (wrong, unknown, inactive):
            self.assertEqual(response.status_code, 401)
            self.assertEqual(response.json(), {"code": "authentication_rejected", "message": "Invalid username or password."})
        self.assertEqual(PlayerProfile.objects.count(), 0)
        self.assertEqual(User.objects.count(), 1)

    def test_invalid_request_fields_are_rejected_before_profile_creation(self):
        for payload in ({}, {"username": "player"}, {"username": 42, "password": "x"}, {"username": "player", "password": []}, {"username": "player", "password": ""}, {"username": "x" * 151, "password": "x"}, {"username": "player", "password": "x" * 4097}, []):
            with self.subTest(payload_type=type(payload).__name__):
                response = self.client.post("/api/auth/password/", payload, content_type="application/json")
                self.assertEqual(response.status_code, 400)
                self.assertNotIn("access", response.json())
        self.assertEqual(PlayerProfile.objects.count(), 0)

    def test_existing_profile_is_preserved(self):
        profile = PlayerProfile.objects.create(user=self.user, platform_id="external-player")
        self.assertEqual(self.login().json()["player_id"], str(profile.pk))
        self.assertEqual(PlayerProfile.objects.get(user=self.user).platform_id, "external-player")

    def test_profile_provisioning_rolls_back_when_wallet_creation_fails(self):
        with patch("shop.views.auth_views.Wallet.objects.create", side_effect=RuntimeError("Database failed")):
            with self.assertRaises(RuntimeError):
                self.login()
        self.assertFalse(PlayerProfile.objects.filter(user=self.user).exists())

    @override_settings(DEBUG=True)
    def test_development_endpoint_cannot_bypass_managed_password(self):
        PlayerProfile.objects.create(user=self.user, platform_id="dev_player")
        response = self.client.post("/api/auth/dev/", {"username": "player"}, content_type="application/json")
        self.assertEqual(response.status_code, 401)
        self.assertNotIn("access", response.json())

    @patch("shop.throttles.PasswordLoginThrottle.rate", "2/min", create=True)
    def test_login_attempts_are_rate_limited(self):
        self.assertEqual(self.login(password="wrong").status_code, 401)
        self.assertEqual(self.login(password="wrong").status_code, 401)
        self.assertEqual(self.login(password="wrong").status_code, 429)
