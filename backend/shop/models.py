from django.conf import settings
from django.db import models
from datetime import date
from django.core.exceptions import ValidationError

ACCESSORY_TYPES = [
    ("headwear", "Headwear"),
    ("hair", "Hair"),
    ("facewear", "Facewear"),
    ("mainhand", "Main Hand"),
    ("offhand", "Off Hand"),
    ("back", "Back"),
    ("head", "Head"),
    ("torso", "Torso"),
    ("arms", "Arms"),
    ("legs", "Legs"),
    ("feet", "Feet"),
]


class PlayerProfile(models.Model):
    """Links a Django User to the game economy. Created on first login."""
    user = models.OneToOneField(
        settings.AUTH_USER_MODEL,
        on_delete=models.CASCADE,
        related_name="player_profile",
    )
    platform_id = models.CharField(max_length=255, unique=True, blank=True, default="")

    def __str__(self):
        return self.user.username


class Currency(models.Model):
    """A currency type in the economy (e.g., gold, snowflakes, gears)."""
    name = models.CharField(max_length=50, unique=True)

    def __str__(self):
        return self.name

    class Meta:
        verbose_name_plural = "Currencies"
        ordering = ["name"]


class Wallet(models.Model):
    """Per-player balance for a single currency. One wallet per currency per player."""
    player = models.ForeignKey(PlayerProfile, on_delete=models.CASCADE)
    currency = models.ForeignKey(Currency, on_delete=models.CASCADE)
    amount = models.IntegerField(default=0)

    class Meta:
        unique_together = ("player", "currency")


class Accessory(models.Model):
    """A cosmetic item available in the shop (e.g., hat, skin, necklace)."""
    name = models.CharField(max_length=100)
    type = models.CharField(max_length=50, choices=ACCESSORY_TYPES)

    class Meta:
        verbose_name_plural = "Accessories"

    def __str__(self):
        return self.name


class AccessoryCost(models.Model):
    """Multi-currency pricing: an accessory can cost multiple currencies."""
    accessory = models.ForeignKey(
        Accessory, on_delete=models.CASCADE, related_name="costs"
    )
    currency = models.ForeignKey(Currency, on_delete=models.CASCADE)
    amount = models.IntegerField()

    class Meta:
        unique_together = ("accessory", "currency")


class UserAccessory(models.Model):
    """Tracks ownership and equip state. One record per player per accessory."""
    player = models.ForeignKey(PlayerProfile, on_delete=models.CASCADE)
    accessory = models.ForeignKey(Accessory, on_delete=models.CASCADE)
    acquired_at = models.DateTimeField(auto_now_add=True)
    is_equipped = models.BooleanField(default=False)

    class Meta:
        unique_together = ("player", "accessory")
        verbose_name_plural = "User Accessories"


class AccessorySet(models.Model):
    """A purchasable bundle of accessories with its own pricing."""
    name = models.CharField(max_length=100)
    category = models.CharField(max_length=50, default="bundles")
    accessories = models.ManyToManyField(Accessory, related_name="sets")

    def __str__(self):
        return self.name

    class Meta:
        verbose_name_plural = "Accessory Sets"


class AccessorySetCost(models.Model):
    """Multi-currency pricing for a set."""
    accessory_set = models.ForeignKey(
        AccessorySet, on_delete=models.CASCADE, related_name="costs"
    )
    currency = models.ForeignKey(Currency, on_delete=models.CASCADE)
    amount = models.IntegerField()

    class Meta:
        unique_together = ("accessory_set", "currency")


class Achievement(models.Model):
    """Data-driven achievement definition with condition configuration."""
    CONDITION_TYPES = [
        ("threshold_counter", "Threshold Counter"),
        ("collection_completion", "Collection Completion"),
        ("one_time_trigger", "One-Time Trigger"),
    ]

    TRIGGER_EVENTS = [
        ("purchase", "Purchase"),
        ("set_purchase", "Set Purchase"),
        ("login", "Login"),
        ("currency_spent", "Currency Spent"),
    ]

    REWARD_TYPES = [
        ("currency", "Currency"),
        ("accessory", "Accessory"),
        ("none", "None"),
    ]

    key = models.CharField(max_length=100, unique=True)
    title = models.CharField(max_length=255)
    description = models.TextField()
    condition_type = models.CharField(
        max_length=30, choices=CONDITION_TYPES, default="one_time_trigger"
    )
    trigger_event = models.CharField(
        max_length=30, choices=TRIGGER_EVENTS, default="purchase"
    )
    target_count = models.IntegerField(default=0)
    target_slot = models.CharField(
        max_length=50, choices=ACCESSORY_TYPES + [("none", "None")],
        default="none", blank=True
    )
    target_currency = models.CharField(max_length=50, blank=True, default="")
    is_active = models.BooleanField(default=True)

    reward_type = models.CharField(
        max_length=20, choices=REWARD_TYPES, default="none"
    )
    currency_reward = models.ForeignKey(
        Currency,
        on_delete=models.SET_NULL,
        null=True,
        blank=True,
        related_name="achievements",
    )
    accessory_reward = models.ForeignKey(
        Accessory,
        on_delete=models.SET_NULL,
        null=True,
        blank=True,
        related_name="achievements",
    )
    reward_amount = models.IntegerField(default=0)

    def clean(self):
        if self.reward_type == "currency" and not self.currency_reward:
            raise ValidationError(
                "Currency reward is required for currency type achievements"
            )
        if self.reward_type == "accessory" and not self.accessory_reward:
            raise ValidationError(
                "Accessory reward is required for accessory type achievements"
            )

    def __str__(self):
        return self.title


class UserAchievement(models.Model):
    """Tracks per-player achievement progress and unlock state."""
    player = models.ForeignKey(PlayerProfile, on_delete=models.CASCADE)
    achievement = models.ForeignKey(Achievement, on_delete=models.CASCADE)
    current_count = models.IntegerField(default=0)
    is_unlocked = models.BooleanField(default=False)
    unlocked_at = models.DateTimeField(null=True, blank=True)

    class Meta:
        unique_together = ("player", "achievement")



class MonthlyRewardSchedule(models.Model):
    """Backend-controlled reward for a specific calendar day."""
    year = models.IntegerField()
    month = models.IntegerField()
    day = models.IntegerField()
    currency = models.ForeignKey(Currency, on_delete=models.CASCADE)
    reward_amount = models.IntegerField(default=50)

    class Meta:
        unique_together = ("year", "month", "day")
        ordering = ["year", "month", "day"]

    def __str__(self):
        return f"{self.year}-{self.month:02d}-{self.day:02d}: {self.reward_amount} {self.currency}"


class DailyAttendance(models.Model):
    """Tracks a single claimed day for a player."""
    player = models.ForeignKey(PlayerProfile, on_delete=models.CASCADE, related_name="daily_attendances")
    date = models.DateField()
    claimed_at = models.DateTimeField(auto_now_add=True)

    class Meta:
        unique_together = ("player", "date")
        ordering = ["-date"]

    def __str__(self):
        return f"{self.player} — {self.date}"
