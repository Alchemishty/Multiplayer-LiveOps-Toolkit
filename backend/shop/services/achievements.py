"""Sample authoritative achievement mechanics, using progress rows rather than history."""
import json
import re
from django.db import transaction
from django.utils import timezone
from shop.models import Achievement, UserAchievement, PlayerProfile, Accessory, UserAccessory, DailyAttendance, Wallet, Currency
from shop.services import wallet, inventory


def _identifier(value):
    return isinstance(value, str) and re.fullmatch(r'[A-Za-z0-9_.:-]{1,128}', value) is not None


def _name(value):
    return isinstance(value, str) and bool(value.strip()) and len(value) <= 256


def _definitions():
    definitions = list(Achievement.objects.filter(is_active=True).select_related('currency_reward', 'accessory_reward').order_by('key'))
    if len(definitions) > 1024:
        raise ValueError('Achievement content exceeds 1024 definitions.')
    for definition in definitions:
        if not _identifier(definition.key) or not _name(definition.title) or not isinstance(definition.description, str) or len(definition.description) > 4096:
            raise ValueError('Invalid achievement display metadata.')
        if definition.condition_type not in {'one_time_trigger', 'threshold_counter', 'collection_completion'} or definition.trigger_event not in {'purchase', 'set_purchase', 'login', 'currency_spent'}:
            raise ValueError('Unsupported sample achievement condition.')
        if definition.condition_type == 'threshold_counter' and not 1 <= definition.target_count <= wallet.MAX_BALANCE:
            raise ValueError('Achievement target must be positive and bounded.')
        if definition.condition_type == 'collection_completion' and (not _identifier(definition.target_slot) or not Accessory.objects.filter(type=definition.target_slot).exists()):
            raise ValueError('Collection achievement requires nonempty configured cosmetics.')
        if definition.trigger_event == 'currency_spent' and (not _identifier(definition.target_currency) or not Currency.objects.filter(name=definition.target_currency).exists()):
            raise ValueError('Currency spending requires a currency identifier.')
        _reward(definition)
    return definitions


def _reward(definition):
    reward = {'kind': definition.reward_type, 'currency_id': None, 'item_id': None, 'amount': 0}
    if definition.reward_type == 'none':
        if definition.reward_amount != 0 or definition.currency_reward_id or definition.accessory_reward_id:
            raise ValueError('No-reward achievement contains a grant.')
    elif definition.reward_type == 'currency':
        if not definition.currency_reward_id or definition.accessory_reward_id or not 1 <= definition.reward_amount <= wallet.MAX_BALANCE or not _identifier(definition.currency_reward.name):
            raise ValueError('Achievement currency reward is invalid.')
        reward.update(currency_id=definition.currency_reward.name, amount=definition.reward_amount)
    elif definition.reward_type == 'accessory':
        if not definition.accessory_reward_id or definition.currency_reward_id or definition.reward_amount not in (0, 1):
            raise ValueError('Achievement cosmetic reward is invalid.')
        reward.update(item_id=str(definition.accessory_reward_id), amount=1)
    else:
        raise ValueError('Unsupported achievement reward.')
    return reward


def _target(definition):
    if definition.condition_type == 'one_time_trigger':
        return 1
    if definition.condition_type == 'collection_completion':
        return Accessory.objects.filter(type=definition.target_slot).count()
    return definition.target_count


def _evaluate(profile, definitions):
    entries = []
    for definition in definitions:
        progress, _ = UserAchievement.objects.select_for_update().get_or_create(player=profile, achievement=definition)
        target = _target(definition)
        count = progress.current_count
        if not 0 <= count <= wallet.MAX_BALANCE:
            raise ValueError('Achievement progress is out of bounds.')
        if not progress.is_unlocked:
            if definition.condition_type == 'collection_completion':
                count = UserAccessory.objects.filter(player=profile, accessory__type=definition.target_slot).count()
            elif definition.trigger_event == 'login':
                count = DailyAttendance.objects.filter(player=profile).count()
            progress.current_count = min(count, target)
            if count >= target:
                progress.is_unlocked = True
                progress.unlocked_at = timezone.now()
        if progress.is_unlocked and progress.unlocked_at is None:
            progress.unlocked_at = timezone.now()
        if progress.is_claimed and (not progress.is_unlocked or definition.reward_type == 'none'):
            raise ValueError('Achievement claim state is inconsistent.')
        progress.save(update_fields=['current_count', 'is_unlocked', 'unlocked_at'])
        entries.append({'achievement_id': definition.key, 'display_name': definition.title,
                        'description': definition.description, 'current_count': progress.current_count,
                        'target_count': target, 'is_unlocked': progress.is_unlocked, 'is_claimed': progress.is_claimed,
                        'unlocked_at': int(progress.unlocked_at.timestamp() * 1000) if progress.unlocked_at else 0,
                        'reward': _reward(definition)})
    return entries


