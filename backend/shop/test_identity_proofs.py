import base64
import io
import json
import os
import tempfile
from pathlib import Path
from unittest.mock import patch

import jwt
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import rsa
from django.contrib.auth.models import User
from django.core.cache import cache
from django.core.management import call_command
from django.test import TestCase
from rest_framework.test import APIClient

from shop.models import PlayerProfile
from shop.services.identity_proofs import IdentityProofIssuer


class IdentityProofTests(TestCase):
    @classmethod
    def setUpClass(cls):
        super().setUpClass()
        cls.directory = tempfile.TemporaryDirectory()
        cls.private_key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
        cls.key_file = Path(cls.directory.name) / "signer.pem"
        cls.key_file.write_bytes(cls.private_key.private_bytes(
            serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8,
            serialization.NoEncryption(),
        ))
        os.chmod(cls.key_file, 0o600)

    @classmethod
    def tearDownClass(cls):
        cls.directory.cleanup()
        super().tearDownClass()

    def setUp(self):
        cache.clear()
        self.user = User.objects.create_user(username="verified-player")
        self.profile = PlayerProfile.objects.create(user=self.user, platform_id="proof-player")
        self.client = APIClient()
        self.client.force_authenticate(self.user)
        self.challenge = base64.urlsafe_b64encode(bytes(range(32))).rstrip(b"=").decode()
        self.configuration = {
            "private_key_file": str(self.key_file), "key_id": "key-2026",
            "issuer": "https://identity.example.test", "game_id": "sample-game",
            "environment": "test", "content_version": "catalog-1", "lifetime_seconds": 60,
        }
        self.override = self.settings(LIVEOPS_IDENTITY_PROOF=self.configuration)
        self.override.enable()
        self.addCleanup(self.override.disable)

    def request_proof(self, **extra):
        return self.client.post("/api/auth/identity-proof/", {
            "session_id": "session-1", "challenge": self.challenge, **extra,
        }, format="json")

    def test_authenticated_account_gets_scoped_rs256_assertion(self):
        response = self.request_proof()
        self.assertEqual(response.status_code, 200)
        assertion = response.json()["assertion"]
        header = jwt.get_unverified_header(assertion)
        self.assertEqual(header, {"alg": "RS256", "typ": "liveops-identity+jwt", "kid": "key-2026"})
        claims = jwt.decode(assertion, self.private_key.public_key(), algorithms=["RS256"],
                            issuer=self.configuration["issuer"], audience="sample-game")
        self.assertEqual(claims["sub"], str(self.profile.pk))
        self.assertEqual(claims["name"], self.user.username)
        self.assertEqual(claims["sid"], "session-1")
        self.assertEqual(claims["nonce"], self.challenge)
        self.assertEqual(claims["env"], "test")
        self.assertEqual(claims["content"], "catalog-1")
        self.assertEqual(claims["ver"], 1)
        self.assertEqual(claims["exp"] - claims["iat"], 60)
        self.assertEqual(claims["nbf"], claims["iat"])
        self.assertEqual(set(claims), {"iss", "aud", "env", "sub", "name", "sid", "nonce", "iat", "nbf", "exp", "jti", "ver", "content"})
        second = self.request_proof().json()["assertion"]
        self.assertNotEqual(claims["jti"], jwt.decode(second, options={"verify_signature": False})["jti"])

    def test_anonymous_and_inactive_accounts_are_rejected(self):
        self.client.force_authenticate(None)
        self.assertEqual(self.request_proof().status_code, 401)
        self.user.is_active = False
        self.user.save()
        self.client.force_authenticate(self.user)
        self.assertEqual(self.request_proof().status_code, 401)

    def test_caller_cannot_supply_identity_or_private_claims(self):
        for field in ("sub", "player_id", "name", "aud", "env", "exp", "access"):
            with self.subTest(field=field):
                response = self.request_proof(**{field: "attacker-choice"})
                self.assertEqual(response.status_code, 400)
                self.assertNotIn("assertion", response.json())

    def test_invalid_session_or_challenge_is_rejected(self):
        cases = [{"session_id": ""}, {"session_id": "x" * 129}, {"session_id": "with spaces"},
                 {"challenge": ""}, {"challenge": "a" * 42}, {"challenge": "!" * 43},
                 {"challenge": 12}, {"session_id": []}]
        for payload in cases:
            with self.subTest(fields=tuple(payload)):
                self.assertEqual(self.request_proof(**payload).status_code, 400)

    def test_uninitialized_account_does_not_get_a_proof(self):
        self.profile.delete()
        self.assertEqual(self.request_proof().status_code, 409)
        self.assertFalse(PlayerProfile.objects.filter(user=self.user).exists())

    def test_missing_or_invalid_key_configuration_fails_closed(self):
        for configuration in ({}, {**self.configuration, "private_key_file": "/missing/private-proof-key.pem"},
                              {**self.configuration, "lifetime_seconds": 0},
                              {**self.configuration, "lifetime_seconds": 301},
                              {**self.configuration, "lifetime_seconds": 60.5},
                              {**self.configuration, "lifetime_seconds": True},
                              {**self.configuration, "game_id": ""}):
            with self.settings(LIVEOPS_IDENTITY_PROOF=configuration):
                response = self.request_proof()
                self.assertEqual(response.status_code, 503)
                self.assertEqual(response.json()["code"], "proof_configuration")
                self.assertNotIn("private-proof-key", str(response.json()))

    @patch("shop.throttles.IdentityProofThrottle.rate", "2/min", create=True)
    def test_proof_issuance_is_rate_limited(self):
        self.assertEqual(self.request_proof().status_code, 200)
        self.assertEqual(self.request_proof().status_code, 200)
        self.assertEqual(self.request_proof().status_code, 429)

    def test_issuer_can_produce_deterministic_time_for_cross_language_vectors(self):
        issuer = IdentityProofIssuer.from_settings()
        assertion = issuer.issue("player-42", "Verified Player", "session-1", self.challenge, now=1900000000)
        claims = jwt.decode(assertion, self.private_key.public_key(), algorithms=["RS256"],
                            options={"verify_exp": False, "verify_iat": False, "verify_nbf": False}, audience="sample-game")
        self.assertEqual(claims["iat"], 1900000000)
        self.assertEqual(claims["exp"], 1900000060)

    def test_exported_public_trust_matches_signer_without_private_material(self):
        output = io.StringIO()
        call_command("export_identity_trust", stdout=output)
        trust = json.loads(output.getvalue())
        self.assertEqual(trust["issuer"], self.configuration["issuer"])
        self.assertEqual(trust["max_lifetime_seconds"], 60)
        key = trust["public_keys"][0]
        self.assertEqual(set(key), {"key_id", "modulus", "exponent"})
        self.assertEqual(key["key_id"], "key-2026")
        self.assertEqual(int.from_bytes(base64.b64decode(key["modulus"]), "big"), self.private_key.public_key().public_numbers().n)
        self.assertNotIn("private_key_file", output.getvalue())
