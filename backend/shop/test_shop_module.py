from concurrent.futures import ThreadPoolExecutor
from threading import Event, current_thread
from time import monotonic, sleep
from django.db import OperationalError, close_old_connections
from django.test import TransactionTestCase
from shop.services import shop as shop_service, wallet as wallet_service
from unittest.mock import patch
from django.contrib.auth.models import User
from django.test import TestCase
from rest_framework.test import APIClient
from shop.models import Accessory, AccessoryCost, AccessorySet, AccessorySetCost, Currency, PlayerProfile, UserAccessory, Wallet


class ShopModuleTests(TestCase):
    def setUp(self):
        self.user = User.objects.create_user(username='shop-module')
        self.profile = PlayerProfile.objects.create(user=self.user, platform_id='shop-module')
        self.client = APIClient()
        self.client.force_authenticate(self.user)
        self.gold = Currency.objects.create(name='gold')
        self.gears = Currency.objects.create(name='gears')
        self.gold_wallet = Wallet.objects.create(player=self.profile, currency=self.gold, amount=100)
        Wallet.objects.create(player=self.profile, currency=self.gears, amount=5)
        self.hat = Accessory.objects.create(name='Hat', type='headwear')
        self.sword = Accessory.objects.create(name='Sword', type='mainhand')
        AccessoryCost.objects.create(accessory=self.hat, currency=self.gold, amount=25)
        AccessoryCost.objects.create(accessory=self.hat, currency=self.gears, amount=2)
        self.outfit = AccessorySet.objects.create(name='Outfit')
        self.outfit.accessories.add(self.hat, self.sword)
        AccessorySetCost.objects.create(accessory_set=self.outfit, currency=self.gold, amount=75)

    def purchase(self, offer=None, **extra):
        return self.client.post('/api/shop/purchase-offer/', {'offer_id': offer or f'item:{self.hat.id}', **extra}, format='json')

    def test_catalog_returns_offers_and_separate_confirmed_account_snapshots(self):
        response = self.client.get('/api/shop/catalog/')
        self.assertEqual(response.status_code, 200)
        self.assertEqual(set(response.json()), {'offers', 'wallet', 'inventory'})
        offer = next(item for item in response.json()['offers'] if item['offer_id'] == f'item:{self.hat.id}')
        self.assertEqual(offer, {'offer_id': f'item:{self.hat.id}', 'display_name': 'Hat', 'category_id': 'headwear', 'is_set': False, 'item_ids': [str(self.hat.id)], 'costs': [{'currency_id': 'gold', 'amount': 25}, {'currency_id': 'gears', 'amount': 2}]})
        self.assertEqual(response['Cache-Control'], 'no-store')

    def test_body_parts_are_set_only_and_catalog_preserves_categories(self):
        body = Accessory.objects.create(name='Body', type='torso')
        self.outfit.accessories.add(body)
        response = self.client.get('/api/shop/catalog/')
        offers = {offer['offer_id']: offer for offer in response.json()['offers']}
        self.assertNotIn(f'item:{body.id}', offers)
        self.assertEqual(offers[f'item:{self.hat.id}']['category_id'], 'headwear')
        self.assertIn(str(body.id), offers[f'set:{self.outfit.id}']['item_ids'])

    def test_purchase_debits_all_currencies_and_returns_confirmed_ownership(self):
        response = self.purchase()
        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.json()['purchased_offer_id'], f'item:{self.hat.id}')
        self.assertEqual({entry['currency_id']: entry['amount'] for entry in response.json()['wallet']['balances']}, {'gold': 75, 'gears': 3})
        self.assertEqual(response.json()['inventory']['items'][0]['item_id'], str(self.hat.id))

    def test_failed_currency_check_never_debits_other_currency_or_grants(self):
        Wallet.objects.filter(player=self.profile, currency=self.gears).update(amount=1)
        self.assertEqual(self.purchase().status_code, 400)
        self.gold_wallet.refresh_from_db()
        self.assertEqual(self.gold_wallet.amount, 100)
        self.assertFalse(UserAccessory.objects.filter(player=self.profile).exists())

    def test_duplicate_purchase_rejected_without_second_charge(self):
        self.assertEqual(self.purchase().status_code, 200)
        response = self.purchase()
        self.assertEqual(response.json()['code'], 'already_owned')
        self.gold_wallet.refresh_from_db()
        self.assertEqual(self.gold_wallet.amount, 75)

    def test_partial_set_cost_is_full_offer_price_and_owned_members_not_duplicated(self):
        UserAccessory.objects.create(player=self.profile, accessory=self.hat)
        response = self.purchase(f'set:{self.outfit.id}')
        self.assertEqual(response.status_code, 200)
        self.gold_wallet.refresh_from_db()
        self.assertEqual(self.gold_wallet.amount, 25)
        self.assertEqual(UserAccessory.objects.filter(player=self.profile).count(), 2)
        self.assertEqual(len(response.json()['inventory']['sets']), 1)

    def test_grant_failure_rolls_back_every_debit(self):
        with patch('shop.models.UserAccessory.save', side_effect=RuntimeError('injected grant failure')):
            with self.assertRaises(RuntimeError):
                self.purchase()
        self.gold_wallet.refresh_from_db()
        self.assertEqual(self.gold_wallet.amount, 100)
        self.assertFalse(UserAccessory.objects.filter(player=self.profile).exists())

    def test_invalid_price_configuration_cannot_credit_balance(self):
        AccessoryCost.objects.filter(accessory=self.hat, currency=self.gold).update(amount=-10)
        response = self.purchase()
        self.assertEqual(response.status_code, 409)
        self.assertEqual(response.json()['code'], 'invalid_shop_configuration')
        self.gold_wallet.refresh_from_db()
        self.assertEqual(self.gold_wallet.amount, 100)

    def test_invalid_set_member_display_name_rolls_back_purchase(self):
        body = Accessory.objects.create(name='', type='torso')
        self.outfit.accessories.add(body)
        response = self.purchase(f'set:{self.outfit.id}')
        self.assertEqual(response.status_code, 409)
        self.gold_wallet.refresh_from_db()
        self.assertEqual(self.gold_wallet.amount, 100)
        self.assertFalse(UserAccessory.objects.filter(player=self.profile).exists())

    def test_client_cannot_supply_price_or_identity_or_arbitrary_offer(self):
        self.assertEqual(self.purchase(price=0).status_code, 400)
        self.assertEqual(self.purchase(player_id=999).status_code, 400)
        self.assertEqual(self.purchase('item:99999').status_code, 400)
        self.assertFalse(UserAccessory.objects.filter(player=self.profile).exists())

    def test_anonymous_and_missing_profile_rejected_without_provisioning(self):
        self.client.force_authenticate(None)
        self.assertEqual(self.client.get('/api/shop/catalog/').status_code, 401)
        self.client.force_authenticate(self.user)
        self.profile.delete()
        self.assertEqual(self.client.get('/api/shop/catalog/').status_code, 409)
        self.assertFalse(PlayerProfile.objects.filter(user=self.user).exists())

    def test_invalid_catalog_metadata_rejects_purchase_without_charge_or_grant(self):
        self.outfit.category = 'Seasonal Bundles'
        self.outfit.save(update_fields=['category'])
        response = self.purchase(f'set:{self.outfit.id}')
        self.assertEqual(response.status_code, 409)
        self.gold_wallet.refresh_from_db()
        self.assertEqual(self.gold_wallet.amount, 100)
        self.assertFalse(UserAccessory.objects.filter(player=self.profile).exists())




