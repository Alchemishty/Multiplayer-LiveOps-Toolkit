from pathlib import Path
from datetime import timedelta
import os

BASE_DIR = Path(__file__).resolve().parent.parent

SECRET_KEY = os.environ.get(
    "DJANGO_SECRET_KEY",
    "django-insecure-change-me-in-production",
)
DEBUG = os.environ.get("DEBUG", "True").lower() == "true"

ALLOWED_HOSTS = os.environ.get("ALLOWED_HOSTS", "127.0.0.1,localhost").split(",")

INSTALLED_APPS = [
    "django.contrib.admin",
    "django.contrib.auth",
    "django.contrib.contenttypes",
    "django.contrib.sessions",
    "django.contrib.messages",
    "django.contrib.staticfiles",
    "rest_framework",
    "rest_framework_simplejwt",
    "shop",
]

REST_FRAMEWORK = {
    "DEFAULT_AUTHENTICATION_CLASSES": (
        "rest_framework_simplejwt.authentication.JWTAuthentication",
    ),
    "DEFAULT_THROTTLE_RATES": {
        "economy_mutation": "30/min",
        "claim": "5/min",
        "password_login": "10/min",
        "identity_proof": "20/min",
    },
    "DEFAULT_PAGINATION_CLASS": "rest_framework.pagination.PageNumberPagination",
    "PAGE_SIZE": 50,
}

SIMPLE_JWT = {
    "ACCESS_TOKEN_LIFETIME": timedelta(minutes=15),
    "REFRESH_TOKEN_LIFETIME": timedelta(days=1),
    "USER_ID_FIELD": "id",
    "TOKEN_REFRESH_SERIALIZER": "shop.serializers.PlayerTokenRefreshSerializer",
}

MIDDLEWARE = [
    "django.middleware.security.SecurityMiddleware",
    "django.contrib.sessions.middleware.SessionMiddleware",
    "django.middleware.common.CommonMiddleware",
    "django.middleware.csrf.CsrfViewMiddleware",
    "django.contrib.auth.middleware.AuthenticationMiddleware",
    "django.contrib.messages.middleware.MessageMiddleware",
    "django.middleware.clickjacking.XFrameOptionsMiddleware",
]

ROOT_URLCONF = "shop_backend.urls"

TEMPLATES = [
    {
        "BACKEND": "django.template.backends.django.DjangoTemplates",
        "DIRS": [],
        "APP_DIRS": True,
        "OPTIONS": {
            "context_processors": [
                "django.template.context_processors.debug",
                "django.template.context_processors.request",
                "django.contrib.auth.context_processors.auth",
                "django.contrib.messages.context_processors.messages",
            ],
        },
    },
]

WSGI_APPLICATION = "shop_backend.wsgi.application"

DATABASES = {
    "default": {
        "ENGINE": "django.db.backends.sqlite3",
        "NAME": Path(os.environ.get("DB_PATH", BASE_DIR / "db.sqlite3")),
    }
}

AUTH_PASSWORD_VALIDATORS = [
    {"NAME": "django.contrib.auth.password_validation.UserAttributeSimilarityValidator"},
    {"NAME": "django.contrib.auth.password_validation.MinimumLengthValidator"},
    {"NAME": "django.contrib.auth.password_validation.CommonPasswordValidator"},
    {"NAME": "django.contrib.auth.password_validation.NumericPasswordValidator"},
]

LANGUAGE_CODE = "en-us"
TIME_ZONE = "UTC"
USE_I18N = True
USE_TZ = True

STATIC_URL = "static/"
DEFAULT_AUTO_FIELD = "django.db.models.BigAutoField"

CACHES = {
    "default": {
        "BACKEND": "django.core.cache.backends.locmem.LocMemCache",
    }
}

LOGGING = {
    "version": 1,
    "disable_existing_loggers": False,
    "handlers": {
        "console": {
            "class": "logging.StreamHandler",
            "formatter": "verbose",
        },
    },
    "formatters": {
        "verbose": {
            "format": "{levelname} {asctime} {module} {message}",
            "style": "{",
        },
    },
    "loggers": {
        "shop": {
            "handlers": ["console"],
            "level": "INFO",
            "propagate": True,
        },
    },
}


# Separate asymmetric proof key: never substitute the account JWT signing secret.
LIVEOPS_IDENTITY_PROOF = {
    "private_key_file": os.environ.get("LIVEOPS_PROOF_PRIVATE_KEY_FILE", ""),
    "key_id": os.environ.get("LIVEOPS_PROOF_KEY_ID", ""),
    "issuer": os.environ.get("LIVEOPS_PROOF_ISSUER", ""),
    "game_id": os.environ.get("LIVEOPS_GAME_ID", ""),
    "environment": os.environ.get("LIVEOPS_ENVIRONMENT", ""),
    "content_version": os.environ.get("LIVEOPS_CONTENT_VERSION", ""),
    "lifetime_seconds": os.environ.get("LIVEOPS_PROOF_LIFETIME_SECONDS", "60"),
}

# Sample cosmetic slots. Conflicts must be symmetric and reference configured slots.
LIVEOPS_EQUIPMENT_SLOTS = {
    'headwear': ['hair'], 'hair': ['headwear'], 'facewear': [],
    'mainhand': [], 'offhand': [], 'back': [], 'head': [],
    'torso': [], 'arms': [], 'legs': [], 'feet': [],
}
