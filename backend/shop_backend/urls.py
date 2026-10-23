from django.conf import settings
from django.urls import path
from django.contrib import admin
from shop.views import auth_views, wallet_views, shop_views, rewards_views
from rest_framework_simplejwt.views import TokenRefreshView

urlpatterns = [
    path("admin/", admin.site.urls),

    # Auth
    path("api/auth/", auth_views.authenticate_user),
    path("api/auth/refresh/", TokenRefreshView.as_view()),

    # Wallet
    path("api/wallet/", wallet_views.get_wallet),
    path("api/shop/convert-currency/", wallet_views.convert_currency),
    path("api/get-session-reward/", wallet_views.session_reward),

    # Shop & accessories
    path("api/shop/accessories/", shop_views.list_accessories),
    path("api/shop/purchase/", shop_views.purchase_accessory),
    path("api/shop/sets/", shop_views.list_sets),
    path("api/shop/purchase-set/", shop_views.purchase_set),

    # Inventory & equipment
    path("api/accessories/", shop_views.user_accessories),
    path("api/accessories/update-equipped/", shop_views.update_equipped_accessories),
    path("api/accessories/get-equipped/", shop_views.get_equipped_accessories),

    # Daily login
    path("api/daily-login/calendar/", rewards_views.get_daily_login_calendar),
    path("api/daily-login/claim/", rewards_views.claim_daily_login),
    path("api/achievements/", rewards_views.get_achievements),

    # Achievements (data-driven system)
    path("api/achievements/definitions/", rewards_views.list_achievement_definitions),
    path("api/achievements/progress/", rewards_views.get_achievement_progress),
    path("api/achievements/unlock/", rewards_views.report_achievement_unlock),
    path("api/achievements/progress/update/", rewards_views.report_achievement_progress),
]

urlpatterns.append(path("api/auth/dev/", auth_views.dev_authenticate))
urlpatterns.append(path("api/auth/reset/", auth_views.reset_account))
