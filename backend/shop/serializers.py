from rest_framework import serializers
from .models import (
    PlayerProfile, Currency, Wallet, Achievement,
    UserAchievement, AccessoryCost, Accessory, UserAccessory,
)


class PlayerProfileSerializer(serializers.ModelSerializer):
    username = serializers.CharField(source="user.username", read_only=True)

    class Meta:
        model = PlayerProfile
        fields = ["id", "username", "platform_id"]


class CurrencySerializer(serializers.ModelSerializer):
    class Meta:
        model = Currency
        fields = ["id", "name"]


class WalletSerializer(serializers.ModelSerializer):
    currency = CurrencySerializer()

    class Meta:
        model = Wallet
        fields = ["currency", "amount"]


class AccessoryCostSerializer(serializers.ModelSerializer):
    currency = CurrencySerializer()

    class Meta:
        model = AccessoryCost
        fields = ["currency", "amount"]


class AccessorySerializer(serializers.ModelSerializer):
    cost = AccessoryCostSerializer(source="costs", many=True)

    class Meta:
        model = Accessory
        fields = ["id", "name", "type", "cost"]


class UserAccessorySerializer(serializers.ModelSerializer):
    accessory = AccessorySerializer()

    class Meta:
        model = UserAccessory
        fields = ["accessory", "acquired_at", "is_equipped"]


class AchievementSerializer(serializers.ModelSerializer):
    class Meta:
        model = Achievement
        fields = ["key", "title", "description", "reward_type", "reward_amount"]


class UserAchievementSerializer(serializers.ModelSerializer):
    achievement = AchievementSerializer()

    class Meta:
        model = UserAchievement
        fields = ["achievement", "unlocked_at"]
