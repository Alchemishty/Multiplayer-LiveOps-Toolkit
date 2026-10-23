# Security & Anti-Cheat Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the inverted trust model so the server is authoritative over game-critical state (achievements, equip, movement), close race conditions, and add rate limiting.

**Architecture:** Server-side validation for all economy-mutating operations. Backend views gain condition evaluation logic and atomic ownership checks. Unity client gains ServerRpc-gated accessory sync and NetworkTransform for movement. Rate limiting via DRF throttle classes.

**Tech Stack:** Django REST Framework (backend), Unity C# with Netcode for GameObjects (client), NUnit (tests)

**Spec:** Security audit findings from conversation on 2026-10-21. Issues numbered #1-#20, subset in scope per user triage.

## Global Constraints

- Backend tests: `cd ~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend && .venv/bin/python manage.py test shop`
- Unity tests: run via MCP `run_tests` or Unity Test Runner
- No git operations — all work is local
- Every code change must include tests
- SetupExampleScene.cs must stay in sync with any new components
- Follow existing patterns: coroutine async, throw-on-null, SOLID, descriptive names

## Review Focus

1. **Concurrent daily-login claims** — two simultaneous POST requests should result in exactly one attendance record and one wallet credit, never two
2. **Achievement unlock with fabricated count** — POST to `/api/achievements/unlock/` with `current_count: 999` for a `threshold_counter` achievement with `target_count: 5` must be rejected
3. **Equip unowned accessory via direct API call** — POST to `/api/accessories/update-equipped/` with an accessory ID the player hasn't purchased must return 400 with no equip state changes
4. **NetworkVariable write of unowned accessory ID** — a cheating client writing arbitrary IDs to `AccessoryState` must not result in those visuals rendering on remote clients
5. **Rate limit bypass via rapid requests** — 100 purchase requests in 1 second should be throttled after the configured burst limit

---

### Task 1: Server-Side Achievement Validation (#1)

**Files:**
- Modify: `~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend/shop/views/rewards_views.py:245-318`
- Modify: `~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend/shop/models.py` (add helper method)
- Test: `~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend/shop/tests.py`

**Interfaces:**
- Consumes: `Achievement` model fields (`condition_type`, `trigger_event`, `target_count`, `target_slot`, `target_currency`), `UserAccessory`, `UserAchievement`, `DailyAttendance`, `Wallet`
- Produces: `report_achievement_unlock` now validates server-side before marking unlocked; `report_achievement_progress` now computes count from server state instead of trusting client

**Design:** The server must evaluate achievement conditions from its own data:
- `one_time_trigger`: check that the relevant action has occurred at least once (e.g., `UserAccessory.exists()` for "first-purchase", `DailyAttendance.exists()` for "first-login")
- `threshold_counter`: compute the actual count from server state (e.g., `UserAccessory.count()` for purchase achievements, `DailyAttendance.count()` for login achievements, sum of `AccessoryCost` amounts for currency-spent achievements)
- `collection_completion`: check that all accessories of `target_slot` type are owned

- [ ] **Step 1: Write tests for server-side achievement validation**

Add to `shop/tests.py`:

