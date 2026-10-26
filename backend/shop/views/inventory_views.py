"""Read-only reference cosmetic ownership endpoint, independent of shop prices."""
from rest_framework.decorators import api_view, permission_classes
from rest_framework.permissions import IsAuthenticated
from rest_framework.response import Response
from shop.models import PlayerProfile
from shop.services.inventory import snapshot


@api_view(['GET'])
@permission_classes([IsAuthenticated])
def get_inventory(request):
    profile = PlayerProfile.objects.filter(user=request.user).first()
    if profile is None:
        return Response({'code': 'identity_not_initialized'}, status=409)
    response = Response(snapshot(profile))
    response['Cache-Control'] = 'no-store'
    return response
