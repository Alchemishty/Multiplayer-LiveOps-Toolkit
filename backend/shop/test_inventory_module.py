from django.contrib.auth.models import User
from django.test import TestCase
from rest_framework.test import APIClient
from shop.models import Accessory, AccessorySet, PlayerProfile, UserAccessory


class InventoryModuleTests(TestCase):
    def setUp(self):
        self.user = User.objects.create_user(username='inventory-owner')
        self.profile = PlayerProfile.objects.create(user=self.user, platform_id='inventory-owner')
        self.client = APIClient()
        self.client.force_authenticate(self.user)
        self.hat = Accessory.objects.create(name='Hat', type='headwear')
        self.sword = Accessory.objects.create(name='Sword', type='mainhand')
        UserAccessory.objects.create(player=self.profile, accessory=self.hat, is_equipped=True)

    def test_snapshot_contains_owned_cosmetics_without_shop_or_equipment_fields(self):
        response = self.client.get('/api/inventory/')
        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.json(), {'items': [{'item_id': str(self.hat.id), 'display_name': 'Hat', 'category_id': 'headwear'}], 'sets': []})
        self.assertEqual(response['Cache-Control'], 'no-store')

    def test_sets_require_all_members_owned_and_exclude_empty_sets(self):
        complete = AccessorySet.objects.create(name='Hat outfit')
        complete.accessories.add(self.hat)
        partial = AccessorySet.objects.create(name='Partial')
        partial.accessories.add(self.hat, self.sword)
        AccessorySet.objects.create(name='Empty')
        response = self.client.get('/api/inventory/')
        self.assertEqual(response.json()['sets'], [{'set_id': str(complete.id), 'display_name': 'Hat outfit', 'item_ids': [str(self.hat.id)]}])

    def test_account_isolation_ignores_caller_identity(self):
        other = User.objects.create_user(username='inventory-other')
        profile = PlayerProfile.objects.create(user=other, platform_id='inventory-other')
        UserAccessory.objects.create(player=profile, accessory=self.sword)
        response = self.client.get('/api/inventory/', {'player_id': profile.id})
        self.assertEqual([item['item_id'] for item in response.json()['items']], [str(self.hat.id)])

    def test_missing_profile_returns_configuration_error_without_provisioning(self):
        self.profile.delete()
        response = self.client.get('/api/inventory/')
        self.assertEqual(response.status_code, 409)
        self.assertEqual(response.json(), {'code': 'identity_not_initialized'})
        self.assertFalse(PlayerProfile.objects.filter(user=self.user).exists())

    def test_anonymous_cannot_read_inventory(self):
        self.client.force_authenticate(None)
        self.assertEqual(self.client.get('/api/inventory/').status_code, 401)
