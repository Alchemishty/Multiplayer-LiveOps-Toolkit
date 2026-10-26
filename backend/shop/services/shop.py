"""Reference cosmetic offers and atomic purchases; no receipt/history storage."""
import re
from django.db import transaction
from shop.models import Accessory, AccessorySet, PlayerProfile, UserAccessory, Wallet
from shop.services import inventory, wallet, achievements

SET_ONLY_TYPES = {'head', 'torso', 'arms', 'legs', 'feet'}


def _metadata(name, category):
    if not isinstance(name, str) or not name.strip() or len(name) > 256 or not re.fullmatch(r'[A-Za-z0-9_.:-]{1,128}', category):
        raise ValueError('Shop contains invalid display metadata.')


def _costs(costs):
    result = []
    for cost in sorted(costs, key=lambda value: value.currency_id):
        if not 0 <= cost.amount <= wallet.MAX_BALANCE or not re.fullmatch(r'[A-Za-z0-9_.:-]{1,50}', cost.currency.name):
            raise ValueError('Shop contains an invalid currency price.')
        result.append({'currency_id': cost.currency.name, 'amount': cost.amount})
    return result


@transaction.atomic
def snapshot(profile):
    PlayerProfile.objects.select_for_update().get(pk=profile.pk)
    # Hold account wallet rows through both reads, including concurrent conversion/reset.
    list(Wallet.objects.select_for_update().filter(player=profile).order_by('currency_id'))
    offers = []
    for item in Accessory.objects.exclude(type__in=SET_ONLY_TYPES).prefetch_related('costs__currency').order_by('id'):
        _metadata(item.name, item.type)
        offers.append({'offer_id': f'item:{item.id}', 'display_name': item.name, 'category_id': item.type,
                       'is_set': False, 'item_ids': [str(item.id)], 'costs': _costs(item.costs.all())})
    for outfit in AccessorySet.objects.prefetch_related('accessories', 'costs__currency').order_by('id'):
        item_ids = sorted((str(item.id) for item in outfit.accessories.all()), key=int)
        if not item_ids:
            continue
        _metadata(outfit.name, outfit.category)
        offers.append({'offer_id': f'set:{outfit.id}', 'display_name': outfit.name, 'category_id': outfit.category,
                       'is_set': True, 'item_ids': item_ids, 'costs': _costs(outfit.costs.all())})
    balances = wallet.snapshot(profile)
    owned = inventory.snapshot(profile)
    for item in owned['items']:
        _metadata(item['display_name'], item['category_id'])
    for outfit in owned['sets']:
        _metadata(outfit['display_name'], 'set')
    return {'offers': offers, 'wallet': balances, 'inventory': owned}


def validate_request(data):
    if not isinstance(data, dict) or set(data) != {'offer_id'} or not isinstance(data['offer_id'], str) or not re.fullmatch(r'(item|set):[1-9][0-9]{0,18}', data['offer_id']):
        raise ValueError('Only a valid offer_id is accepted.')
    if int(data['offer_id'].split(':')[1]) > 9223372036854775807:
        raise ValueError('Offer ID is outside the reference database bounds.')
    return data['offer_id']


def purchase(profile, offer_id):
    # Serialize this account's purchases even when no ownership row exists yet.
    with transaction.atomic():
        PlayerProfile.objects.select_for_update().get(pk=profile.pk)
        kind, content_id = offer_id.split(':')
        if kind == 'item':
            content = Accessory.objects.exclude(type__in=SET_ONLY_TYPES).filter(pk=int(content_id)).first()
            member_ids = [content.id] if content else []
        else:
            content = AccessorySet.objects.filter(pk=int(content_id)).first()
            member_ids = list(content.accessories.order_by('id').values_list('id', flat=True)) if content else []
        if content is None or not member_ids:
            return {'code': 'offer_unavailable'}, 400
        owned_ids = set(UserAccessory.objects.filter(player=profile).values_list('accessory_id', flat=True))
        unowned_ids = [item_id for item_id in member_ids if item_id not in owned_ids]
        if not unowned_ids:
            return {'code': 'already_owned'}, 400
        prices = _costs(content.costs.select_related('currency').all())
        wallets = {entry.currency.name: entry for entry in Wallet.objects.select_for_update().filter(player=profile).select_related('currency').order_by('currency_id')}
        for price in prices:
            account_wallet = wallets.get(price['currency_id'])
            if account_wallet is None or not 0 <= account_wallet.amount <= wallet.MAX_BALANCE:
                return {'code': 'invalid_shop_configuration'}, 409
            if account_wallet.amount < price['amount']:
                return {'code': 'insufficient_funds'}, 400
        for price in prices:
            account_wallet = wallets[price['currency_id']]
            account_wallet.amount -= price['amount']
            account_wallet.save(update_fields=['amount'])
        for item_id in unowned_ids:
            UserAccessory.objects.create(player=profile, accessory_id=item_id)
        achievements.record_purchase(profile, kind == 'set', prices)
        result = snapshot(profile)
        result['purchased_offer_id'] = offer_id
        return result, 200
