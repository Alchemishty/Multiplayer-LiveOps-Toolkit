using System;
using System.Collections;
using UnityEngine.Networking;

namespace MultiplayerLiveOpsToolkit.UnityIntegration
{
    /// <summary>Account-scoped authenticated requests and lifetime; exposes no bearer credentials.</summary>
    public interface IAuthenticatedSession : IDisposable
    {
        event Action<string> SessionEnded;
        int SessionVersion { get; }
        bool IsActive { get; }
        IEnumerator Send(UnityWebRequest request, ApiResponse response, Action<bool> onComplete = null);
        void RejectSession(int expectedVersion, string reason = null);
    }
}
