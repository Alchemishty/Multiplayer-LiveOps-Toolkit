using System;
using MultiplayerLiveOpsToolkit.Identity;
using Newtonsoft.Json;
using NUnit.Framework;

public class IdentityTrustConfigurationTests
{
    [Test]
    public void ExportedPublicTrust_CreatesVerifierWithMatchingPolicy()
    {
        string json = JsonConvert.SerializeObject(new {
            issuer = "https://identity.example.test", game_id = "sample-game", environment = "test", content_version = "catalog-1",
            max_lifetime_seconds = 60, clock_skew_seconds = 0,
            public_keys = new[] { new { key_id = "key-2026", modulus = IdentityProofTestVector.MODULUS.Replace('-', '+').Replace('_', '/') + "==", exponent = IdentityProofTestVector.EXPONENT } }
        });
        var verifier = IdentityTrustConfiguration.CreateVerifier(json);
        var challenge = new IdentityChallenge("session-1", IdentityProofTestVector.NONCE, 1900000060);
        Assert.That(verifier.TryVerify(IdentityProofTestVector.ASSERTION, challenge, 1900000000, out var identity, out _), Is.True);
        Assert.That(identity.PlayerId, Is.EqualTo("player-42"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("null")]
    [TestCase("{}")]
    [TestCase("{\"issuer\":\"a\",\"issuer\":\"b\"}")]
    [TestCase("{\"public_keys\":[]}")]
    public void InvalidTrust_FailsBeforeNetworkStart(string json)
    {
        Assert.Throws<ArgumentException>(() => IdentityTrustConfiguration.CreateVerifier(json));
    }
}
