"""Reference wallet backend: atomic balance conversion with integer arithmetic."""
import re
from django.db import transaction
from shop.models import Wallet

MAX_BALANCE = 2147483647
OFFERS = {
    'snowflakes-to-gold-v1': ('snowflakes', 'gold', 2, 1),
    'gears-to-gold-v1': ('gears', 'gold', 1, 25),
}


def snapshot(profile):
    balances = [{'currency_id': wallet.currency.name, 'amount': wallet.amount}
                for wallet in Wallet.objects.filter(player=profile).select_related('currency').order_by('currency__name')]
    if any(not re.fullmatch(r'[A-Za-z0-9_.:-]{1,50}', item['currency_id']) or not 0 <= item['amount'] <= MAX_BALANCE for item in balances):
        raise ValueError('Wallet contains invalid currency configuration or balance.')
    currencies = {item['currency_id'] for item in balances}
    offers = [{'offer_id': key, 'source_currency_id': source, 'target_currency_id': target,
               'source_units': source_units, 'target_units': target_units}
              for key, (source, target, source_units, target_units) in sorted(OFFERS.items())
              if source in currencies and target in currencies]
    return {'balances': balances, 'offers': offers}


def validate_request(data):
    if not isinstance(data, dict) or set(data) != {'offer_id', 'amount'}:
        raise ValueError('Only offer_id and amount are accepted.')
    offer_id = data['offer_id']
    amount = data['amount']
    if not isinstance(offer_id, str) or not re.fullmatch(r'[A-Za-z0-9_.:-]{1,128}', offer_id):
        raise ValueError('A conversion offer ID is required.')
    if type(amount) is not int or not 1 <= amount <= MAX_BALANCE:
        raise ValueError('Amount must be a positive 32-bit integer.')
    return offer_id, amount


def convert(profile, offer_id, amount):
    offer = OFFERS.get(offer_id)
    if offer is None:
        return {'code': 'offer_unavailable'}, 400
    source, target, source_units, target_units = offer
    if amount % source_units:
        return {'code': 'invalid_units'}, 400
    with transaction.atomic():
        wallets = {wallet.currency.name: wallet for wallet in Wallet.objects.select_for_update().filter(player=profile).select_related('currency').order_by('currency_id')}
        source_wallet, target_wallet = wallets.get(source), wallets.get(target)
        if source_wallet is None or target_wallet is None:
            return {'code': 'missing_wallet'}, 409
        if not 0 <= source_wallet.amount <= MAX_BALANCE or not 0 <= target_wallet.amount <= MAX_BALANCE:
            return {'code': 'invalid_wallet_configuration'}, 409
        if source_wallet.amount < amount:
            return {'code': 'insufficient_funds'}, 400
        credited = (amount // source_units) * target_units
        if credited > MAX_BALANCE or target_wallet.amount + credited > MAX_BALANCE:
            return {'code': 'balance_limit'}, 400
        source_wallet.amount -= amount
        target_wallet.amount += credited
        source_wallet.save(update_fields=['amount'])
        target_wallet.save(update_fields=['amount'])
        return snapshot(profile), 200
