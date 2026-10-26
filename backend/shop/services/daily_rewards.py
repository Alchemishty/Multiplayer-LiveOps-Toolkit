"""Sample monthly attendance mechanic; UTC backend date and atomic grants."""
import calendar as cal
import json
import re
from datetime import date
from django.db import transaction, IntegrityError
from django.utils import timezone
from shop.models import MonthlyRewardSchedule, DailyAttendance, Currency, Wallet, PlayerProfile
from shop.services import wallet

def _ensure_monthly_rewards(year, month):
    """Auto-generate a reward schedule for a month if none exists."""
    if MonthlyRewardSchedule.objects.filter(year=year, month=month).exists():
        return

    days_in_month = cal.monthrange(year, month)[1]
    gold = Currency.objects.filter(name="gold").first()
    if not gold:
        raise ValueError(
            "_ensure_monthly_rewards: 'gold' currency not found — seed the database first"
        )
    snowflakes = Currency.objects.filter(name="snowflakes").first()
    gears = Currency.objects.filter(name="gears").first()

    rewards_to_create = []
    for day in range(1, days_in_month + 1):
        if day == days_in_month:
            currency, amount = gold, 200
        elif day % 14 == 0 and gears:
            currency, amount = gears, 3
        elif day % 7 == 0 and snowflakes:
            currency, amount = snowflakes, 15
        elif day % 5 == 0:
            currency, amount = gold, 100
        else:
            currency, amount = gold, 50
        rewards_to_create.append(
            MonthlyRewardSchedule(
                year=year, month=month, day=day,
                currency=currency, reward_amount=amount,
            )
        )

    try:
        with transaction.atomic():
            if not MonthlyRewardSchedule.objects.filter(year=year, month=month).exists():
                MonthlyRewardSchedule.objects.bulk_create(rewards_to_create)
    except IntegrityError:
        pass



def validate_request(data):
    if not isinstance(data, dict) or set(data) != {'date'} or not isinstance(data['date'], str) or not re.fullmatch(r'\d{4}-\d{2}-\d{2}', data['date']):
        raise ValueError('Only the displayed ISO date is accepted.')
    return date.fromisoformat(data['date'])


@transaction.atomic
def snapshot(profile, today=None):
    PlayerProfile.objects.select_for_update().get(pk=profile.pk)
    today = today or timezone.now().date()
    _ensure_monthly_rewards(today.year, today.month)
    count = cal.monthrange(today.year, today.month)[1]
    schedules = list(MonthlyRewardSchedule.objects.filter(year=today.year, month=today.month).select_related('currency'))
    if any(not 1 <= reward.day <= count or not 1 <= reward.reward_amount <= wallet.MAX_BALANCE or re.fullmatch(r'[A-Za-z0-9_.:-]{1,128}', reward.currency.name) is None for reward in schedules):
        raise ValueError('Daily reward schedule contains invalid day/currency/amount.')
    rewards = {reward.day: {'currency_id': reward.currency.name, 'amount': reward.reward_amount} for reward in schedules}
    claimed = set(DailyAttendance.objects.filter(player=profile, date__year=today.year, date__month=today.month).values_list('date__day', flat=True))
    if rewards:
        list(Wallet.objects.select_for_update().filter(player=profile).order_by('currency_id'))
    balances = wallet.snapshot(profile) if rewards else None
    if balances is not None and (len(balances['balances']) > 256 or len(balances['offers']) > 128 or len(json.dumps(balances)) > 131072):
        raise ValueError('Daily reward wallet export exceeds supported bounds.')
    return {'calendar': {'today_date': today.isoformat(), 'days': [
        {'day': day, 'reward': rewards.get(day), 'is_claimed': day in claimed} for day in range(1, count + 1)]},
        'wallet': balances, 'claimed_date': None}


@transaction.atomic
def claim(profile, expected_date):
    PlayerProfile.objects.select_for_update().get(pk=profile.pk)
    today = timezone.now().date()
    if expected_date != today:
        return {'code': 'day_changed'}, 400
    state = snapshot(profile, today)
    if state['calendar']['days'][today.day - 1]['is_claimed']:
        return {'code': 'already_claimed'}, 400
    reward = state['calendar']['days'][today.day - 1]['reward']
    if reward is not None:
        currency = Currency.objects.get(name=reward['currency_id'])
        balance, _ = Wallet.objects.select_for_update().get_or_create(player=profile, currency=currency, defaults={'amount': 0})
        if not 0 <= balance.amount <= wallet.MAX_BALANCE - reward['amount']:
            raise ValueError('Daily reward exceeds wallet balance bounds.')
        balance.amount += reward['amount']
        balance.save(update_fields=['amount'])
    DailyAttendance.objects.create(player=profile, date=today)
    result = snapshot(profile, today)
    result['claimed_date'] = today.isoformat()
    return result, 200
