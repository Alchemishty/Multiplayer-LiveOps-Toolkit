from shop.views import daily_reward_views
from django.urls import path
from django.contrib import admin
from shop.views import auth_views, wallet_views, identity_proof_views, inventory_views, shop_module_views, equipment_views, achievement_views
from rest_framework_simplejwt.views import TokenRefreshView

urlpatterns = [
    path("api/daily-rewards/state/", daily_reward_views.state),
    path("api/daily-rewards/claim/", daily_reward_views.claim),
    path("admin/", admin.site.urls),

    # Auth
    path("api/auth/password/", auth_views.password_authenticate),
    path("api/auth/identity-proof/", identity_proof_views.issue_identity_proof),
    path("api/auth/refresh/", TokenRefreshView.as_view()),

    # Wallet
    path("api/wallet/", wallet_views.get_wallet),
    path("api/wallet/convert/", wallet_views.convert_currency),

    path("api/shop/catalog/", shop_module_views.catalog),
    path("api/shop/purchase-offer/", shop_module_views.purchase),

    path("api/equipment/loadout/", equipment_views.loadout),
    path("api/equipment/proof/", equipment_views.proof),
    # Inventory & equipment
    path("api/inventory/", inventory_views.get_inventory),

    path("api/achievements/state/", achievement_views.state),
    path("api/achievements/claim/", achievement_views.claim),
]

urlpatterns.append(path("api/auth/dev/", auth_views.dev_authenticate))
urlpatterns.append(path("api/auth/reset/", auth_views.reset_account))
