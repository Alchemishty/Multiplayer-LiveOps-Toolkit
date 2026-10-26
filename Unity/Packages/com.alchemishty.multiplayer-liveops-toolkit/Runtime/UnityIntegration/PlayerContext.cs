using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Core;
using MultiplayerLiveOpsToolkit.Identity;

namespace MultiplayerLiveOpsToolkit.UnityIntegration
{
    /// <summary>Owns one player's Identity runtime and authenticated backend-session lifetime.</summary>
    public sealed class PlayerContext : IDisposable
    {
        private LiveOpsRuntime runtime;
        private IdentityModule identity;
        private bool isDisposed;
        public IAuthenticatedSession Session { get; }
        public IIdentityProofBackend IdentityProofs { get; }
        public bool IsAuthenticated => !isDisposed && runtime != null && runtime.State == LiveOpsRuntimeState.Ready && Session.IsActive;
        public PlayerIdentity CurrentPlayer => IsAuthenticated ? identity.CurrentPlayer : throw new InvalidOperationException("PlayerContext is not authenticated.");
        public event Action<string> SessionEnded;

        public PlayerContext(IAuthenticatedSession session, IIdentityProofBackend identityProofs)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            IdentityProofs = identityProofs ?? throw new ArgumentNullException(nameof(identityProofs));
            Session.SessionEnded += OnSessionEnded;
        }

        public IEnumerator Initialize(IIdentityProvider provider, IIdentityBackend backend, Action onReady, Action<LiveOpsError> onFailure, IEnumerable<ILiveOpsModule> modules = null)
        {
            if (isDisposed) throw new InvalidOperationException("PlayerContext has stopped. Create a new context.");
            if (onReady == null) throw new ArgumentNullException(nameof(onReady));
            if (onFailure == null) throw new ArgumentNullException(nameof(onFailure));
            if (runtime != null && runtime.State != LiveOpsRuntimeState.Stopped)
                throw new InvalidOperationException("PlayerContext already has an initialized or pending Identity runtime.");
            var nextIdentity = new IdentityModule(provider, backend);
            var selectedModules = new List<ILiveOpsModule> { nextIdentity };
            if (modules != null) selectedModules.AddRange(modules);
            var nextRuntime = new LiveOpsRuntime(selectedModules);
            var initialization = nextRuntime.Initialize(onReady, onFailure);
            identity = nextIdentity;
            runtime = nextRuntime;
            return initialization;
        }

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;
            Session.SessionEnded -= OnSessionEnded;
            identity = null;
            var failures = new List<Exception>();
            try { runtime?.Shutdown(); }
            catch (Exception exception) { failures.Add(exception); }
            try { Session.Dispose(); }
            catch (Exception exception) { failures.Add(exception); }
            if (failures.Count > 0) throw new AggregateException("PlayerContext cleanup failed after releasing its session.", failures);
        }

        private void OnSessionEnded(string reason)
        {
            try { Dispose(); }
            finally
            {
                var subscribers = SessionEnded;
                if (subscribers != null)
                {
                    var failures = new List<Exception>();
                    foreach (Action<string> subscriber in subscribers.GetInvocationList())
                    {
                        try { subscriber(reason); }
                        catch (Exception exception) { failures.Add(exception); }
                    }
                    if (failures.Count > 0) throw new AggregateException("PlayerContext terminal notification failed.", failures);
                }
            }
        }
    }
}