```python
class AchievementValidationTests(ShopTestBase):

    def setUp(self):
        super().setUp()
        self.first_purchase = Achievement.objects.create(
            key="first-purchase", title="Window Shopper",
            description="Make your first purchase",
            condition_type="one_time_trigger",
            trigger_event="purchase", target_count=0,
            is_active=True,
        )
        self.buy_5 = Achievement.objects.create(
            key="buy-5", title="Regular Customer",
            description="Purchase 5 accessories",
            condition_type="threshold_counter",
            trigger_event="purchase", target_count=5,
            is_active=True,
        )
        self.all_headwear = Achievement.objects.create(
            key="all-headwear", title="Hat Trick",
            description="Own every headwear accessory",
            condition_type="collection_completion",
            trigger_event="purchase", target_count=0,
            target_slot="headwear", is_active=True,
        )
        self.login_7 = Achievement.objects.create(
            key="login-7", title="Dedicated Player",
            description="Log in 7 times",
            condition_type="threshold_counter",
            trigger_event="login", target_count=7,
            is_active=True,
        )
        self.spend_gold = Achievement.objects.create(
            key="spend-gold-500", title="Gold Digger",
            description="Spend 500 gold",
            condition_type="threshold_counter",
            trigger_event="currency_spent", target_count=500,
            target_currency="gold", is_active=True,
        )

    def test_unlock_one_time_trigger_with_no_purchases_rejected(self):
        resp = self.client.post(
            "/api/achievements/unlock/",
            {"achievement_id": "first-purchase", "current_count": 1},
            format="json",
        )
        self.assertEqual(resp.status_code, 400)
        self.assertIn("not met", resp.data["message"].lower())

    def test_unlock_one_time_trigger_with_purchase_accepted(self):
        UserAccessory.objects.create(player=self.profile, accessory=self.hat)
        resp = self.client.post(
            "/api/achievements/unlock/",
            {"achievement_id": "first-purchase", "current_count": 1},
            format="json",
        )
        self.assertEqual(resp.status_code, 200)
        ua = UserAchievement.objects.get(
            player=self.profile, achievement=self.first_purchase
        )
        self.assertTrue(ua.is_unlocked)

    def test_unlock_threshold_counter_with_fabricated_count_rejected(self):
        UserAccessory.objects.create(player=self.profile, accessory=self.hat)
        resp = self.client.post(
            "/api/achievements/unlock/",
            {"achievement_id": "buy-5", "current_count": 999},
            format="json",
        )
        self.assertEqual(resp.status_code, 400)

    def test_unlock_threshold_counter_when_met(self):
        for acc in [self.hat, self.cape, self.crown, self.expensive]:
            UserAccessory.objects.create(player=self.profile, accessory=acc)
        fifth = Accessory.objects.create(name="Fifth", type="back")
        UserAccessory.objects.create(player=self.profile, accessory=fifth)
        resp = self.client.post(
            "/api/achievements/unlock/",
            {"achievement_id": "buy-5", "current_count": 5},
            format="json",
        )
        self.assertEqual(resp.status_code, 200)

    def test_unlock_collection_completion_incomplete_rejected(self):
        UserAccessory.objects.create(player=self.profile, accessory=self.hat)
        resp = self.client.post(
            "/api/achievements/unlock/",
            {"achievement_id": "all-headwear", "current_count": 1},
            format="json",
        )
        self.assertEqual(resp.status_code, 400)

    def test_unlock_collection_completion_when_complete(self):
        UserAccessory.objects.create(player=self.profile, accessory=self.hat)
        UserAccessory.objects.create(player=self.profile, accessory=self.crown)
        resp = self.client.post(
            "/api/achievements/unlock/",
            {"achievement_id": "all-headwear", "current_count": 2},
            format="json",
        )
        self.assertEqual(resp.status_code, 200)

    def test_unlock_login_threshold_not_met(self):
        resp = self.client.post(
            "/api/achievements/unlock/",
            {"achievement_id": "login-7", "current_count": 7},
            format="json",
        )
        self.assertEqual(resp.status_code, 400)

    def test_unlock_login_threshold_met(self):
        from datetime import timedelta
        for i in range(7):
            DailyAttendance.objects.create(
                player=self.profile,
                date=date.today() - timedelta(days=i),
            )
        resp = self.client.post(
            "/api/achievements/unlock/",
            {"achievement_id": "login-7", "current_count": 7},
            format="json",
        )
        self.assertEqual(resp.status_code, 200)

    def test_progress_update_uses_server_count(self):
        UserAccessory.objects.create(player=self.profile, accessory=self.hat)
        UserAccessory.objects.create(player=self.profile, accessory=self.cape)
        resp = self.client.post(
            "/api/achievements/progress/update/",
            {"achievement_id": "buy-5", "current_count": 999},
            format="json",
        )
        self.assertEqual(resp.status_code, 200)
        ua = UserAchievement.objects.get(
            player=self.profile, achievement=self.buy_5
        )
        self.assertEqual(ua.current_count, 2)

    def test_unlock_already_unlocked_returns_400(self):
        UserAccessory.objects.create(player=self.profile, accessory=self.hat)
        UserAchievement.objects.create(
            player=self.profile, achievement=self.first_purchase,
            is_unlocked=True, current_count=1,
        )
        resp = self.client.post(
            "/api/achievements/unlock/",
            {"achievement_id": "first-purchase", "current_count": 1},
            format="json",
        )
        self.assertEqual(resp.status_code, 400)
        self.assertIn("already unlocked", resp.data["message"].lower())
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend && .venv/bin/python manage.py test shop.tests.AchievementValidationTests -v2`
Expected: Multiple FAILs — the current views accept any client count.

- [ ] **Step 3: Implement server-side condition evaluation**

In `rewards_views.py`, add a helper function and modify both views:

