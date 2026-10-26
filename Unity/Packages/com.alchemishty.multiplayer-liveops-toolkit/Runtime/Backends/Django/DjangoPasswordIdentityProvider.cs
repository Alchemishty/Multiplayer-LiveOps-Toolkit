using System;
using System.Collections;
using MultiplayerLiveOpsToolkit.Identity;
using Newtonsoft.Json;

namespace MultiplayerLiveOpsToolkit.Backends.Django
{
    /// <summary>One-use Django credential source. Never persists the supplied password.</summary>
    public sealed class DjangoPasswordIdentityProvider : IIdentityProvider
    {
        private IdentityCredential credential;

        public DjangoPasswordIdentityProvider(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || username.Length > 150)
                throw new ArgumentException("Django login requires a username of at most 150 characters.", nameof(username));
            if (string.IsNullOrEmpty(password) || password.Length > 4096)
                throw new ArgumentException("Django login requires a password of at most 4096 characters.", nameof(password));
            credential = new IdentityCredential("django-password", JsonConvert.SerializeObject(new PasswordRequest
            {
                Username = username,
                Password = password,
            }));
        }

        public IEnumerator GetCredential(Action<IdentityCredential> onCredential)
        {
            if (onCredential == null) throw new ArgumentNullException(nameof(onCredential));
            if (credential == null) throw new InvalidOperationException("Django password credential was already consumed. Create a fresh provider for another attempt.");
            return SupplyCredential(onCredential);
        }

        private IEnumerator SupplyCredential(Action<IdentityCredential> onCredential)
        {
            try
            {
                onCredential(credential ?? throw new InvalidOperationException("Django password credential was already consumed."));
            }
            finally { credential = null; }
            yield break;
        }

        private sealed class PasswordRequest
        {
            [JsonProperty("username")] public string Username;
            [JsonProperty("password")] public string Password;
        }
    }
}
