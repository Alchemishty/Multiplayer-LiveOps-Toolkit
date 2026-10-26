from rest_framework.decorators import api_view, permission_classes, throttle_classes
from rest_framework.permissions import IsAuthenticated
from rest_framework.response import Response
from shop.models import PlayerProfile
from shop.services import daily_rewards
from shop.throttles import ClaimThrottle


def _response(body, status=200):
    result = Response(body, status=status)
    result['Cache-Control'] = 'no-store'
    return result


@api_view(['GET'])
@permission_classes([IsAuthenticated])
def state(request):
    try:
        return _response(daily_rewards.snapshot(request.user.player_profile))
    except PlayerProfile.DoesNotExist:
        return _response({'code': 'missing_identity'}, 409)
    except ValueError:
        return _response({'code': 'invalid_daily_reward_configuration'}, 409)


@api_view(['POST'])
@permission_classes([IsAuthenticated])
@throttle_classes([ClaimThrottle])
def claim(request):
    try:
        expected_date = daily_rewards.validate_request(request.data)
    except ValueError:
        return _response({'code': 'invalid_request'}, 400)
    try:
        body, status = daily_rewards.claim(request.user.player_profile, expected_date)
        return _response(body, status)
    except PlayerProfile.DoesNotExist:
        return _response({'code': 'missing_identity'}, 409)
    except ValueError:
        return _response({'code': 'invalid_daily_reward_configuration'}, 409)
