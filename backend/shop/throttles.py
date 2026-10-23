from rest_framework.throttling import UserRateThrottle


class EconomyMutationThrottle(UserRateThrottle):
    scope = "economy_mutation"


class ClaimThrottle(UserRateThrottle):
    scope = "claim"