class ShopSnapshotConcurrencyTests(TransactionTestCase):
    def test_catalog_read_does_not_mix_pre_purchase_balance_with_post_purchase_ownership(self):
        user = User.objects.create_user(username='snapshot-reader')
        profile = PlayerProfile.objects.create(user=user, platform_id='snapshot-reader')
        currency = Currency.objects.create(name='gold')
        Wallet.objects.create(player=profile, currency=currency, amount=100)
        item = Accessory.objects.create(name='Hat', type='headwear')
        AccessoryCost.objects.create(accessory=item, currency=currency, amount=25)
        wallet_read = Event()
        release_read = Event()
        purchase_started = Event()
        purchase_committed = Event()
        original_snapshot = wallet_service.snapshot
        reader_thread = []

        def pause_after_wallet_read(account):
            state = original_snapshot(account)
            if current_thread() is reader_thread[0]:
                wallet_read.set()
                if not release_read.wait(3):
                    raise RuntimeError('Catalog read was not released')
            return state

        def read_catalog():
            close_old_connections()
            reader_thread.append(current_thread())
            try:
                client = APIClient()
                client.force_authenticate(user)
                response = client.get('/api/shop/catalog/')
                self.assertEqual(response.status_code, 200)
                return response.json()
            finally:
                close_old_connections()

        def buy():
            close_old_connections()
            purchase_started.set()
            try:
                # SQLite can abort a concurrent writer instead of waiting on row locks.
                # Retry only this test fixture's aborted transaction after the read ends.
                deadline = monotonic() + 3
                while True:
                    try:
                        result, status = shop_service.purchase(profile, f'item:{item.id}')
                        self.assertEqual(status, 200)
                        purchase_committed.set()
                        return result
                    except OperationalError:
                        if monotonic() > deadline:
                            raise
                        sleep(0.02)
            finally:
                close_old_connections()

        with ThreadPoolExecutor(max_workers=2) as workers, patch.object(wallet_service, 'snapshot', side_effect=pause_after_wallet_read):
            read = workers.submit(read_catalog)
            try:
                self.assertTrue(wallet_read.wait(2))
                purchase = workers.submit(buy)
                self.assertTrue(purchase_started.wait(2))
                self.assertFalse(purchase_committed.wait(0.15), 'Purchase committed in the middle of a catalog account snapshot')
            finally:
                release_read.set()
            snapshot = read.result(timeout=4)
            confirmed = purchase.result(timeout=4)
        self.assertEqual(snapshot['wallet']['balances'][0]['amount'], 100)
        self.assertEqual(snapshot['inventory']['items'], [])
        self.assertEqual(confirmed['wallet']['balances'][0]['amount'], 75)
        self.assertEqual(confirmed['inventory']['items'][0]['item_id'], str(item.id))
