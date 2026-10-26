from rest_framework.decorators import api_view, permission_classes, throttle_classes
from rest_framework.response import Response
from rest_framework.permissions import IsAuthenticated
from django.db import transaction

from shop.models import PlayerProfile
from shop.services import wallet as wallet_service
from shop.throttles import EconomyMutationThrottle

def _wallet_profile(request):
    return PlayerProfile.objects.filter(user=request.user).first()


@api_view(["GET"])
@permission_classes([IsAuthenticated])
def get_wallet(request):
    profile = _wallet_profile(request)
    if profile is None:
        return Response({'code': 'identity_not_initialized'}, status=409)
    try:
        result = wallet_service.snapshot(profile)
    except ValueError:
        return Response({'code': 'invalid_wallet_configuration'}, status=409)
    response = Response(result)
    response['Cache-Control'] = 'no-store'
    return response


@api_view(["POST"])
@permission_classes([IsAuthenticated])
@throttle_classes([EconomyMutationThrottle])
def convert_currency(request):
    profile = _wallet_profile(request)
    if profile is None:
        return Response({'code': 'identity_not_initialized'}, status=409)
    try:
        offer_id, amount = wallet_service.validate_request(request.data)
    except ValueError as error:
        return Response({'code': 'invalid_request', 'message': str(error)}, status=400)
    try:
        result, response_status = wallet_service.convert(profile, offer_id, amount)
    except ValueError:
        return Response({'code': 'invalid_wallet_configuration'}, status=409)
    response = Response(result, status=response_status)
    response['Cache-Control'] = 'no-store'
    return response