```python
# Add these imports at the top
from shop.models import (
    Achievement, UserAchievement,
    MonthlyRewardSchedule, DailyAttendance, Currency, Wallet,
    UserAccessory, Accessory, AccessoryCost,
)


def _compute_server_count(achievement, profile):
    """Compute the actual achievement progress from server state."""
    if achievement.condition_type == "one_time_trigger":
        return _compute_one_time_trigger_count(achievement, profile)
    elif achievement.condition_type == "threshold_counter":
        return _compute_threshold_count(achievement, profile)
    elif achievement.condition_type == "collection_completion":
        return _compute_collection_count(achievement, profile)
    return 0


def _compute_one_time_trigger_count(achievement, profile):
    if achievement.trigger_event == "purchase":
        return 1 if UserAccessory.objects.filter(player=profile).exists() else 0
    elif achievement.trigger_event == "set_purchase":
        from shop.models import AccessorySet
        for acc_set in AccessorySet.objects.prefetch_related("accessories"):
            acc_ids = set(acc_set.accessories.values_list("id", flat=True))
            owned_ids = set(
                UserAccessory.objects.filter(
                    player=profile, accessory_id__in=acc_ids
                ).values_list("accessory_id", flat=True)
            )
            if acc_ids and acc_ids == owned_ids:
                return 1
        return 0
    elif achievement.trigger_event == "login":
        return 1 if DailyAttendance.objects.filter(player=profile).exists() else 0
    return 0


def _compute_threshold_count(achievement, profile):
    if achievement.trigger_event == "purchase":
        return UserAccessory.objects.filter(player=profile).count()
    elif achievement.trigger_event == "set_purchase":
        from shop.models import AccessorySet
        count = 0
        for acc_set in AccessorySet.objects.prefetch_related("accessories"):
            acc_ids = set(acc_set.accessories.values_list("id", flat=True))
            owned_ids = set(
                UserAccessory.objects.filter(
                    player=profile, accessory_id__in=acc_ids
                ).values_list("accessory_id", flat=True)
            )
            if acc_ids and acc_ids == owned_ids:
                count += 1
        return count
    elif achievement.trigger_event == "login":
        return DailyAttendance.objects.filter(player=profile).count()
    elif achievement.trigger_event == "currency_spent":
        target_currency = achievement.target_currency
        total_spent = 0
        owned_accessories = UserAccessory.objects.filter(
            player=profile
        ).values_list("accessory_id", flat=True)
        costs = AccessoryCost.objects.filter(
            accessory_id__in=owned_accessories,
            currency__name=target_currency,
        )
        for cost in costs:
            total_spent += cost.amount
        return total_spent
    return 0


def _compute_collection_count(achievement, profile):
    target_slot = achievement.target_slot
    all_in_slot = set(
        Accessory.objects.filter(type=target_slot).values_list("id", flat=True)
    )
    owned_in_slot = set(
        UserAccessory.objects.filter(
            player=profile, accessory_id__in=all_in_slot
        ).values_list("accessory_id", flat=True)
    )
    return len(owned_in_slot)


def _is_condition_met(achievement, profile):
    """Check whether the achievement condition is satisfied from server state."""
    server_count = _compute_server_count(achievement, profile)
    if achievement.condition_type == "one_time_trigger":
        return server_count >= 1
    elif achievement.condition_type == "threshold_counter":
        return server_count >= achievement.target_count
    elif achievement.condition_type == "collection_completion":
        target_slot = achievement.target_slot
        total_in_slot = Accessory.objects.filter(type=target_slot).count()
        return total_in_slot > 0 and server_count >= total_in_slot
    return False
```

Then replace `report_achievement_unlock` (lines 245-282):

```python
@api_view(["POST"])
@permission_classes([IsAuthenticated])
def report_achievement_unlock(request):
    profile = request.user.player_profile
    achievement_id = request.data.get("achievement_id")
    current_count = request.data.get("current_count", 0)

    if not achievement_id:
        return Response({"message": "achievement_id is required"}, status=400)

    try:
        achievement = Achievement.objects.get(key=achievement_id, is_active=True)
    except Achievement.DoesNotExist:
        return Response({"message": f"Achievement '{achievement_id}' not found"}, status=400)

    user_achievement, _ = UserAchievement.objects.get_or_create(
        player=profile,
        achievement=achievement,
        defaults={"current_count": 0},
    )

    if user_achievement.is_unlocked:
        return Response({"message": "Achievement already unlocked"}, status=400)

    if not _is_condition_met(achievement, profile):
        server_count = _compute_server_count(achievement, profile)
        return Response(
            {"message": f"Condition not met. Server count: {server_count}"},
            status=400,
        )

    server_count = _compute_server_count(achievement, profile)
    user_achievement.current_count = server_count
    user_achievement.is_unlocked = True
    user_achievement.unlocked_at = timezone.now()
    user_achievement.save()

    return Response({"success": True})
```

And replace `report_achievement_progress` (lines 285-318):

```python
@api_view(["POST"])
@permission_classes([IsAuthenticated])
def report_achievement_progress(request):
    profile = request.user.player_profile
    achievement_id = request.data.get("achievement_id")

    if not achievement_id:
        return Response({"message": "achievement_id is required"}, status=400)

    try:
        achievement = Achievement.objects.get(key=achievement_id, is_active=True)
    except Achievement.DoesNotExist:
        return Response({"message": f"Achievement '{achievement_id}' not found"}, status=400)

    user_achievement, _ = UserAchievement.objects.get_or_create(
        player=profile,
        achievement=achievement,
        defaults={"current_count": 0},
    )

    if not user_achievement.is_unlocked:
        server_count = _compute_server_count(achievement, profile)
        user_achievement.current_count = server_count
        user_achievement.save()

    return Response({"success": True})
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd ~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend && .venv/bin/python manage.py test shop -v2`
Expected: All tests PASS including existing tests.

