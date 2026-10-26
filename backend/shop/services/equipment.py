"""Sample canonical cosmetic equipment. No visual or transport dependencies."""
import re
from django.conf import settings
from django.db import transaction
from shop.models import Accessory, PlayerProfile, UserAccessory

MAX_REVISION = 9223372036854775806
_IDENTIFIER = re.compile(r'[A-Za-z0-9_.:-]{1,128}\Z')


def definitions():
    slots = settings.LIVEOPS_EQUIPMENT_SLOTS
    if not isinstance(slots, dict) or not 1 <= len(slots) <= 32:
        raise ValueError('Configure 1–32 equipment slots.')
    for slot, conflicts in slots.items():
        if not isinstance(slot, str) or not _IDENTIFIER.fullmatch(slot) or not isinstance(conflicts, list) or any(not isinstance(conflict, str) or not _IDENTIFIER.fullmatch(conflict) for conflict in conflicts) or len(set(conflicts)) != len(conflicts):
            raise ValueError('Invalid equipment slot.')
        for conflict in conflicts:
            if conflict == slot or conflict not in slots or slot not in slots[conflict]:
                raise ValueError('Equipment conflicts must be symmetric configured slots.')
    items = list(Accessory.objects.order_by('id').values('id', 'type'))
    if len(items) > 4096 or any(item['type'] not in slots for item in items):
        raise ValueError('Cosmetics require configured slots and bounded content.')
    return {'slots': [{'slot_id': slot, 'conflicts': sorted(conflicts)} for slot, conflicts in sorted(slots.items())],
            'items': [{'item_id': str(item['id']), 'slot_id': item['type']} for item in items]}


def _loadout(profile, configuration):
    slots = {entry['slot_id']: entry['conflicts'] for entry in configuration['slots']}
    result = {}
    for owned in UserAccessory.objects.filter(player=profile, is_equipped=True).select_related('accessory').order_by('accessory_id'):
        slot = owned.accessory.type
        if slot not in slots or slot in result or any(conflict in result for conflict in slots[slot]):
            raise ValueError('Stored equipment violates configured slots.')
        result[slot] = str(owned.accessory_id)
    if not 0 <= profile.equipment_revision <= MAX_REVISION:
        raise ValueError('Equipment revision is out of range.')
    return {'revision': profile.equipment_revision, 'items': result}


@transaction.atomic
def snapshot(profile):
    profile = PlayerProfile.objects.select_for_update().get(pk=profile.pk)
    configuration = definitions()
    return {**configuration, 'loadout': _loadout(profile, configuration)}


def validate_request(data):
    if not isinstance(data, dict) or set(data) != {'item_ids', 'expected_revision'}:
        raise ValueError('Only item_ids and expected_revision are accepted.')
    ids, revision = data['item_ids'], data['expected_revision']
    if type(revision) is not int or not 0 <= revision < MAX_REVISION or not isinstance(ids, list) or len(ids) > 32:
        raise ValueError('Invalid equipment request.')
    if any(not isinstance(item, str) or not re.fullmatch(r'[1-9][0-9]{0,18}', item) or int(item) > 9223372036854775807 for item in ids) or len(set(ids)) != len(ids):
        raise ValueError('Unique string cosmetic IDs are required.')
    return ids, revision


@transaction.atomic
def replace(profile, item_ids, expected_revision):
    profile = PlayerProfile.objects.select_for_update().get(pk=profile.pk)
    configuration = definitions()
    if profile.equipment_revision != expected_revision:
        return {'code': 'stale_equipment'}, 409
    owned = {str(entry.accessory_id): entry.accessory.type for entry in UserAccessory.objects.select_for_update().filter(player=profile).select_related('accessory')}
    slots = {entry['slot_id']: entry['conflicts'] for entry in configuration['slots']}
    chosen = set()
    for item_id in item_ids:
        if item_id not in owned:
            return {'code': 'invalid_loadout'}, 400
        slot = owned[item_id]
        if slot in chosen or any(conflict in chosen for conflict in slots[slot]):
            return {'code': 'invalid_loadout'}, 400
        chosen.add(slot)
    UserAccessory.objects.filter(player=profile).update(is_equipped=False)
    UserAccessory.objects.filter(player=profile, accessory_id__in=[int(item) for item in item_ids]).update(is_equipped=True)
    profile.equipment_revision += 1
    profile.save(update_fields=['equipment_revision'])
    return snapshot(profile), 200
