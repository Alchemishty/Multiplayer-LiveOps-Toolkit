from django.core.exceptions import ImproperlyConfigured
from django.db import transaction
from rest_framework.decorators import api_view, permission_classes, throttle_classes
from rest_framework.permissions import IsAuthenticated
from rest_framework.response import Response
from shop.models import PlayerProfile
from shop.services import equipment
from shop.services.identity_proofs import IdentityProofIssuer, validate_challenge_context
from shop.throttles import EconomyMutationThrottle, IdentityProofThrottle


def _response(data, status=200):
    response = Response(data, status=status)
    response['Cache-Control'] = 'no-store'
    return response


@api_view(['GET', 'POST'])
@permission_classes([IsAuthenticated])
@throttle_classes([EconomyMutationThrottle])
def loadout(request):
    profile = PlayerProfile.objects.filter(user=request.user).first()
    if profile is None:
        return _response({'code': 'identity_not_initialized'}, 409)
    try:
        if request.method == 'GET':
            return _response(equipment.snapshot(profile))
        try:
            item_ids, revision = equipment.validate_request(request.data)
        except ValueError:
            return _response({'code': 'invalid_request'}, 400)
        result, status = equipment.replace(profile, item_ids, revision)
        return _response(result, status)
    except ValueError:
        return _response({'code': 'equipment_configuration'}, 409)


@api_view(['POST'])
@permission_classes([IsAuthenticated])
@throttle_classes([IdentityProofThrottle])
def proof(request):
    if not isinstance(request.data, dict) or set(request.data) != {'session_id', 'challenge'}:
        return _response({'code': 'invalid_challenge'}, 400)
    try:
        validate_challenge_context(request.data['session_id'], request.data['challenge'])
    except ValueError:
        return _response({'code': 'invalid_challenge'}, 400)
    profile = PlayerProfile.objects.filter(user=request.user).first()
    if profile is None:
        return _response({'code': 'identity_not_initialized'}, 409)
    try:
        issuer = IdentityProofIssuer.from_settings()
        with transaction.atomic():
            state = equipment.snapshot(profile)['loadout']
            assertion = issuer.issue_equipment(str(profile.pk), request.user.username,
                                                request.data['session_id'], request.data['challenge'], state)
        return _response({'assertion': assertion})
    except ImproperlyConfigured:
        return _response({'code': 'proof_configuration'}, 503)
    except ValueError:
        return _response({'code': 'equipment_configuration'}, 409)
