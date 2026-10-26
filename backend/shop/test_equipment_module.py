import base64
import tempfile
from pathlib import Path
from unittest.mock import patch
import jwt
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import rsa
from django.contrib.auth.models import User
from django.test import TestCase, override_settings
from rest_framework.test import APIClient
from shop.models import Accessory, PlayerProfile, UserAccessory


class EquipmentModuleTests(TestCase):
    def setUp(self):
        self.user = User.objects.create_user('equipment-module')
        self.profile = PlayerProfile.objects.create(user=self.user, platform_id='equipment-module')
        self.client = APIClient()
        self.client.force_authenticate(self.user)
        self.hat = Accessory.objects.create(name='Hat', type='headwear')
        self.hair = Accessory.objects.create(name='Hair', type='hair')
        self.sword = Accessory.objects.create(name='Sword', type='mainhand')
        for item in (self.hat, self.hair, self.sword):
            UserAccessory.objects.create(player=self.profile, accessory=item)

    def apply(self, items, revision=0):
        return self.client.post('/api/equipment/loadout/', {'item_ids': [str(item.id) for item in items], 'expected_revision': revision}, format='json')

    def test_initial_state_contains_configured_slots_all_item_mappings_and_revision(self):
        response = self.client.get('/api/equipment/loadout/')
        self.assertEqual(response.status_code, 200)
        state = response.json()
        self.assertEqual(state['loadout'], {'revision': 0, 'items': {}})
        self.assertIn({'slot_id': 'headwear', 'conflicts': ['hair']}, state['slots'])
        self.assertIn({'item_id': str(self.hat.id), 'slot_id': 'headwear'}, state['items'])
        self.assertEqual(response['Cache-Control'], 'no-store')

    def test_replace_and_clear_are_confirmed_single_revision_changes(self):
        response = self.apply([self.hat, self.sword])
        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.json()['loadout'], {'revision': 1, 'items': {'headwear': str(self.hat.id), 'mainhand': str(self.sword.id)}})
        self.assertEqual(self.apply([], 1).json()['loadout'], {'revision': 2, 'items': {}})
        self.assertFalse(UserAccessory.objects.filter(player=self.profile, is_equipped=True).exists())

    def test_stale_revision_cannot_overwrite_newer_equipment(self):
        self.assertEqual(self.apply([self.hat]).status_code, 200)
        rejected = self.apply([self.sword])
        self.assertEqual(rejected.status_code, 409)
        self.assertEqual(rejected.json()['code'], 'stale_equipment')
        self.assertTrue(UserAccessory.objects.get(player=self.profile, accessory=self.hat).is_equipped)

    def test_unowned_duplicate_and_conflicting_slots_reject_without_changes(self):
        unowned = Accessory.objects.create(name='Back', type='back')
        another_hat = Accessory.objects.create(name='Crown', type='headwear')
        UserAccessory.objects.create(player=self.profile, accessory=another_hat)
        for items in ([unowned], [self.hat, self.hat], [self.hat, self.hair], [self.hat, another_hat]):
            self.assertEqual(self.apply(items).status_code, 400)
        self.assertFalse(UserAccessory.objects.filter(player=self.profile, is_equipped=True).exists())

    def test_request_cannot_supply_identity_revision_or_slots_implicitly(self):
        for body in ({'item_ids': []}, {'item_ids': [], 'expected_revision': True}, {'item_ids': [self.hat.id], 'expected_revision': 0}, {'item_ids': [], 'expected_revision': 0, 'player_id': 'other'}):
            self.assertEqual(self.client.post('/api/equipment/loadout/', body, format='json').status_code, 400)

    @override_settings(LIVEOPS_EQUIPMENT_SLOTS={'headwear': [], 'hair': [], 'mainhand': []})
    def test_conflicts_are_backend_configuration_not_humanoid_constants(self):
        self.assertEqual(self.apply([self.hat, self.hair]).status_code, 200)

    @override_settings(LIVEOPS_EQUIPMENT_SLOTS={'headwear': ['missing']})
    def test_invalid_configuration_is_explicit_and_does_not_mutate(self):
        response = self.apply([self.hat])
        self.assertEqual(response.status_code, 409)
        self.assertEqual(response.json()['code'], 'equipment_configuration')
        self.assertFalse(UserAccessory.objects.filter(player=self.profile, is_equipped=True).exists())

    def test_failure_after_write_rolls_back_flags_and_revision(self):
        from shop.services import equipment
        with patch.object(equipment, 'snapshot', side_effect=ValueError('invalid configuration')):
            self.assertEqual(self.apply([self.hat]).status_code, 409)
        self.assertFalse(UserAccessory.objects.filter(player=self.profile, is_equipped=True).exists())
        self.assertEqual(self.client.get('/api/equipment/loadout/').json()['loadout']['revision'], 0)

    def test_proof_is_signed_for_authenticated_player_challenge_and_canonical_loadout(self):
        key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'key.pem'
            path.write_bytes(key.private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8, serialization.NoEncryption()))
            policy = {'issuer': 'test', 'game_id': 'game', 'environment': 'test', 'content_version': 'v1', 'key_id': 'key', 'lifetime_seconds': 60, 'private_key_file': str(path)}
            self.assertEqual(self.apply([self.hat]).status_code, 200)
            challenge = base64.urlsafe_b64encode(bytes(range(32))).rstrip(b'=').decode()
            with override_settings(LIVEOPS_IDENTITY_PROOF=policy):
                response = self.client.post('/api/equipment/proof/', {'session_id': 'session', 'challenge': challenge}, format='json')
            self.assertEqual(response.status_code, 200)
            assertion = response.json()['assertion']
            claims = jwt.decode(assertion, key.public_key(), algorithms=['RS256'], audience='game', issuer='test')
            self.assertEqual(jwt.get_unverified_header(assertion)['typ'], 'liveops-equipment+jwt')
            self.assertEqual(claims['sub'], str(self.profile.pk))
            self.assertEqual(claims['nonce'], challenge)
            self.assertEqual(claims['sid'], 'session')
            self.assertEqual(claims['revision'], 1)
            self.assertEqual(claims['items'], {'headwear': str(self.hat.id)})
            self.assertNotIn('inventory', claims)

    @override_settings(LIVEOPS_EQUIPMENT_SLOTS={'headwear': [[]]})
    def test_malformed_conflict_configuration_is_reported_not_an_internal_error(self):
        self.assertEqual(self.client.get('/api/equipment/loadout/').status_code, 409)

    def test_anonymous_and_missing_identity_are_rejected(self):
        self.client.force_authenticate(None)
        self.assertEqual(self.client.get('/api/equipment/loadout/').status_code, 401)
        missing = User.objects.create_user('missing-profile')
        self.client.force_authenticate(missing)
        self.assertEqual(self.client.get('/api/equipment/loadout/').status_code, 409)
