"""Export public verification configuration. Never exports private key fields."""
import base64
import json

from django.core.management.base import BaseCommand
from shop.services.identity_proofs import IdentityProofIssuer


class Command(BaseCommand):
    help = "Write identity proof public trust JSON to stdout for Unity deployment."

    def handle(self, *args, **options):
        issuer = IdentityProofIssuer.from_settings()
        configuration = issuer.configuration
        numbers = issuer.private_key.public_key().public_numbers()

        def encode_integer(value):
            return base64.b64encode(value.to_bytes((value.bit_length() + 7) // 8, 'big')).decode('ascii')

        self.stdout.write(json.dumps({
            'issuer': configuration['issuer'], 'game_id': configuration['game_id'],
            'environment': configuration['environment'], 'content_version': configuration['content_version'],
            'max_lifetime_seconds': configuration['lifetime_seconds'], 'clock_skew_seconds': 5,
            'public_keys': [{ 'key_id': configuration['key_id'], 'modulus': encode_integer(numbers.n),
                              'exponent': encode_integer(numbers.e) }],
        }, indent=2))