---

### Task 2: Server-Side Equip Ownership Inside Atomic (#3, #11)

**Files:**
- Modify: `~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend/shop/views/shop_views.py:135-171`
- Modify: `~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend/shop/models.py` (add slot uniqueness)
- Test: `~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend/shop/tests.py`

**Interfaces:**
- Consumes: `UserAccessory` model, `Accessory` model (type field for slot uniqueness)
- Produces: `update_equipped_accessories` with atomic ownership check + one-item-per-slot enforcement

- [ ] **Step 1: Write tests for atomic ownership and slot uniqueness**

Add to `shop/tests.py`:

```python
class EquipSecurityTests(ShopTestBase):

    def setUp(self):
        super().setUp()
        self.ua_hat = UserAccessory.objects.create(
            player=self.profile, accessory=self.hat
        )
        self.ua_cape = UserAccessory.objects.create(
            player=self.profile, accessory=self.cape
        )

    def test_equip_unowned_id_rejected_atomically(self):
        """Unowned ID in list means nothing changes — atomic rollback."""
        self.ua_hat.is_equipped = True
        self.ua_hat.save()

        resp = self.client.post(
            "/api/accessories/update-equipped/",
            {"accessory_ids": [self.hat.id, 99999]},
            format="json",
        )
        self.assertEqual(resp.status_code, 400)
        self.ua_hat.refresh_from_db()
        self.assertTrue(self.ua_hat.is_equipped)

    def test_equip_duplicate_slot_keeps_last(self):
        """Two headwear items — only the last one in the list is equipped."""
        self.ua_crown = UserAccessory.objects.create(
            player=self.profile, accessory=self.crown
        )
        resp = self.client.post(
            "/api/accessories/update-equipped/",
            {"accessory_ids": [self.hat.id, self.crown.id, self.cape.id]},
            format="json",
        )
        self.assertEqual(resp.status_code, 200)
        equipped = set(
            UserAccessory.objects.filter(
                player=self.profile, is_equipped=True
            ).values_list("accessory_id", flat=True)
        )
        self.assertIn(self.cape.id, equipped)
        self.assertIn(self.crown.id, equipped)
        self.assertNotIn(self.hat.id, equipped)
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd ~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend && .venv/bin/python manage.py test shop.tests.EquipSecurityTests -v2`

- [ ] **Step 3: Move ownership check inside atomic, add slot enforcement**

Replace `update_equipped_accessories` in `shop_views.py`:

```python
@api_view(["POST"])
@permission_classes([IsAuthenticated])
def update_equipped_accessories(request):
    profile = request.user.player_profile
    accessory_ids = request.data.get("accessory_ids", [])

    with transaction.atomic():
        owned = {
            ua.accessory_id: ua
            for ua in UserAccessory.objects.select_for_update().filter(player=profile)
        }

        for accessory_id in accessory_ids:
            if accessory_id not in owned:
                return Response(
                    {"message": f"User does not own accessory with ID {accessory_id}."},
                    status=400,
                )

        # Resolve slot conflicts: last item per type wins
        slot_winners = {}
        for accessory_id in accessory_ids:
            accessory = Accessory.objects.get(id=accessory_id)
            slot_winners[accessory.type] = accessory_id

        final_equip_ids = set(slot_winners.values())

        UserAccessory.objects.filter(player=profile).update(is_equipped=False)
        UserAccessory.objects.filter(
            player=profile, accessory_id__in=final_equip_ids
        ).update(is_equipped=True)

    return Response({"message": "Equipped accessories updated successfully."})
```

- [ ] **Step 4: Run all equip + existing tests**

Run: `cd ~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend && .venv/bin/python manage.py test shop -v2`
Expected: All PASS including existing EquipTests.

---

### Task 3: Daily Login Race Condition Fix (#5) + Monthly Rewards Race (#13)

**Files:**
- Modify: `~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend/shop/views/rewards_views.py:106-156` (claim_daily_login)
- Modify: `~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend/shop/views/rewards_views.py:15-45` (_ensure_monthly_rewards)
- Test: `~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend/shop/tests.py`

**Interfaces:**
- Consumes: `DailyAttendance`, `MonthlyRewardSchedule`, `Wallet`
- Produces: Race-safe `claim_daily_login` and `_ensure_monthly_rewards`

- [ ] **Step 1: Write concurrency test for daily login**

```python
class DailyLoginRaceTests(ShopTestBase):

    @patch("shop.views.rewards_views.date")
    def test_double_claim_returns_400_on_second(self, mock_date):
        mock_date.today.return_value = date(2026, 10, 15)
        mock_date.side_effect = lambda *a, **kw: date(*a, **kw)
        MonthlyRewardSchedule.objects.create(
            year=2026, month=10, day=15, currency=self.gold, reward_amount=100,
        )
        resp1 = self.client.post("/api/daily-login/claim/")
        self.assertEqual(resp1.status_code, 200)
        resp2 = self.client.post("/api/daily-login/claim/")
        self.assertEqual(resp2.status_code, 400)
        self.wallet_gold.refresh_from_db()
        self.assertEqual(self.wallet_gold.amount, 600)
        self.assertEqual(
            DailyAttendance.objects.filter(
                player=self.profile, date=date(2026, 10, 15)
            ).count(), 1
        )
```

