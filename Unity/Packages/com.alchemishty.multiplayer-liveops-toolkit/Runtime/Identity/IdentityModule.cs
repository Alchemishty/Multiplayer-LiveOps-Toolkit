using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Core;

namespace MultiplayerLiveOpsToolkit.Identity
{
    public sealed class IdentityModule : ILiveOpsModule
    {
        private readonly IIdentityProvider provider;
        private readonly IIdentityBackend backend;
        private PlayerIdentity currentPlayer;
        private bool hasStarted;
        private bool isStopped;

        public string Id => "identity";
        public IReadOnlyCollection<string> Dependencies => Array.Empty<string>();
        public PlayerIdentity CurrentPlayer => currentPlayer
            ?? throw new InvalidOperationException("Identity is not authenticated. Wait for the owning runtime to become Ready.");

        public IdentityModule(IIdentityProvider provider, IIdentityBackend backend)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        public IEnumerator Initialize()
        {
            if (hasStarted || isStopped)
                throw new InvalidOperationException("Identity module cannot be reused. Create a new module for each runtime.");
            hasStarted = true;
            return Authenticate();
        }

        public void Shutdown()
        {
            if (isStopped) return;
            isStopped = true;
            currentPlayer = null;
            backend.ClearSession();
        }

        private IEnumerator Authenticate()
        {
            if (isStopped) yield break;
            IdentityCredential credential = null;
            int credentialCount = 0;
            bool isAcceptingCredential = true;
            try
            {
                yield return provider.GetCredential(value =>
                {
                    // A callback arriving after cancellation belongs to an expired operation.
                    if (isStopped || !isAcceptingCredential) return;
                    credential = value;
                    credentialCount++;
                }) ?? throw new InvalidOperationException("Identity provider returned a null credential routine.");
            }
            finally { isAcceptingCredential = false; }
            if (isStopped) yield break;
            if (credentialCount != 1 || credential == null)
                throw new InvalidOperationException("Identity provider must complete exactly once with a non-null credential before its routine finishes.");

            PlayerIdentity authenticatedPlayer = null;
            int authenticationCount = 0;
            bool isAcceptingIdentity = true;
            try
            {
                yield return backend.ExchangeCredential(credential, value =>
                {
                    if (isStopped || !isAcceptingIdentity) return;
                    authenticatedPlayer = value;
                    authenticationCount++;
                }) ?? throw new InvalidOperationException("Identity backend returned a null exchange routine.");
            }
            finally { isAcceptingIdentity = false; }
            if (isStopped) yield break;
            if (authenticationCount != 1 || authenticatedPlayer == null)
                throw new InvalidOperationException("Identity backend must complete exactly once with a verified player before its routine finishes.");
            currentPlayer = authenticatedPlayer;
        }
    }
}
