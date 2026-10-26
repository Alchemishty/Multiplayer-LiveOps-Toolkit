from datetime import date, timedelta
from unittest.mock import patch
from django.contrib.auth.models import User
from django.test import TestCase
from django.core.cache import cache
from rest_framework.test import APIClient
from shop.models import Achievement, UserAchievement, Accessory, AccessorySet, AccessoryCost, AccessorySetCost, PlayerProfile, UserAccessory, Currency, Wallet, DailyAttendance


class AchievementsModuleTests(TestCase):
    def setUp(self):
        cache.clear()
        self.user = User.objects.create_user('achievements-module')
        self.profile = PlayerProfile.objects.create(user=self.user, platform_id='achievements-module')
        self.client = APIClient(); self.client.force_authenticate(self.user)
        self.gold = Currency.objects.create(name='gold')
        self.balance = Wallet.objects.create(player=self.profile, currency=self.gold, amount=100)
        self.hat = Accessory.objects.create(name='Hat', type='headwear')
        self.sword = Accessory.objects.create(name='Sword', type='mainhand')
        AccessoryCost.objects.create(accessory=self.hat, currency=self.gold, amount=25)
        self.outfit = AccessorySet.objects.create(name='Outfit'); self.outfit.accessories.add(self.hat, self.sword)
        AccessorySetCost.objects.create(accessory_set=self.outfit, currency=self.gold, amount=50)

    def definition(self, key='first', **fields):
        values = dict(title='First', description='First purchase', condition_type='one_time_trigger', trigger_event='purchase')
        values.update(fields)
        return Achievement.objects.create(key=key, **values)

    def fetch(self):
        return self.client.get('/api/achievements/state/')

    def claim(self, key='first', **extra):
        return self.client.post('/api/achievements/claim/', {'achievement_id': key, **extra}, format='json')

    def purchase(self, offer=None):
        return self.client.post('/api/shop/purchase-offer/', {'offer_id': offer or f'item:{self.hat.id}'}, format='json')

    def entries(self):
        return {entry['achievement_id']: entry for entry in self.fetch().json()['achievements']['entries']}

    def test_identity_only_snapshot_exposes_backend_definitions_without_local_catalog_or_economy(self):
        self.definition()
        response = self.fetch(); self.assertEqual(response.status_code, 200)
        self.assertEqual(response['Cache-Control'], 'no-store')
        self.assertIsNone(response.json()['wallet']); self.assertIsNone(response.json()['inventory'])
        entry = response.json()['achievements']['entries'][0]
        self.assertEqual((entry['current_count'], entry['target_count'], entry['is_unlocked'], entry['is_claimed']), (0, 1, False, False))
        self.assertEqual(entry['reward'], {'kind': 'none', 'currency_id': None, 'item_id': None, 'amount': 0})

    def test_trusted_purchase_completes_and_client_cannot_supply_a_count_or_player(self):
        self.definition(reward_type='currency', currency_reward=self.gold, reward_amount=10)
        self.assertEqual(self.claim(current_count=999).status_code, 400)
        self.assertEqual(self.claim(player_id='other').status_code, 400)
        self.assertEqual(self.claim().json()['code'], 'condition_not_met')
        self.assertEqual(self.purchase().status_code, 200)
        self.assertTrue(self.entries()['first']['is_unlocked'])
        self.balance.refresh_from_db(); self.assertEqual(self.balance.amount, 75)

    def test_claim_grants_currency_once_and_returns_confirmed_account_snapshots(self):
        self.definition(reward_type='currency', currency_reward=self.gold, reward_amount=10)
        self.purchase()
        response = self.claim(); self.assertEqual(response.status_code, 200)
        self.assertEqual(response.json()['claimed_achievement_id'], 'first')
        self.assertEqual(response.json()['wallet']['balances'][0]['amount'], 85)
        self.assertTrue(response.json()['achievements']['entries'][0]['is_claimed'])
        self.assertEqual(self.claim().json()['code'], 'already_claimed')
        self.balance.refresh_from_db(); self.assertEqual(self.balance.amount, 85)

    def test_cosmetic_reward_grants_once_and_collection_can_complete_from_it(self):
        self.definition(reward_type='accessory', accessory_reward=self.sword)
        self.definition('collection', condition_type='collection_completion', target_slot='mainhand')
        self.purchase()
        response = self.claim(); self.assertEqual(response.status_code, 200)
        ids = {entry['item_id'] for entry in response.json()['inventory']['items']}
        self.assertEqual(ids, {str(self.hat.id), str(self.sword.id)})
        self.assertTrue(self.entries()['collection']['is_unlocked'])
        self.assertEqual(UserAccessory.objects.filter(player=self.profile, accessory=self.sword).count(), 1)

    def test_no_reward_completion_does_not_require_claim(self):
        self.definition(); self.purchase()
        self.assertTrue(self.entries()['first']['is_unlocked'])
        self.assertEqual(self.claim().json()['code'], 'no_reward')

    def test_set_purchase_counts_once_and_spend_uses_actual_basket_not_current_item_prices(self):
        self.definition('sets', condition_type='threshold_counter', trigger_event='set_purchase', target_count=2)
        self.definition('spend', condition_type='threshold_counter', trigger_event='currency_spent', target_count=100, target_currency='gold')
        self.definition('items', condition_type='threshold_counter', target_count=2)
        self.assertEqual(self.purchase(f'set:{self.outfit.id}').status_code, 200)
        AccessoryCost.objects.filter(accessory=self.hat).update(amount=999)
        entries = self.entries(); self.assertEqual(entries['sets']['current_count'], 1)
        self.assertEqual(entries['spend']['current_count'], 50)
        self.assertEqual(entries['items']['current_count'], 0)
        self.assertEqual(self.purchase(f'set:{self.outfit.id}').status_code, 400)
        self.assertEqual(self.entries()['spend']['current_count'], 50)

    def test_purchase_progress_rolls_back_if_purchase_cannot_commit(self):
        self.definition()
        with patch('shop.services.shop.snapshot', side_effect=ValueError('broken export')):
            self.assertEqual(self.purchase().status_code, 409)
        self.assertFalse(UserAchievement.objects.filter(player=self.profile, current_count__gt=0).exists())
        self.assertFalse(UserAccessory.objects.filter(player=self.profile).exists())

    def test_daily_login_means_recorded_attendance_not_a_client_login_event(self):
        self.definition('attendance', condition_type='threshold_counter', trigger_event='login', target_count=2)
        DailyAttendance.objects.create(player=self.profile, date=date.today()-timedelta(days=1))
        self.assertEqual(self.entries()['attendance']['current_count'], 1)
        DailyAttendance.objects.create(player=self.profile, date=date.today())
        self.assertTrue(self.entries()['attendance']['is_unlocked'])

    def test_collection_progress_is_server_owned_and_nonempty(self):
        self.definition('collection', condition_type='collection_completion', target_slot='headwear')
        UserAccessory.objects.create(player=self.profile, accessory=self.hat)
        entry = self.entries()['collection']; self.assertEqual(entry['target_count'], 1)
        self.assertTrue(entry['is_unlocked'])
        self.assertTrue(self.fetch().json()['achievements']['requires_inventory'])

    def test_invalid_reward_configuration_or_balance_overflow_never_grants_or_claims(self):
        definition = self.definition(reward_type='currency', currency_reward=self.gold, reward_amount=-1)
        response = self.fetch(); self.assertEqual(response.status_code, 409)
        definition.reward_amount = 10; definition.save(); self.purchase()
        self.balance.amount = 2147483647; self.balance.save()
        self.assertEqual(self.claim().status_code, 409)
        self.assertFalse(UserAchievement.objects.get(player=self.profile, achievement=definition).is_claimed)

    def test_failure_after_reward_write_rolls_back_claim_and_balance(self):
        self.definition(reward_type='currency', currency_reward=self.gold, reward_amount=10)
        self.purchase()
        from shop.services import achievements
        with patch.object(achievements, 'snapshot', side_effect=ValueError('invalid export')):
            self.assertEqual(self.claim().status_code, 409)
        self.balance.refresh_from_db(); self.assertEqual(self.balance.amount, 75)
        self.assertFalse(UserAchievement.objects.get(player=self.profile).is_claimed)

    def test_private_progress_is_scoped_and_inactive_definitions_are_hidden(self):
        self.definition('inactive', is_active=False)
        definition = self.definition()
        other = PlayerProfile.objects.create(user=User.objects.create_user('other'), platform_id='other')
        UserAchievement.objects.create(player=other, achievement=definition, current_count=1, is_unlocked=True)
        self.assertEqual(set(self.entries()), {'first'})
        self.assertFalse(self.entries()['first']['is_unlocked'])

    def test_anonymous_and_missing_identity_reject_without_creating_state(self):
        self.client.force_authenticate(None); self.assertEqual(self.fetch().status_code, 401)
        missing = User.objects.create_user('missing-profile'); self.client.force_authenticate(missing)
        self.assertEqual(self.fetch().status_code, 409)

    def test_unexportable_reward_snapshot_rolls_back_grant_and_claim(self):
        definition = self.definition(reward_type='currency', currency_reward=self.gold, reward_amount=10)
        self.purchase()
        from shop.services import achievements
        oversized = {'balances': [{'currency_id': f'currency-{index}', 'amount': 0} for index in range(257)], 'offers': []}
        with patch.object(achievements.wallet, 'snapshot', return_value=oversized):
            self.assertEqual(self.claim().status_code, 409)
        self.balance.refresh_from_db(); self.assertEqual(self.balance.amount, 75)
        self.assertFalse(UserAchievement.objects.get(player=self.profile, achievement=definition).is_claimed)

    def test_spending_condition_requires_a_configured_currency(self):
        self.definition(trigger_event='currency_spent', target_currency='missing-currency')
        self.assertEqual(self.fetch().status_code, 409)
