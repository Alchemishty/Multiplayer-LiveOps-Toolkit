from django.core.management.base import BaseCommand
from shop.models import (
    Currency, Accessory, AccessoryCost, Achievement,
    AccessorySet, AccessorySetCost,
)


CURRENCIES = ["gold", "snowflakes", "gears"]

# (id, name, type, [(currency_name, amount), ...])
# IDs must match AccessoryDef IDs in SetupExampleScene.cs.
# Types use AccessoryType enum names (lowercase).
# Additive accessories only — body part sets added later.
ACCESSORIES = [
    # Headwear (attached to Head bone)
    (1,  "Medieval Hat",   "headwear", [("gold", 25)]),
    (6,  "Ranger Hat",     "headwear", [("gold", 60), ("snowflakes", 5)]),
    (12, "Modern Hat",     "headwear", [("gold", 150), ("snowflakes", 15), ("gears", 3)]),
    (17, "Viking Helmet",  "headwear", [("gold", 200), ("snowflakes", 20), ("gears", 5)]),
    # Hair (attached to Head bone)
    (18, "Punk Hair",      "hair", [("gold", 80), ("snowflakes", 8)]),
    (19, "Cyber Hair",     "hair", [("gold", 120), ("snowflakes", 12), ("gears", 2)]),
    # Facewear (attached to Head bone)
    (7,  "Cyber Glasses",  "facewear", [("gold", 50), ("snowflakes", 4)]),
    (3,  "Cyber Mask",     "facewear", [("gold", 20)]),
    # MainHand weapons (attached to RightHand bone)
    (20, "Iron Sword",     "mainhand", [("gold", 90), ("snowflakes", 10)]),
    (21, "Battle Axe",     "mainhand", [("gold", 110), ("snowflakes", 12), ("gears", 2)]),
    (22, "Wizard Staff",   "mainhand", [("gold", 160), ("snowflakes", 18), ("gears", 4)]),
    (23, "Laser Gun",      "mainhand", [("gold", 250), ("snowflakes", 25), ("gears", 6)]),
    # OffHand (attached to LeftHand bone)
    (24, "Wooden Shield",  "offhand", [("gold", 70), ("snowflakes", 6)]),
    (25, "Hunter Bow",     "offhand", [("gold", 100), ("snowflakes", 10), ("gears", 2)]),
    # Back (attached to Spine2 bone)
    (26, "Adventure Pack", "back", [("gold", 60), ("snowflakes", 5)]),
    (27, "Shadow Cloak",   "back", [("gold", 180), ("snowflakes", 20), ("gears", 4)]),
]

# (key, title, description, condition_type, trigger_event, target_count, target_slot, target_currency)
ACHIEVEMENTS = [
    # One-Time Triggers
    ("first-purchase", "Window Shopper", "Make your first accessory purchase",
     "one_time_trigger", "purchase", 0, "none", ""),
    ("first-set", "Bundle of Joy", "Purchase your first accessory set",
     "one_time_trigger", "set_purchase", 0, "none", ""),
    ("first-login", "Welcome Aboard", "Claim your first daily login reward",
     "one_time_trigger", "login", 0, "none", ""),
    # Threshold Counters - Purchase
    ("buy-5", "Regular Customer", "Purchase 5 accessories",
     "threshold_counter", "purchase", 5, "none", ""),
    ("buy-15", "Shopaholic", "Purchase 15 accessories",
     "threshold_counter", "purchase", 15, "none", ""),
    # Threshold Counters - SetPurchase
    ("buy-sets-3", "Set Collector", "Purchase 3 accessory sets",
     "threshold_counter", "set_purchase", 3, "none", ""),
    # Threshold Counters - Login
    ("login-7", "Dedicated Player", "Log in 7 times",
     "threshold_counter", "login", 7, "none", ""),
    ("login-30", "Loyal Citizen", "Log in 30 times",
     "threshold_counter", "login", 30, "none", ""),
    # Threshold Counters - CurrencySpent
    ("spend-gold-500", "Gold Digger", "Spend 500 gold total",
     "threshold_counter", "currency_spent", 500, "none", "gold"),
    ("spend-gold-5000", "Big Spender", "Spend 5000 gold total",
     "threshold_counter", "currency_spent", 5000, "none", "gold"),
    ("spend-gems-100", "Gear Enthusiast", "Spend 100 gears total",
     "threshold_counter", "currency_spent", 100, "none", "gears"),
    # Collection Completion
    ("all-headwear", "Hat Trick", "Own every headwear accessory",
     "collection_completion", "purchase", 0, "headwear", ""),
    ("all-facewear", "Face Value", "Own every facewear accessory",
     "collection_completion", "purchase", 0, "facewear", ""),
    ("all-mainhand", "Armed and Ready", "Own every main hand accessory",
     "collection_completion", "purchase", 0, "mainhand", ""),
]

