"""Confirmed cosmetic ownership read model shared by Inventory and Shop."""
from shop.models import AccessorySet, UserAccessory


def snapshot(profile):
    owned = UserAccessory.objects.filter(player=profile).select_related('accessory').order_by('accessory_id')
    items = [{'item_id': str(entry.accessory_id), 'display_name': entry.accessory.name,
              'category_id': entry.accessory.type} for entry in owned]
    owned_ids = {item['item_id'] for item in items}
    sets = []
    for accessory_set in AccessorySet.objects.prefetch_related('accessories').order_by('id'):
        member_ids = sorted((str(item.id) for item in accessory_set.accessories.all()), key=int)
        if member_ids and all(item_id in owned_ids for item_id in member_ids):
            sets.append({'set_id': str(accessory_set.id), 'display_name': accessory_set.name, 'item_ids': member_ids})
    return {'items': items, 'sets': sets}