- [ ] **Step 2: Fix `claim_daily_login` — move exists-check inside atomic**

```python
@api_view(["POST"])
@permission_classes([IsAuthenticated])
def claim_daily_login(request):
    profile = request.user.player_profile
    today = date.today()

    _ensure_monthly_rewards(today.year, today.month)

    reward = MonthlyRewardSchedule.objects.filter(
        year=today.year, month=today.month, day=today.day,
    ).select_related("currency").first()

    try:
        with transaction.atomic():
            if DailyAttendance.objects.filter(player=profile, date=today).exists():
                return Response({"message": "Already claimed today"}, status=400)

            DailyAttendance.objects.create(player=profile, date=today)

            if reward:
                wallet, _ = Wallet.objects.select_for_update().get_or_create(
                    player=profile,
                    currency=reward.currency,
                    defaults={"amount": 0},
                )
                wallet.amount += reward.reward_amount
                wallet.save()
    except IntegrityError:
        return Response({"message": "Already claimed today"}, status=400)

    if reward:
        message = f"Claimed {reward.reward_amount} {reward.currency.name}!"
        reward_data = {
            "currency": reward.currency.name,
            "amount": reward.reward_amount,
        }
    else:
        message = "Daily attendance recorded!"
        reward_data = None

    return Response({
        "day": today.day,
        "reward": reward_data,
        "message": message,
    })
```

Add `from django.db import transaction, IntegrityError` at top.

- [ ] **Step 3: Fix `_ensure_monthly_rewards` — wrap in atomic, catch IntegrityError**

```python
def _ensure_monthly_rewards(year, month):
    if MonthlyRewardSchedule.objects.filter(year=year, month=month).exists():
        return

    days_in_month = cal.monthrange(year, month)[1]
    gold = Currency.objects.filter(name="gold").first()
    snowflakes = Currency.objects.filter(name="snowflakes").first()
    gears = Currency.objects.filter(name="gears").first()
    if not gold:
        return

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
```

- [ ] **Step 4: Run all backend tests**

Run: `cd ~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend && .venv/bin/python manage.py test shop -v2`
Expected: All PASS including existing daily login tests.

---

### Task 4: Rate Limiting (#4)

**Files:**
- Modify: `~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend/shop_backend/settings.py`
- Create: `~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend/shop/throttles.py`
- Modify: `~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend/shop/views/shop_views.py`
- Modify: `~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend/shop/views/rewards_views.py`
- Modify: `~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend/shop/views/wallet_views.py`
- Test: `~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend/shop/tests.py`

**Interfaces:**
- Consumes: DRF throttle framework
- Produces: Economy-mutating endpoints throttled at configurable rates

- [ ] **Step 1: Create throttle classes**

Create `shop/throttles.py`:

```python
from rest_framework.throttling import UserRateThrottle


class EconomyMutationThrottle(UserRateThrottle):
    scope = "economy_mutation"


class ClaimThrottle(UserRateThrottle):
    scope = "claim"
```

- [ ] **Step 2: Add throttle rates to settings**

In `settings.py`, update `REST_FRAMEWORK`:

```python
REST_FRAMEWORK = {
    "DEFAULT_AUTHENTICATION_CLASSES": (
        "rest_framework_simplejwt.authentication.JWTAuthentication",
    ),
    "DEFAULT_THROTTLE_RATES": {
        "economy_mutation": "30/min",
        "claim": "5/min",
    },
}
```

- [ ] **Step 3: Add throttle decorators to mutating views**

Import in each views file: `from rest_framework.decorators import api_view, permission_classes, throttle_classes`
Import: `from shop.throttles import EconomyMutationThrottle, ClaimThrottle`

Add `@throttle_classes([EconomyMutationThrottle])` to:
- `shop_views.py`: `purchase_accessory`, `purchase_set`, `update_equipped_accessories`
- `wallet_views.py`: `convert_currency`

Add `@throttle_classes([ClaimThrottle])` to:
- `rewards_views.py`: `claim_daily_login`, `report_achievement_unlock`

Example:
```python
@api_view(["POST"])
@permission_classes([IsAuthenticated])
@throttle_classes([EconomyMutationThrottle])
def purchase_accessory(request):
    ...
```

- [ ] **Step 4: Write throttle verification test**

```python
class ThrottleConfigTests(TestCase):
    def test_economy_throttle_scope_configured(self):
        from shop.throttles import EconomyMutationThrottle
        self.assertEqual(EconomyMutationThrottle.scope, "economy_mutation")

    def test_claim_throttle_scope_configured(self):
        from shop.throttles import ClaimThrottle
        self.assertEqual(ClaimThrottle.scope, "claim")
```

