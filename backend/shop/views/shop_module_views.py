from rest_framework.decorators import api_view, permission_classes, throttle_classes
from rest_framework.permissions import IsAuthenticated
from rest_framework.response import Response
from shop.models import PlayerProfile
from shop.services import shop
from shop.throttles import EconomyMutationThrottle


def _response(data, status=200):
    response = Response(data, status=status)
    response['Cache-Control'] = 'no-store'
    return response


@api_view(['GET'])
@permission_classes([IsAuthenticated])
def catalog(request):
    profile = PlayerProfile.objects.filter(user=request.user).first()
    if profile is None:
        return _response({'code': 'identity_not_initialized'}, 409)
    try:
        return _response(shop.snapshot(profile))
    except ValueError:
        return _response({'code': 'invalid_shop_configuration'}, 409)


@api_view(['POST'])
@permission_classes([IsAuthenticated])
@throttle_classes([EconomyMutationThrottle])
def purchase(request):
    profile = PlayerProfile.objects.filter(user=request.user).first()
    if profile is None:
        return _response({'code': 'identity_not_initialized'}, 409)
    try:
        offer_id = shop.validate_request(request.data)
    except ValueError:
        return _response({'code': 'invalid_request'}, 400)
    try:
        result, response_status = shop.purchase(profile, offer_id)
        return _response(result, response_status)
    except ValueError:
        return _response({'code': 'invalid_shop_configuration'}, 409)
