from datetime import datetime, timezone, date
from unittest.mock import patch
from django.contrib.auth.models import User
from django.core.cache import cache
from django.test import TestCase
from rest_framework.test import APIClient
from shop.models import PlayerProfile, Currency, Wallet, MonthlyRewardSchedule, DailyAttendance

class DailyRewardsModuleTests(TestCase):
    def setUp(self):
        cache.clear()
        self.user=User.objects.create_user('daily-module'); self.profile=PlayerProfile.objects.create(user=self.user,platform_id="daily-module")
        self.gold=Currency.objects.create(name='gold'); self.wallet=Wallet.objects.create(player=self.profile,currency=self.gold,amount=100)
        MonthlyRewardSchedule.objects.create(year=2026,month=10,day=25,currency=self.gold,reward_amount=50)
        self.client=APIClient(); self.client.force_authenticate(self.user)
        clock=patch('django.utils.timezone.now',return_value=datetime(2026,10,25,23,59,tzinfo=timezone.utc));clock.start();self.addCleanup(clock.stop)
    def fetch(self): return self.client.get('/api/daily-rewards/state/')
    def claim(self,**body): return self.client.post('/api/daily-rewards/claim/',body or {'date':'2026-10-25'},format='json')
    def test_calendar_is_full_server_month_without_grant(self):
        response=self.fetch();self.assertEqual(response.status_code,200);self.assertEqual(response['Cache-Control'],'no-store')
        calendar=response.json()['calendar'];self.assertEqual(calendar['today_date'],'2026-10-25');self.assertEqual(len(calendar['days']),31)
        self.assertEqual(calendar['days'][24]['reward'],{'currency_id':'gold','amount':50});self.assertIsNone(calendar['days'][0]['reward'])
        self.wallet.refresh_from_db();self.assertEqual(self.wallet.amount,100)
    def test_claim_grants_once_and_returns_confirmed_account_state(self):
        response=self.claim();self.assertEqual(response.status_code,200);self.assertEqual(response.json()['claimed_date'],'2026-10-25')
        self.assertTrue(response.json()['calendar']['days'][24]['is_claimed']);self.assertEqual(response.json()['wallet']['balances'][0]['amount'],150)
        self.assertEqual(self.claim().json()['code'],'already_claimed');self.wallet.refresh_from_db();self.assertEqual(self.wallet.amount,150)
        self.assertEqual(DailyAttendance.objects.filter(player=self.profile).count(),1)
    def test_rollover_does_not_claim_new_day_from_old_display(self):
        with patch('django.utils.timezone.now',return_value=datetime(2026,10,26,tzinfo=timezone.utc)):
            self.assertEqual(self.claim().json()['code'],'day_changed');self.assertEqual(self.fetch().json()['calendar']['today_date'],'2026-10-26')
        self.assertFalse(DailyAttendance.objects.filter(player=self.profile).exists())
    def test_untrusted_grant_player_and_date_rejected(self):
        for body in [{'date':'2026-10-25','amount':999},{'date':'2026-10-25','player_id':'other'},{'date':'2026-10-26'},{'date':'bad'},{'date':25}]:self.assertEqual(self.claim(**body).status_code,400)
        self.assertFalse(DailyAttendance.objects.filter(player=self.profile).exists())
    def test_attendance_only_is_supported_without_wallet(self):
        with patch('shop.services.daily_rewards._ensure_monthly_rewards'):
            MonthlyRewardSchedule.objects.all().delete();response=self.claim();self.assertEqual(response.status_code,200)
            self.assertIsNone(response.json()['wallet']);self.assertTrue(response.json()['calendar']['days'][24]['is_claimed'])
        self.wallet.refresh_from_db();self.assertEqual(self.wallet.amount,100)
    def test_invalid_reward_or_overflow_cannot_record_attendance(self):
        MonthlyRewardSchedule.objects.update(reward_amount=-1);self.assertEqual(self.claim().status_code,409)
        MonthlyRewardSchedule.objects.update(reward_amount=50);self.wallet.amount=2147483647;self.wallet.save();self.assertEqual(self.claim().status_code,409)
        self.assertFalse(DailyAttendance.objects.filter(player=self.profile).exists())
    def test_unexportable_wallet_rolls_back_and_invalid_calendar_rejects(self):
        from shop.services import daily_rewards
        oversized={'balances':[{'currency_id':f'currency-{index}','amount':0} for index in range(257)],'offers':[]}
        with patch.object(daily_rewards.wallet,'snapshot',return_value=oversized):self.assertEqual(self.claim().status_code,409)
        self.wallet.refresh_from_db();self.assertEqual(self.wallet.amount,100);self.assertFalse(DailyAttendance.objects.filter(player=self.profile).exists())
        MonthlyRewardSchedule.objects.create(year=2026,month=10,day=32,currency=self.gold,reward_amount=1);self.assertEqual(self.fetch().status_code,409)
    def test_private_progress_and_missing_identity(self):
        other=PlayerProfile.objects.create(user=User.objects.create_user('other'),platform_id='other');DailyAttendance.objects.create(player=other,date=date(2026,10,25))
        self.assertFalse(self.fetch().json()['calendar']['days'][24]['is_claimed'])
        self.client.force_authenticate(User.objects.create_user('missing'));self.assertEqual(self.fetch().status_code,409)
        self.client.force_authenticate(None);self.assertEqual(self.fetch().status_code,401)
    def test_month_boundary_and_leap_year(self):
        for instant,count in [(datetime(2028,2,29,tzinfo=timezone.utc),29),(datetime(2026,11,1,tzinfo=timezone.utc),30)]:
            with patch('django.utils.timezone.now',return_value=instant):
                response=self.fetch();self.assertEqual(response.status_code,200);self.assertEqual(len(response.json()['calendar']['days']),count)

    def test_failure_after_grant_rolls_back_attendance_and_wallet(self):
        from shop.services import daily_rewards
        original = daily_rewards.snapshot
        calls = 0
        def result(profile, today=None):
            nonlocal calls
            calls += 1
            if calls == 2:
                raise ValueError('invalid post-grant export')
            return original(profile, today)
        with patch.object(daily_rewards, 'snapshot', side_effect=result):
            self.assertEqual(self.claim().status_code, 409)
        self.wallet.refresh_from_db(); self.assertEqual(self.wallet.amount, 100)
        self.assertFalse(DailyAttendance.objects.filter(player=self.profile).exists())