@transaction.atomic
def record_purchase(profile, is_set, prices):
    # Called only from the successful Shop transaction. Failed/duplicate purchases
    # never reach this point, and a later purchase failure rolls this write back.
    PlayerProfile.objects.select_for_update().get(pk=profile.pk)
    spent = {entry['currency_id']: entry['amount'] for entry in prices}
    event = 'set_purchase' if is_set else 'purchase'
    for definition in _definitions():
        if definition.condition_type == 'collection_completion' or definition.trigger_event == 'login':
            continue
        amount = spent.get(definition.target_currency, 0) if definition.trigger_event == 'currency_spent' else (1 if definition.trigger_event == event else 0)
        if amount <= 0:
            continue
        progress, _ = UserAchievement.objects.select_for_update().get_or_create(player=profile, achievement=definition)
        if not progress.is_unlocked:
            if not 0 <= progress.current_count <= wallet.MAX_BALANCE:
                raise ValueError('Achievement progress is out of bounds.')
            progress.current_count = min(_target(definition), progress.current_count + amount)
            progress.save(update_fields=['current_count'])


@transaction.atomic
def snapshot(profile):
    PlayerProfile.objects.select_for_update().get(pk=profile.pk)
    definitions = _definitions()
    entries = _evaluate(profile, definitions)
    needs_wallet = any(entry['reward']['kind'] == 'currency' for entry in entries)
    needs_inventory = any(definition.condition_type == 'collection_completion' or definition.reward_type == 'accessory' for definition in definitions)
    if needs_wallet:
        list(Wallet.objects.select_for_update().filter(player=profile).order_by('currency_id'))
    balances = wallet.snapshot(profile) if needs_wallet else None
    owned = inventory.snapshot(profile) if needs_inventory else None
    if owned is not None:
        for item in owned['items']:
            if not _identifier(item['category_id']) or not _name(item['display_name']):
                raise ValueError('Achievement inventory export metadata is invalid.')
        if any(not _name(outfit['display_name']) for outfit in owned['sets']):
            raise ValueError('Achievement inventory set metadata is invalid.')
    result = {'achievements': {'requires_inventory': needs_inventory, 'entries': entries},
              'wallet': balances, 'inventory': owned, 'claimed_achievement_id': None}
    # These are the adapter's existing wire limits. Reject before committing a grant
    # whose confirmed state the sample client cannot consume.
    if balances is not None and (len(balances['balances']) > 256 or len(balances['offers']) > 128 or len(json.dumps(balances, ensure_ascii=True)) > 131072):
        raise ValueError('Achievement wallet export exceeds supported bounds.')
    if owned is not None and (len(owned['items']) > 4096 or len(owned['sets']) > 1024 or any(len(outfit['item_ids']) > 4096 for outfit in owned['sets']) or len(json.dumps(owned, ensure_ascii=True)) > 1048576):
        raise ValueError('Achievement inventory export exceeds supported bounds.')
    if len(json.dumps(result, ensure_ascii=True)) > 8388352:
        raise ValueError('Achievement export exceeds supported bounds.')
    return result


def validate_request(data):
    if not isinstance(data, dict) or set(data) != {'achievement_id'} or not _identifier(data['achievement_id']):
        raise ValueError('Only achievement_id is accepted.')
    return data['achievement_id']


@transaction.atomic
def claim(profile, achievement_id):
    PlayerProfile.objects.select_for_update().get(pk=profile.pk)
    definitions = _definitions()
    entries = {entry['achievement_id']: entry for entry in _evaluate(profile, definitions)}
    entry = entries.get(achievement_id)
    if entry is None:
        return {'code': 'achievement_unavailable'}, 400
    if entry['is_claimed']:
        return {'code': 'already_claimed'}, 400
    if not entry['is_unlocked']:
        return {'code': 'condition_not_met'}, 400
    definition = next(definition for definition in definitions if definition.key == achievement_id)
    reward = entry['reward']
    if reward['kind'] == 'none':
        return {'code': 'no_reward'}, 400
    if reward['kind'] == 'currency':
        balance, _ = Wallet.objects.select_for_update().get_or_create(player=profile, currency=definition.currency_reward, defaults={'amount': 0})
        if not 0 <= balance.amount <= wallet.MAX_BALANCE - reward['amount']:
            raise ValueError('Achievement reward exceeds the wallet balance bounds.')
        balance.amount += reward['amount']
        balance.save(update_fields=['amount'])
    else:
        UserAccessory.objects.get_or_create(player=profile, accessory=definition.accessory_reward)
    progress = UserAchievement.objects.get(player=profile, achievement=definition)
    progress.is_claimed = True
    progress.save(update_fields=['is_claimed'])
    result = snapshot(profile)
    if not any(entry['achievement_id'] == achievement_id and entry['is_claimed'] for entry in result['achievements']['entries']):
        raise ValueError('Claim result no longer contains the configured achievement.')
    result['claimed_achievement_id'] = achievement_id
    return result, 200