- [ ] **Step 5: Run all backend tests**

Run: `cd ~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend && .venv/bin/python manage.py test shop -v2`

---

### Task 5: Pagination + Calendar Input Validation (#14)

**Files:**
- Modify: `~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend/shop_backend/settings.py`
- Modify: `~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend/shop/views/rewards_views.py`
- Test: `~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend/shop/tests.py`

**Interfaces:**
- Consumes: DRF pagination, `get_daily_login_calendar` view
- Produces: Paginated list defaults, bounded calendar parameters

- [ ] **Step 1: Add default pagination to settings**

Add to `REST_FRAMEWORK` in `settings.py`:
```python
    "DEFAULT_PAGINATION_CLASS": "rest_framework.pagination.PageNumberPagination",
    "PAGE_SIZE": 50,
```

- [ ] **Step 2: Add calendar parameter validation**

In `rewards_views.py`, in `get_daily_login_calendar`, replace the year/month parsing (lines 64-65):

```python
    try:
        year = int(request.query_params.get("year", today.year))
        month = int(request.query_params.get("month", today.month))
    except (ValueError, TypeError):
        return Response({"message": "Invalid year or month"}, status=400)

    if month < 1 or month > 12 or year < 2020 or year > 2100:
        return Response({"message": "Year/month out of range"}, status=400)
```

- [ ] **Step 3: Write calendar bounds tests**

```python
class CalendarBoundsTests(ShopTestBase):

    def test_calendar_rejects_invalid_month(self):
        resp = self.client.get("/api/daily-login/calendar/?month=13")
        self.assertEqual(resp.status_code, 400)

    def test_calendar_rejects_month_zero(self):
        resp = self.client.get("/api/daily-login/calendar/?month=0")
        self.assertEqual(resp.status_code, 400)

    def test_calendar_rejects_far_future_year(self):
        resp = self.client.get("/api/daily-login/calendar/?year=9999")
        self.assertEqual(resp.status_code, 400)

    def test_calendar_rejects_non_numeric(self):
        resp = self.client.get("/api/daily-login/calendar/?month=abc")
        self.assertEqual(resp.status_code, 400)

    def test_calendar_accepts_valid_range(self):
        resp = self.client.get("/api/daily-login/calendar/?year=2026&month=6")
        self.assertEqual(resp.status_code, 200)
```

- [ ] **Step 4: Run all backend tests**

Run: `cd ~/Desktop/Cloudy/multiplayer-liveops-toolkit/backend && .venv/bin/python manage.py test shop -v2`

---

### Task 6: NetworkVariable Validation via ServerRpc (#9)

**Files:**
- Modify: `/Volumes/Samsung T7/Cloudy/MultiplayerLiveOpsToolkit/Assets/Scripts/Player/PlayerAccessory.cs`
- Modify: `/Volumes/Samsung T7/Cloudy/MultiplayerLiveOpsToolkit/Assets/Scripts/Data/AccessoryLoadout.cs`
- Modify: `/Volumes/Samsung T7/Cloudy/MultiplayerLiveOpsToolkit/Assets/Scripts/Registry/AccessoryRegistry.cs`

**Interfaces:**
- Consumes: `AccessoryRegistry.Instance`, `AccessoryLoadout`, `ShopCatalog`
- Produces: `PlayerAccessory` uses `WritePermission.Server` with `RequestAccessoryUpdateServerRpc`; `AccessoryLoadout.SerializeAccessoryState` added; `AccessoryRegistry.IsAccessoryOwned` added

- [ ] **Step 1: Add `SerializeAccessoryState` to `AccessoryLoadout`**

In `AccessoryLoadout.cs`, add:

```csharp
public static string SerializeAccessoryState(Dictionary<AccessoryType, int> state)
{
    if (state == null || state.Count == 0) return "";
    var dict = new Dictionary<string, int>();
    foreach (var kvp in state)
        dict[kvp.Key.ToString()] = kvp.Value;
    return Newtonsoft.Json.JsonConvert.SerializeObject(dict);
}
```

- [ ] **Step 2: Add `IsAccessoryOwned` to `AccessoryRegistry`**

In `AccessoryRegistry.cs`, add:

```csharp
public bool IsAccessoryOwned(int accessoryId)
{
    if (activeShopCatalog == null) return false;
    var shopItem = activeShopCatalog.GetItemById(accessoryId);
    return shopItem != null && shopItem.isOwned;
}
```

- [ ] **Step 3: Change NetworkVariable to server-write, add ServerRpc**

In `PlayerAccessory.cs`, change lines 26-31:
```csharp
public NetworkVariable<AccessoryState> AccessoryJson =
    new NetworkVariable<AccessoryState>(
        new AccessoryState(""),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
```

