from rest_framework.decorators import api_view, permission_classes, throttle_classes
from rest_framework.response import Response
from rest_framework.permissions import IsAuthenticated
from django.utils import timezone
from django.db import transaction, IntegrityError
import calendar as cal
from datetime import date
from shop.models import (
    Achievement, UserAchievement,
    MonthlyRewardSchedule, DailyAttendance, Currency, Wallet,
    UserAccessory, Accessory, AccessoryCost,
)
from shop.serializers import UserAchievementSerializer
from shop.throttles import ClaimThrottle


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


@api_view(["GET"])
@permission_classes([IsAuthenticated])
def get_daily_login_calendar(request):
    """
    Monthly calendar with reward schedule and claim status per day.

    GET /api/daily-login/calendar/
    Optional query params: ?year=2026&month=10
    Response: {"year": 2026, "month": 10, "today": 17,
               "days": [{"day": 1, "reward": {"currency": "gold", "amount": 50}, "is_claimed": true}, ...]}

    curl http://localhost:8000/api/daily-login/calendar/ \\
      -H "Authorization: Bearer <token>"
    """
    profile = request.user.player_profile
    today = date.today()
    try:
        year = int(request.query_params.get("year", today.year))
        month = int(request.query_params.get("month", today.month))
    except (ValueError, TypeError):
        return Response({"message": "Invalid year or month"}, status=400)

    if month < 1 or month > 12 or year < 2020 or year > 2100:
        return Response({"message": "Year/month out of range"}, status=400)

    _ensure_monthly_rewards(year, month)

    rewards = {
        r.day: r
        for r in MonthlyRewardSchedule.objects.filter(year=year, month=month)
            .select_related("currency")
    }

    claimed_dates = set(
        DailyAttendance.objects.filter(
            player=profile,
            date__year=year,
            date__month=month,
        ).values_list("date__day", flat=True)
    )

    days_in_month = cal.monthrange(year, month)[1]
    days = []
    for day in range(1, days_in_month + 1):
        reward = rewards.get(day)
        days.append({
            "day": day,
            "reward": {
                "currency": reward.currency.name,
                "amount": reward.reward_amount,
            } if reward else None,
            "is_claimed": day in claimed_dates,
        })

    return Response({
        "year": year,
        "month": month,
        "today": today.day if year == today.year and month == today.month else 0,
        "days": days,
    })


@api_view(["POST"])
@permission_classes([IsAuthenticated])
@throttle_classes([ClaimThrottle])
def claim_daily_login(request):
    """
    Claim today's daily login reward. Creates an attendance record and
    grants the configured currency reward. Returns 400 if already claimed.

    POST /api/daily-login/claim/
    Success 200: {"day": 17, "reward": {"currency": "gold", "amount": 50}, "message": "Claimed 50 gold!"}
    Error 400: {"message": "Already claimed today"}

    curl -X POST http://localhost:8000/api/daily-login/claim/ \\
      -H "Authorization: Bearer <token>"
    """
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


@api_view(["GET"])
@permission_classes([IsAuthenticated])
def get_achievements(request):
    """
    Get the current user's unlocked achievements (legacy endpoint).

    GET /api/achievements/
    """
    profile = request.user.player_profile
    unlocked = UserAchievement.objects.filter(player=profile).select_related(
        "achievement"
    )
    serializer = UserAchievementSerializer(unlocked, many=True)
    return Response(serializer.data)


@api_view(["GET"])
@permission_classes([IsAuthenticated])
def list_achievement_definitions(request):
    """
    List all active achievement definitions.

    GET /api/achievements/definitions/
    Response: {"achievements": [{"id": "first-purchase", "name": "Window Shopper",
               "description": "...", "condition_type": "one_time_trigger",
               "trigger_event": "purchase", "target_count": 0,
               "target_slot": "", "target_currency": ""}]}
    """
    achievements = Achievement.objects.filter(is_active=True)

    response_data = {"achievements": []}
    for achievement in achievements:
        response_data["achievements"].append({
            "id": achievement.key,
            "name": achievement.title,
            "description": achievement.description,
            "condition_type": achievement.condition_type,
            "trigger_event": achievement.trigger_event,
            "target_count": achievement.target_count,
            "target_slot": achievement.target_slot if achievement.target_slot != "none" else "",
            "target_currency": achievement.target_currency,
        })

    return Response(response_data)


@api_view(["GET"])
@permission_classes([IsAuthenticated])
def get_achievement_progress(request):
    """
    Get the current user's achievement progress for all achievements.

    GET /api/achievements/progress/
    Response: {"progress": [{"achievement_id": "first-purchase", "current_count": 0,
               "is_unlocked": false, "unlocked_at": 0}]}
    """
    profile = request.user.player_profile
    achievements = Achievement.objects.filter(is_active=True)

    progress_list = []
    for achievement in achievements:
        try:
            user_achievement = UserAchievement.objects.get(
                player=profile, achievement=achievement
            )
            unlocked_at_ms = (
                int(user_achievement.unlocked_at.timestamp() * 1000)
                if user_achievement.unlocked_at else 0
            )
            progress_list.append({
                "achievement_id": achievement.key,
                "current_count": user_achievement.current_count,
                "is_unlocked": user_achievement.is_unlocked,
                "unlocked_at": unlocked_at_ms,
            })
        except UserAchievement.DoesNotExist:
            progress_list.append({
                "achievement_id": achievement.key,
                "current_count": 0,
                "is_unlocked": False,
                "unlocked_at": 0,
            })

    return Response({"progress": progress_list})


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
        for accessory_set in AccessorySet.objects.prefetch_related("accessories"):
            accessory_ids = set(accessory_set.accessories.values_list("id", flat=True))
            owned_ids = set(
                UserAccessory.objects.filter(
                    player=profile, accessory_id__in=accessory_ids
                ).values_list("accessory_id", flat=True)
            )
            if accessory_ids and accessory_ids == owned_ids:
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
        for accessory_set in AccessorySet.objects.prefetch_related("accessories"):
            accessory_ids = set(accessory_set.accessories.values_list("id", flat=True))
            owned_ids = set(
                UserAccessory.objects.filter(
                    player=profile, accessory_id__in=accessory_ids
                ).values_list("accessory_id", flat=True)
            )
            if accessory_ids and accessory_ids == owned_ids:
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


@api_view(["POST"])
@permission_classes([IsAuthenticated])
@throttle_classes([ClaimThrottle])
def report_achievement_unlock(request):
    """
    Report that a client has unlocked an achievement.

    POST /api/achievements/unlock/
    Body: {"achievement_id": "first-purchase", "current_count": 1}
    Success 200: {"success": true}
    Error 400: {"message": "..."}
    """
    profile = request.user.player_profile
    achievement_id = request.data.get("achievement_id")
    current_count = request.data.get("current_count", 0)

    if not achievement_id:
        return Response({"message": "achievement_id is required"}, status=400)

    try:
        achievement = Achievement.objects.get(key=achievement_id, is_active=True)
    except Achievement.DoesNotExist:
        return Response({"message": f"Achievement '{achievement_id}' not found"}, status=400)

    with transaction.atomic():
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


@api_view(["POST"])
@permission_classes([IsAuthenticated])
def report_achievement_progress(request):
    """
    Update incremental progress for an achievement.

    POST /api/achievements/progress/update/
    Body: {"achievement_id": "buy-5", "current_count": 3}
    Success 200: {"success": true}
    Error 400: {"message": "..."}
    """
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
