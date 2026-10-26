from django.core.exceptions import ImproperlyConfigured
from rest_framework.decorators import api_view, permission_classes, throttle_classes
from rest_framework.permissions import IsAuthenticated
from rest_framework.response import Response

from shop.models import PlayerProfile
from shop.services.identity_proofs import IdentityProofIssuer, validate_challenge_context
from shop.throttles import IdentityProofThrottle


@api_view(["POST"])
@permission_classes([IsAuthenticated])
@throttle_classes([IdentityProofThrottle])
def issue_identity_proof(request):
    if not request.user.is_active:
        return Response({"code": "authentication_rejected"}, status=401)
    if not isinstance(request.data, dict) or set(request.data) != {"session_id", "challenge"}:
        return Response({"code": "invalid_challenge", "message": "Only session_id and challenge are accepted."}, status=400)
    try:
        validate_challenge_context(request.data["session_id"], request.data["challenge"])
    except ValueError:
        return Response({"code": "invalid_challenge", "message": "A valid session and canonical 32-byte challenge are required."}, status=400)
    try:
        profile = PlayerProfile.objects.get(user=request.user)
    except PlayerProfile.DoesNotExist:
        return Response({"code": "identity_not_initialized"}, status=409)
    try:
        issuer = IdentityProofIssuer.from_settings()
    except ImproperlyConfigured:
        return Response({"code": "proof_configuration", "message": "Identity proof signing is not configured correctly."}, status=503)
    try:
        assertion = issuer.issue(str(profile.pk), request.user.username,
                                 request.data["session_id"], request.data["challenge"])
    except ValueError:
        return Response({"code": "invalid_public_identity"}, status=409)
    return Response({"assertion": assertion})
