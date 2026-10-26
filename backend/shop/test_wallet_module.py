from unittest.mock import patch
from django.test import TestCase
from django.contrib.auth.models import User
from django.core.cache import cache
from rest_framework.test import APIClient
from shop.models import PlayerProfile, Currency, Wallet


class WalletModuleTests(TestCase):
    def setUp(self):
        cache.clear()
        self.user = User.objects.create_user(username='wallet-owner')
        self.profile = PlayerProfile.objects.create(user=self.user, platform_id='wallet-owner')
        self.gold = Currency.objects.create(name='gold')
        self.snow = Currency.objects.create(name='snowflakes')
        self.gears = Currency.objects.create(name='gears')
        self.gold_wallet = Wallet.objects.create(player=self.profile, currency=self.gold, amount=100)
        self.snow_wallet = Wallet.objects.create(player=self.profile, currency=self.snow, amount=20)
        self.gear_wallet = Wallet.objects.create(player=self.profile, currency=self.gears, amount=10)
        self.client = APIClient()
        self.client.force_authenticate(self.user)

    def convert(self, **changes):
        return self.client.post('/api/wallet/convert/', {'offer_id': 'gears-to-gold-v1', 'amount': 2, **changes}, format='json')

    def test_snapshot_contains_private_balances_and_integer_offers(self):
        response = self.client.get('/api/wallet/')
        self.assertEqual(response.status_code, 200)
        self.assertEqual({entry['currency_id']: entry['amount'] for entry in response.data['balances']}, {'gold': 100, 'snowflakes': 20, 'gears': 10})
        offers = {offer['offer_id']: offer for offer in response.data['offers']}
        self.assertEqual(offers['snowflakes-to-gold-v1']['source_units'], 2)
        self.assertEqual(offers['snowflakes-to-gold-v1']['target_units'], 1)

    def test_conversion_returns_confirmed_balances(self):
        response = self.convert()
        self.assertEqual(response.status_code, 200)
        self.gold_wallet.refresh_from_db()
        self.gear_wallet.refresh_from_db()
        self.assertEqual(self.gold_wallet.amount, 150)
        self.assertEqual(self.gear_wallet.amount, 8)
        self.assertEqual(next(item['amount'] for item in response.data['balances'] if item['currency_id'] == 'gold'), 150)

    def test_integer_bounds_and_exact_units_are_required(self):
        for amount in (True, 2.5, '2', 0, -1, 2147483648, None):
            with self.subTest(amount=amount):
                self.assertEqual(self.convert(amount=amount).status_code, 400)
        response = self.convert(offer_id='snowflakes-to-gold-v1', amount=3)
        self.assertEqual(response.status_code, 400)
        self.assertEqual(response.data['code'], 'invalid_units')
        self.snow_wallet.refresh_from_db()
        self.assertEqual(self.snow_wallet.amount, 20)

    def test_rejection_and_overflow_never_debit_source(self):
        self.assertEqual(self.convert(amount=100).data['code'], 'insufficient_funds')
        self.gold_wallet.amount = 2147483640
        self.gold_wallet.save()
        self.assertEqual(self.convert().data['code'], 'balance_limit')
        self.gold_wallet.delete()
        self.assertEqual(self.convert().status_code, 409)
        self.gear_wallet.refresh_from_db()
        self.assertEqual(self.gear_wallet.amount, 10)

    def test_destination_write_failure_rolls_back_debit(self):
        original_save = Wallet.save
        def save_or_fail(wallet, *args, **kwargs):
            if wallet.currency_id == self.gold.pk:
                raise RuntimeError('simulated destination failure')
            return original_save(wallet, *args, **kwargs)
        with patch.object(Wallet, 'save', save_or_fail):
            with self.assertRaises(RuntimeError):
                self.convert()
        self.gear_wallet.refresh_from_db()
        self.gold_wallet.refresh_from_db()
        self.assertEqual(self.gear_wallet.amount, 10)
        self.assertEqual(self.gold_wallet.amount, 100)

    def test_authenticated_account_is_authority_and_identity_claims_are_rejected(self):
        other = User.objects.create_user(username='other-wallet-owner')
        other_profile = PlayerProfile.objects.create(user=other, platform_id='other-wallet-owner')
        other_gold = Wallet.objects.create(player=other_profile, currency=self.gold, amount=0)
        Wallet.objects.create(player=other_profile, currency=self.gears, amount=4)
        self.assertEqual(self.convert(player_id=other_profile.pk).status_code, 400)
        self.assertEqual(self.convert().status_code, 200)
        other_gold.refresh_from_db()
        self.assertEqual(other_gold.amount, 0)
        self.client.force_authenticate(other)
        self.assertEqual(self.convert().status_code, 200)
        self.assertEqual(Wallet.objects.get(player=other_profile, currency=self.gold).amount, 50)

    def test_anonymous_and_missing_profile_are_rejected(self):
        self.client.force_authenticate(None)
        self.assertEqual(self.convert().status_code, 401)
        self.client.force_authenticate(self.user)
        self.profile.delete()
        self.assertEqual(self.client.get('/api/wallet/').status_code, 409)
        self.assertEqual(self.convert().status_code, 409)