Add the validation ServerRpc:
```csharp
[ServerRpc]
private void RequestAccessoryUpdateServerRpc(string accessoryJson, ServerRpcParams rpcParams = default)
{
    if (string.IsNullOrEmpty(accessoryJson))
    {
        AccessoryJson.Value = new AccessoryState("");
        return;
    }

    Dictionary<AccessoryType, int> requestedState;
    try
    {
        requestedState = AccessoryLoadout.DeserializeAccessoryState(accessoryJson);
    }
    catch (System.Exception exception)
    {
        Debug.LogWarning($"PlayerAccessory: rejected malformed accessory JSON from client {rpcParams.Receive.SenderClientId}: {exception.Message}");
        return;
    }

    if (AccessoryRegistry.Instance == null)
    {
        Debug.LogWarning("PlayerAccessory: AccessoryRegistry not available for validation");
        return;
    }

    var validatedState = new Dictionary<AccessoryType, int>();
    foreach (var kvp in requestedState)
    {
        if (AccessoryRegistry.Instance.IsAccessoryOwned(kvp.Value))
            validatedState[kvp.Key] = kvp.Value;
        else
            Debug.LogWarning($"PlayerAccessory: rejected unowned accessory {kvp.Value} from client {rpcParams.Receive.SenderClientId}");
    }

    string validatedJson = AccessoryLoadout.SerializeAccessoryState(validatedState);
    AccessoryJson.Value = new AccessoryState(validatedJson);
}
```

- [ ] **Step 4: Replace all direct `AccessoryJson.Value =` with ServerRpc**

Every `AccessoryJson.Value = new AccessoryState(...)` becomes `RequestAccessoryUpdateServerRpc(...)`:

- `LoadAccessoriesFromBackend` (~line 96): `RequestAccessoryUpdateServerRpc(loadout.BuildAccessoryJson());`
- `UpdateEquippedAccessories(int)` (~line 112): `RequestAccessoryUpdateServerRpc(loadout.BuildAccessoryJson());`
- `UpdateEquippedAccessories(List<int>)` (~line 133): `RequestAccessoryUpdateServerRpc(loadout.BuildAccessoryJson());`
- `UnequipAccessory` (~line 156): `RequestAccessoryUpdateServerRpc(loadout.BuildAccessoryJson());`
- `ClearAllEquipped` (~line 176): `RequestAccessoryUpdateServerRpc("");`

- [ ] **Step 5: Verify compilation**

Use `refresh_unity` then `read_console` — zero errors expected.

---

### Task 7: Player Movement Networking (#16)

**Files:**
- Modify: `/Volumes/Samsung T7/Cloudy/MultiplayerLiveOpsToolkit/Assets/Scripts/Example/SimplePlayerMovement.cs`
- Modify: `/Volumes/Samsung T7/Cloudy/MultiplayerLiveOpsToolkit/Assets/Editor/SetupExampleScene.cs`

**Interfaces:**
- Consumes: `Unity.Netcode.NetworkBehaviour`, `Unity.Netcode.Components.NetworkTransform`
- Produces: Server-synced player movement; only owner processes input

- [ ] **Step 1: Convert SimplePlayerMovement to NetworkBehaviour**

Replace the full file:

```csharp
using UnityEngine;
using Unity.Netcode;

public class SimplePlayerMovement : NetworkBehaviour
{
    private float walkSpeed = 3f;
    private float runSpeed = 6f;
    private Animator animator;
    private static readonly int SpeedParam = Animator.StringToHash("Speed");

    void Start()
    {
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        if (!IsOwner) return;

        var cam = Camera.main;
        if (cam == null) return;

        float forward = 0f;
        float strafe = 0f;

        if (Input.GetKey(KeyCode.W)) forward += 1f;
        if (Input.GetKey(KeyCode.S)) forward -= 1f;
        if (Input.GetKey(KeyCode.A)) strafe -= 1f;
        if (Input.GetKey(KeyCode.D)) strafe += 1f;

        Vector3 camForward = cam.transform.forward;
        Vector3 camRight = cam.transform.right;
        camForward.y = 0;
        camRight.y = 0;
        camForward.Normalize();
        camRight.Normalize();

        Vector3 input = camForward * forward + camRight * strafe;
        float inputMagnitude = Mathf.Clamp01(input.magnitude);

        bool running = Input.GetKey(KeyCode.LeftShift);
        float currentSpeed = running ? runSpeed : walkSpeed;
        float animSpeed = running ? 1f : inputMagnitude * 0.5f;

        if (inputMagnitude > 0.1f)
        {
            Vector3 move = input.normalized;
            transform.position += move * currentSpeed * Time.deltaTime;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(move), 10f * Time.deltaTime);
        }

        if (animator != null)
            animator.SetFloat(SpeedParam, inputMagnitude > 0.1f ? animSpeed : 0f);
    }
}
```

- [ ] **Step 2: Add NetworkTransform + SimplePlayerMovement to player prefab**

