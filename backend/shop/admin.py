from django.contrib import admin
from .models import (
    PlayerProfile, Currency, Wallet, Accessory, AccessoryCost,
    UserAccessory, AccessorySet, AccessorySetCost, Achievement, UserAchievement,
    MonthlyRewardSchedule, DailyAttendance,
)


@admin.register(PlayerProfile)
class PlayerProfileAdmin(admin.ModelAdmin):
    list_display = ["user", "platform_id"]


@admin.register(Currency)
class CurrencyAdmin(admin.ModelAdmin):
    list_display = ["name"]


@admin.register(Wallet)
class WalletAdmin(admin.ModelAdmin):
    list_display = ["player", "currency", "amount"]


class AccessoryCostInline(admin.TabularInline):
    model = AccessoryCost
    extra = 1


@admin.register(Accessory)
class AccessoryAdmin(admin.ModelAdmin):
    list_display = ["id", "name", "type"]
    inlines = [AccessoryCostInline]


@admin.register(UserAccessory)
class UserAccessoryAdmin(admin.ModelAdmin):
    list_display = ["player", "accessory", "acquired_at", "is_equipped"]


class AccessorySetCostInline(admin.TabularInline):
    model = AccessorySetCost
    extra = 1


@admin.register(AccessorySet)
class AccessorySetAdmin(admin.ModelAdmin):
    list_display = ["id", "name", "category"]
    filter_horizontal = ["accessories"]
    inlines = [AccessorySetCostInline]


@admin.register(Achievement)
class AchievementAdmin(admin.ModelAdmin):
    list_display = ["key", "title", "condition_type", "trigger_event", "target_count", "is_active"]
    list_filter = ["condition_type", "trigger_event", "is_active"]


@admin.register(UserAchievement)
class UserAchievementAdmin(admin.ModelAdmin):
    list_display = ["player", "achievement", "current_count", "is_unlocked", "unlocked_at"]
    list_filter = ["is_unlocked"]



@admin.register(MonthlyRewardSchedule)
class MonthlyRewardScheduleAdmin(admin.ModelAdmin):
    list_display = ["year", "month", "day", "currency", "reward_amount"]
    list_filter = ["year", "month", "currency"]
    ordering = ["year", "month", "day"]


@admin.register(DailyAttendance)
class DailyAttendanceAdmin(admin.ModelAdmin):
    list_display = ["player", "date", "claimed_at"]
    list_filter = ["date"]
