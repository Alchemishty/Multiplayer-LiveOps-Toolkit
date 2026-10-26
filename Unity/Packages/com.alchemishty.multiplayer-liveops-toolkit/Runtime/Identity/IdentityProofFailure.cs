namespace MultiplayerLiveOpsToolkit.Identity
{
    public enum IdentityProofFailure
    {
        None,
        Malformed,
        UntrustedKey,
        InvalidSignature,
        InvalidContext,
        NotYetValid,
        Expired,
        AlreadyConsumed
    }
}