# Body-part accessories sold through sets only
# (id, name, type)
BODY_PART_ACCESSORIES = [
    # Ninja
    (28, "Ninja Head",       "head"),
    (29, "Ninja Tunic",      "torso"),
    (30, "Ninja Arms",       "arms"),
    (31, "Ninja Trousers",   "legs"),
    (32, "Ninja Boots",      "feet"),
    # Mage
    (33, "Mage Head",        "head"),
    (34, "Mage Tunic",       "torso"),
    (35, "Mage Arms",        "arms"),
    (36, "Mage Trousers",    "legs"),
    (37, "Mage Boots",       "feet"),
    # Sheriff
    (38, "Sheriff Head",     "head"),
    (39, "Sheriff Tunic",    "torso"),
    (40, "Sheriff Arms",     "arms"),
    (41, "Sheriff Trousers", "legs"),
    (42, "Sheriff Boots",    "feet"),
    # Steampunk
    (43, "Steampunk Head",   "head"),
    (44, "Steampunk Tunic",  "torso"),
    (45, "Steampunk Arms",   "arms"),
    (46, "Steampunk Trousers", "legs"),
    (47, "Steampunk Boots",  "feet"),
    # Commando
    (48, "Commando Head",    "head"),
    (49, "Commando Tunic",   "torso"),
    (50, "Commando Arms",    "arms"),
    (51, "Commando Trousers", "legs"),
    (52, "Commando Boots",   "feet"),
    # Cyber
    (53, "Cyber Head",       "head"),
    (54, "Cyber Tunic",      "torso"),
    (55, "Cyber Arms",       "arms"),
    (56, "Cyber Trousers",   "legs"),
    (57, "Cyber Boots",      "feet"),
]

# (set_id, name, category, accessory_ids, [(currency, amount)])
ACCESSORY_SETS = [
    (4, "Ninja Outfit",     "outfits", [28, 29, 30, 31, 32], [("gold", 100), ("snowflakes", 10)]),
    (5, "Mage Outfit",      "outfits", [33, 34, 35, 36, 37], [("gold", 200), ("snowflakes", 20)]),
    (6, "Sheriff Outfit",   "outfits", [38, 39, 40, 41, 42], [("gold", 300), ("snowflakes", 30), ("gears", 5)]),
    (7, "Steampunk Outfit", "outfits", [43, 44, 45, 46, 47], [("gold", 400), ("snowflakes", 40), ("gears", 8)]),
    (8, "Commando Outfit",  "outfits", [48, 49, 50, 51, 52], [("gold", 550), ("snowflakes", 55), ("gears", 12)]),
    (9, "Cyber Outfit",     "outfits", [53, 54, 55, 56, 57], [("gold", 700), ("snowflakes", 70), ("gears", 16)]),
]

class Command(BaseCommand):
    help = "Seed the database with currencies, accessories, sets, and achievements."

    def handle(self, *args, **options):
        currencies = {}
        for name in CURRENCIES:
            obj, created = Currency.objects.get_or_create(name=name)
            currencies[name] = obj
            self._log(created, "Currency", name)

        for acc_id, acc_name, acc_type, costs in ACCESSORIES:
            acc, created = Accessory.objects.update_or_create(
                pk=acc_id,
                defaults={"name": acc_name, "type": acc_type},
            )
            self._log(created, "Accessory", f"{acc_name} ({acc_type}) [id={acc_id}]")

            acc.costs.all().delete()
            for currency_name, amount in costs:
                AccessoryCost.objects.create(
                    accessory=acc,
                    currency=currencies[currency_name],
                    amount=amount,
                )
                self.stdout.write(f"  + Cost: {amount} {currency_name}")

        body_part_accessories = {}
        for acc_id, acc_name, acc_type in BODY_PART_ACCESSORIES:
            acc, created = Accessory.objects.update_or_create(
                pk=acc_id,
                defaults={"name": acc_name, "type": acc_type},
            )
            body_part_accessories[acc_id] = acc
            self._log(created, "Accessory", f"{acc_name} ({acc_type}) [id={acc_id}]")

        for set_id, set_name, category, acc_ids, costs in ACCESSORY_SETS:
            acc_set, created = AccessorySet.objects.update_or_create(
                pk=set_id,
                defaults={"name": set_name, "category": category},
            )
            acc_set.accessories.set([body_part_accessories[aid] for aid in acc_ids])
            acc_set.costs.all().delete()
            for currency_name, amount in costs:
                AccessorySetCost.objects.create(
                    accessory_set=acc_set,
                    currency=currencies[currency_name],
                    amount=amount,
                )
            self._log(created, "AccessorySet", f"{set_name} ({len(acc_ids)} items)")

        for (key, title, desc, cond_type, trigger, target_count,
             target_slot, target_currency) in ACHIEVEMENTS:
            _, created = Achievement.objects.update_or_create(
                key=key,
                defaults={
                    "title": title,
                    "description": desc,
                    "condition_type": cond_type,
                    "trigger_event": trigger,
                    "target_count": target_count,
                    "target_slot": target_slot,
                    "target_currency": target_currency,
                    "is_active": True,
                },
            )
            self._log(created, "Achievement", f"{title} ({key})")

        self.stdout.write(self.style.SUCCESS("\nSeed complete."))

    def _log(self, created, model, label):
        prefix = self.style.SUCCESS("Created") if created else self.style.WARNING("Updated")
        self.stdout.write(f"  {prefix} {model}: {label}")