In `SetupExampleScene.cs`, in `CreatePlayerPrefab()`, after line `root.AddComponent<PlayerAvatarSetup>();` (~line 281), add:

```csharp
root.AddComponent<Unity.Netcode.Components.NetworkTransform>();
root.AddComponent<SimplePlayerMovement>();
```

- [ ] **Step 3: Verify compilation**

Use `refresh_unity` then `read_console` — zero errors expected.

---

### Task 8: Tier 3 Cleanup (#6, #8, #12, #17)

**Files:**
- Modify: `/Volumes/Samsung T7/Cloudy/MultiplayerLiveOpsToolkit/Assets/Scripts/API/APIConfig.cs` (#8)
- Modify: `/Volumes/Samsung T7/Cloudy/MultiplayerLiveOpsToolkit/Assets/Scripts/API/TokenAPI.cs` (#6)
- Modify: `/Volumes/Samsung T7/Cloudy/MultiplayerLiveOpsToolkit/Assets/Scripts/API/ShopAPI.cs` (#8 const→static)
- Modify: `/Volumes/Samsung T7/Cloudy/MultiplayerLiveOpsToolkit/Assets/Scripts/API/WalletAPI.cs` (#8 const→static)
- Modify: `/Volumes/Samsung T7/Cloudy/MultiplayerLiveOpsToolkit/Assets/Scripts/API/AchievementAPI.cs` (#8 const→static)
- Modify: `/Volumes/Samsung T7/Cloudy/MultiplayerLiveOpsToolkit/Assets/Scripts/API/DailyLoginAPI.cs` (#8 const→static)
- Modify: `/Volumes/Samsung T7/Cloudy/MultiplayerLiveOpsToolkit/Assets/Scripts/Registry/AchievementService.cs` (#12)

**Interfaces:**
- Consumes: `ApiConfig`, `PlayerPrefs`, `AchievementService`
- Produces: Configurable HTTPS URL, encoded token storage, immediate flush on unlock

- [ ] **Step 1: (#8) Make APIConfig runtime-configurable**

```csharp
public static class ApiConfig
{
    public static string BASE_URL =
        UnityEngine.Application.isEditor ? "http://localhost:8000" : "https://localhost:8000";
}
```

Then change all `private const string` URL fields in API classes to `private static readonly string`.

- [ ] **Step 2: (#6) Encode tokens at rest**

In `TokenAPI.cs`, replace `SetTokens` and `GetAccessToken`:

```csharp
public void SetTokens(string accessToken, string refreshToken)
{
    PlayerPrefs.SetString(ACCESS_TOKEN_KEY, System.Convert.ToBase64String(
        System.Text.Encoding.UTF8.GetBytes(accessToken)));
    PlayerPrefs.SetString(REFRESH_TOKEN_KEY, System.Convert.ToBase64String(
        System.Text.Encoding.UTF8.GetBytes(refreshToken)));
    PlayerPrefs.Save();
}

public string GetAccessToken()
{
    string stored = PlayerPrefs.GetString(ACCESS_TOKEN_KEY, "");
    if (string.IsNullOrEmpty(stored)) return "";
    try { return System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(stored)); }
    catch { return stored; }
}
```

Apply same decode in `RefreshToken` method for `REFRESH_TOKEN_KEY`.

- [ ] **Step 3: (#12) Clear dirty progress on unlock**

In `AchievementService.cs` `ReportEvent`, add `dirtyProgressIds.Remove(achievementId);` before the existing `authority.RequestUnlock` call inside the `foreach (string achievementId in newlyUnlocked)` loop.

- [ ] **Step 4: (#17) Wrap informational Debug.Log in conditional compilation**

Wrap `Debug.Log(...)` (not `LogError`/`LogWarning`) in `#if UNITY_EDITOR` across API and registry files. Key targets:
- `PlayerAccessory.cs:193` — `Debug.Log(message)` in StoreEquippedAccessories callback
- `AccessoryRegistry.cs` — catalog load success logs
- `ShopAPI.cs` — success response logs

- [ ] **Step 5: Verify compilation**

Use `refresh_unity` then `read_console` — zero errors expected.

---

## Task Dependency Graph

```
Tasks 1-7: all independent, run in parallel
    │
    └──▶ Task 8: depends on Task 6 (shares PlayerAccessory.cs, API URL constants)
```

## Task Summary

| Task | Issue(s) | Scope | Est. |
|------|----------|-------|------|
| 1 | #1 | Backend: server-side achievement validation | Medium |
| 2 | #3, #11 | Backend: atomic equip + slot uniqueness | Small |
| 3 | #5, #13 | Backend: race condition fixes | Small |
| 4 | #4 | Backend: rate limiting | Small |
| 5 | #14 | Backend: pagination + input validation | Small |
| 6 | #9 | Client: ServerRpc-gated NetworkVariable | Medium |
| 7 | #16 | Client: NetworkTransform for movement | Small |
| 8 | #6, #8, #12, #17 | Client: cleanup bundle | Medium |
