from datetime import timedelta
from django.contrib.auth.models import User
from django.test import TestCase
from rest_framework_simplejwt.tokens import RefreshToken


class SessionRenewalTests(TestCase):
    def setUp(self):
        self.user = User.objects.create_user(username="renewal", password="password")
        self.token = RefreshToken.for_user(self.user)

    def renew(self):
        return self.client.post("/api/auth/refresh/", {"refresh": str(self.token)}, content_type="application/json")

    def test_expired_refresh_is_rejected(self):
        self.token.set_exp(lifetime=timedelta(seconds=-1))
        self.assertEqual(self.renew().status_code, 401)

    def test_inactive_user_is_rejected(self):
        self.user.is_active = False
        self.user.save()
        self.assertEqual(self.renew().status_code, 401)

    def test_deleted_user_is_rejected_without_server_error(self):
        self.user.delete()
        self.assertEqual(self.renew().status_code, 401)
