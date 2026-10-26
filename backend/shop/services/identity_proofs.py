"""Identity assertion protocol v1. Signing keys never leave the Django process."""
import base64
import re
import time
import uuid

import jwt
from cryptography.exceptions import UnsupportedAlgorithm
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import rsa
from django.conf import settings
from django.core.exceptions import ImproperlyConfigured

_IDENTIFIER = re.compile(r"[A-Za-z0-9_.:-]{1,128}\Z")
_CHALLENGE = re.compile(r"[A-Za-z0-9_-]{43}\Z")


def validate_challenge_context(session_id, challenge):
    if not isinstance(session_id, str) or not _IDENTIFIER.fullmatch(session_id):
        raise ValueError("A valid session identifier is required.")
    if not isinstance(challenge, str) or not _CHALLENGE.fullmatch(challenge):
        raise ValueError("A canonical 32-byte challenge is required.")
    decoded = base64.urlsafe_b64decode(challenge + "=")
    if len(decoded) != 32 or base64.urlsafe_b64encode(decoded).rstrip(b"=").decode() != challenge:
        raise ValueError("A canonical 32-byte challenge is required.")


class IdentityProofIssuer:
    def __init__(self, private_key, configuration):
        self.private_key = private_key
        self.configuration = dict(configuration)

    @classmethod
    def from_settings(cls):
        try:
            configuration = dict(settings.LIVEOPS_IDENTITY_PROOF)
            for field in ("game_id", "environment", "content_version", "key_id"):
                value = configuration[field]
                if not isinstance(value, str) or not _IDENTIFIER.fullmatch(value):
                    raise ValueError()
            if len(configuration["key_id"]) > 64:
                raise ValueError()
            issuer = configuration["issuer"]
            if not isinstance(issuer, str) or not 1 <= len(issuer) <= 256 or any(ord(character) < 33 or ord(character) > 126 for character in issuer):
                raise ValueError()
            lifetime = configuration["lifetime_seconds"]
            if type(lifetime) is not int and (not isinstance(lifetime, str) or not re.fullmatch(r"[0-9]{1,3}", lifetime)):
                raise ValueError()
            lifetime = int(lifetime)
            if not 1 <= lifetime <= 300:
                raise ValueError()
            configuration["lifetime_seconds"] = lifetime
            with open(configuration["private_key_file"], "rb") as key_file:
                private_bytes = key_file.read(32769)
            if len(private_bytes) > 32768:
                raise ValueError()
            private_key = serialization.load_pem_private_key(private_bytes, password=None)
            if not isinstance(private_key, rsa.RSAPrivateKey) or not 2048 <= private_key.key_size <= 4096:
                raise ValueError()
            return cls(private_key, configuration)
        except (AttributeError, KeyError, TypeError, ValueError, OSError, UnsupportedAlgorithm):
            raise ImproperlyConfigured("Configure the identity proof issuer, audience, environment, content version and RSA signing key.") from None

    def _claims(self, player_id, display_name, session_id, challenge, now=None):
        validate_challenge_context(session_id, challenge)
        if not isinstance(player_id, str) or not _IDENTIFIER.fullmatch(player_id):
            raise ValueError("A stable player identifier is required.")
        if not isinstance(display_name, str) or not display_name.strip() or len(display_name.encode("utf-8")) > 600 or any(ord(character) < 32 or ord(character) == 127 for character in display_name):
            raise ValueError("A valid public display name is required.")
        issued_at = int(time.time()) if now is None else now
        if type(issued_at) is not int or not 0 < issued_at <= 253402300000:
            raise ValueError("A valid issue time is required.")
        configuration = self.configuration
        claims = {
            "iss": configuration["issuer"], "aud": configuration["game_id"],
            "env": configuration["environment"], "content": configuration["content_version"],
            "sub": player_id, "name": display_name, "sid": session_id, "nonce": challenge,
            "iat": issued_at, "nbf": issued_at,
            "exp": issued_at + configuration["lifetime_seconds"], "jti": uuid.uuid4().hex, "ver": 1,
        }
        return claims

    def issue(self, player_id, display_name, session_id, challenge, now=None):
        claims = self._claims(player_id, display_name, session_id, challenge, now)
        configuration = self.configuration
        return jwt.encode(claims, self.private_key, algorithm="RS256", headers={
            "kid": configuration["key_id"], "typ": "liveops-identity+jwt",
        })

    def issue_equipment(self, player_id, display_name, session_id, challenge, loadout):
        claims = self._claims(player_id, display_name, session_id, challenge)
        claims.update(revision=loadout['revision'], items=loadout['items'])
        return jwt.encode(claims, self.private_key, algorithm="RS256", headers={
            "kid": self.configuration["key_id"], "typ": "liveops-equipment+jwt",
        })
