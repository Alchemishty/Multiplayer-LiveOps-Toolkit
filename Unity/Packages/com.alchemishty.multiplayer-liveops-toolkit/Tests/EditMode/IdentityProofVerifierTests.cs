using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MultiplayerLiveOpsToolkit.Identity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

public class IdentityProofVerifierTests
{
    private const long NOW = 1900000010;
    private RSA signer;
    private RSA nextSigner;

    [OneTimeSetUp]
    public void CreateTestSigners()
    {
        signer = new RSACryptoServiceProvider(2048);
        nextSigner = new RSACryptoServiceProvider(2048);
    }

    [OneTimeTearDown]
    public void DisposeTestSigners()
    {
        signer.Dispose();
        nextSigner.Dispose();
    }

    private static IdentityProofPolicy Policy() => new IdentityProofPolicy(
        "https://identity.example.test", "sample-game", "test", "catalog-1", 60, 0);

    private static IdentityChallenge Challenge() => new IdentityChallenge("session-1", IdentityProofTestVector.NONCE, 1900000100);

    private IdentityProofVerifier Verifier() => new IdentityProofVerifier(
        new Dictionary<string, RSAParameters> { { "key-2026", signer.ExportParameters(false) } }, Policy());

    private static JObject Claims() => new JObject
    {
        ["iss"] = "https://identity.example.test", ["aud"] = "sample-game", ["env"] = "test", ["content"] = "catalog-1",
        ["sub"] = "player-42", ["name"] = "Verified Player", ["sid"] = "session-1", ["nonce"] = IdentityProofTestVector.NONCE,
        ["iat"] = 1900000000, ["nbf"] = 1900000000, ["exp"] = 1900000060, ["ver"] = 1,
        ["jti"] = "0123456789abcdef0123456789abcdef",
    };

    private string Sign(JObject claims = null, JObject header = null, RSA signingKey = null)
    {
        return SignJson((claims ?? Claims()).ToString(Formatting.None),
            (header ?? new JObject { ["alg"] = "RS256", ["typ"] = "liveops-identity+jwt", ["kid"] = "key-2026" }).ToString(Formatting.None), signingKey ?? signer);
    }

    private static string SignJson(string claims, string header, RSA signingKey)
    {
        string content = Encode(Encoding.UTF8.GetBytes(header)) + "." + Encode(Encoding.UTF8.GetBytes(claims));
        return content + "." + Encode(signingKey.SignData(Encoding.ASCII.GetBytes(content), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight((padded.Length + 3) / 4 * 4, '='));
    }

    [Test]
    public void TryVerify_DjangoGeneratedVector_ReturnsBackendPublicIdentity()
    {
        var verifier = new IdentityProofVerifier(new Dictionary<string, RSAParameters>
        {
            { "key-2026", new RSAParameters { Modulus = Decode(IdentityProofTestVector.MODULUS), Exponent = Decode(IdentityProofTestVector.EXPONENT) } },
        }, Policy());
        Assert.That(verifier.TryVerify(IdentityProofTestVector.ASSERTION, Challenge(), NOW, out var player, out var failure), Is.True);
        Assert.That(player.PlayerId, Is.EqualTo("player-42"));
        Assert.That(player.DisplayName, Is.EqualTo("Verified Player"));
        Assert.That(failure, Is.EqualTo(IdentityProofFailure.None));
    }

    [Test]
    public void TryVerify_ReusedChallenge_RejectsSecondProof()
    {
        var challenge = Challenge();
        var verifier = Verifier();
        Assert.That(verifier.TryVerify(Sign(), challenge, NOW, out _, out _), Is.True);
        Assert.That(challenge.IsConsumed, Is.True);
        Assert.That(verifier.TryVerify(Sign(), challenge, NOW, out var player, out var failure), Is.False);
        Assert.That(player, Is.Null);
        Assert.That(failure, Is.EqualTo(IdentityProofFailure.AlreadyConsumed));
    }

    [TestCase("iss", "\"other-issuer\"", IdentityProofFailure.InvalidContext)]
    [TestCase("aud", "\"other-game\"", IdentityProofFailure.InvalidContext)]
    [TestCase("env", "\"production\"", IdentityProofFailure.InvalidContext)]
    [TestCase("content", "\"catalog-2\"", IdentityProofFailure.InvalidContext)]
    [TestCase("sid", "\"session-2\"", IdentityProofFailure.InvalidContext)]
    [TestCase("nonce", "\"wrong-challenge\"", IdentityProofFailure.InvalidContext)]
    [TestCase("ver", "2", IdentityProofFailure.InvalidContext)]
    [TestCase("sub", "\" \"", IdentityProofFailure.Malformed)]
    [TestCase("name", "null", IdentityProofFailure.Malformed)]
    [TestCase("jti", "\"not-an-assertion-id\"", IdentityProofFailure.Malformed)]
    [TestCase("exp", "1900000005", IdentityProofFailure.Expired)]
    [TestCase("exp", "1900000301", IdentityProofFailure.Malformed)]
    [TestCase("exp", "1900000060.5", IdentityProofFailure.Malformed)]
    public void TryVerify_InvalidSignedClaims_RejectsWithoutConsumingChallenge(string field, string value, IdentityProofFailure expected)
    {
        var claims = Claims();
        claims[field] = JToken.Parse(value);
        var challenge = Challenge();
        var verifier = Verifier();
        Assert.That(verifier.TryVerify(Sign(claims), challenge, NOW, out var player, out var failure), Is.False);
        Assert.That(player, Is.Null);
        Assert.That(failure, Is.EqualTo(expected));
        Assert.That(challenge.IsConsumed, Is.False);
        Assert.That(verifier.TryVerify(Sign(), challenge, NOW, out _, out _), Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not.a.proof")]
    public void TryVerify_MalformedInput_FailsWithTypedError(string assertion)
    {
        Assert.That(Verifier().TryVerify(assertion, Challenge(), NOW, out _, out var failure), Is.False);
        Assert.That(failure, Is.EqualTo(IdentityProofFailure.Malformed));
    }

    [Test]
    public void TryVerify_OversizedAssertion_RejectsBeforeParsing()
    {
        Assert.That(Verifier().TryVerify(new string('a', 8193), Challenge(), NOW, out _, out var failure), Is.False);
        Assert.That(failure, Is.EqualTo(IdentityProofFailure.Malformed));
    }

    [Test]
    public void TryVerify_TamperedPlayerIdentity_RejectsSignature()
    {
        string original = Sign();
        var claims = Claims();
        claims["sub"] = "another-player";
        string[] segments = original.Split('.');
        segments[1] = Encode(Encoding.UTF8.GetBytes(claims.ToString(Formatting.None)));
        Assert.That(Verifier().TryVerify(string.Join(".", segments), Challenge(), NOW, out _, out var failure), Is.False);
        Assert.That(failure, Is.EqualTo(IdentityProofFailure.InvalidSignature));
    }

    [TestCase("alg", "none", IdentityProofFailure.Malformed)]
    [TestCase("alg", "HS256", IdentityProofFailure.Malformed)]
    [TestCase("typ", "JWT", IdentityProofFailure.Malformed)]
    [TestCase("kid", "unknown-key", IdentityProofFailure.UntrustedKey)]
    [TestCase("jku", "https://attacker.invalid/key", IdentityProofFailure.Malformed)]
    public void TryVerify_UntrustedHeader_Rejects(string field, string value, IdentityProofFailure expected)
    {
        var header = new JObject { ["alg"] = "RS256", ["typ"] = "liveops-identity+jwt", ["kid"] = "key-2026" };
        header[field] = value;
        Assert.That(Verifier().TryVerify(Sign(header: header), Challenge(), NOW, out _, out var failure), Is.False);
        Assert.That(failure, Is.EqualTo(expected));
    }

    [Test]
    public void TryVerify_DuplicateCriticalFields_RejectsAmbiguousJson()
    {
        string header = "{\"alg\":\"RS256\",\"alg\":\"RS256\",\"typ\":\"liveops-identity+jwt\",\"kid\":\"key-2026\"}";
        Assert.That(Verifier().TryVerify(SignJson(Claims().ToString(Formatting.None), header, signer), Challenge(), NOW, out _, out var failure), Is.False);
        Assert.That(failure, Is.EqualTo(IdentityProofFailure.Malformed));
        string claims = Claims().ToString(Formatting.None).TrimEnd('}') + ",\"sub\":\"another-player\"}";
        header = "{\"alg\":\"RS256\",\"typ\":\"liveops-identity+jwt\",\"kid\":\"key-2026\"}";
        Assert.That(Verifier().TryVerify(SignJson(claims, header, signer), Challenge(), NOW, out _, out failure), Is.False);
        Assert.That(failure, Is.EqualTo(IdentityProofFailure.Malformed));
    }

    [Test]
    public void TryVerify_FutureIssuedProof_RejectsUntilValid()
    {
        var claims = Claims();
        claims["iat"] = NOW + 1;
        claims["nbf"] = NOW + 1;
        claims["exp"] = NOW + 61;
        Assert.That(Verifier().TryVerify(Sign(claims), Challenge(), NOW, out _, out var failure), Is.False);
        Assert.That(failure, Is.EqualTo(IdentityProofFailure.NotYetValid));
    }

    [Test]
    public void TryVerify_ExpiredPeerChallenge_RejectsOtherwiseValidProof()
    {
        var challenge = new IdentityChallenge("session-1", IdentityProofTestVector.NONCE, NOW);
        Assert.That(Verifier().TryVerify(Sign(), challenge, NOW, out _, out var failure), Is.False);
        Assert.That(failure, Is.EqualTo(IdentityProofFailure.Expired));
    }

    [Test]
    public void TryVerify_KeyRotation_AcceptsOverlapAndRejectsRemovedKey()
    {
        var keys = new Dictionary<string, RSAParameters>
        {
            { "key-2026", signer.ExportParameters(false) }, { "next-key", nextSigner.ExportParameters(false) },
        };
        string nextProof = Sign(header: new JObject { ["alg"] = "RS256", ["typ"] = "liveops-identity+jwt", ["kid"] = "next-key" }, signingKey: nextSigner);
        var overlap = new IdentityProofVerifier(keys, Policy());
        Assert.That(overlap.TryVerify(Sign(), Challenge(), NOW, out _, out _), Is.True);
        Assert.That(overlap.TryVerify(nextProof, Challenge(), NOW, out _, out _), Is.True);
        keys.Remove("key-2026");
        var rotated = new IdentityProofVerifier(keys, Policy());
        Assert.That(rotated.TryVerify(Sign(), Challenge(), NOW, out _, out var failure), Is.False);
        Assert.That(failure, Is.EqualTo(IdentityProofFailure.UntrustedKey));
        Assert.That(rotated.TryVerify(nextProof, Challenge(), NOW, out _, out _), Is.True);
    }

    [Test]
    public void Constructor_TrustMaterialIsSnapshottedAndPrivateKeysRejected()
    {
        var publicKey = signer.ExportParameters(false);
        var keys = new Dictionary<string, RSAParameters> { { "key-2026", publicKey } };
        var verifier = new IdentityProofVerifier(keys, Policy());
        publicKey.Modulus[0] ^= 1;
        keys.Clear();
        Assert.That(verifier.TryVerify(Sign(), Challenge(), NOW, out _, out _), Is.True);
        Assert.Throws<ArgumentException>(() => new IdentityProofVerifier(new Dictionary<string, RSAParameters> { { "private", signer.ExportParameters(true) } }, Policy()));
        Assert.Throws<ArgumentNullException>(() => new IdentityProofVerifier(null, Policy()));
        Assert.Throws<ArgumentNullException>(() => new IdentityProofVerifier(new Dictionary<string, RSAParameters>(), null));
        Assert.Throws<ArgumentException>(() => new IdentityProofVerifier(new Dictionary<string, RSAParameters>(), Policy()));
    }

    [Test]
    public void TryVerify_ConcurrentReplay_ConsumesChallengeOnlyOnce()
    {
        string assertion = Sign();
        var verifier = Verifier();
        var challenge = Challenge();
        int acceptedCount = 0;
        Action verify = () => { if (verifier.TryVerify(assertion, challenge, NOW, out _, out _)) Interlocked.Increment(ref acceptedCount); };
        Task.WaitAll(Task.Run(verify), Task.Run(verify));
        Assert.That(acceptedCount, Is.EqualTo(1));
    }

    [Test]
    public void ChallengeAndPolicy_InvalidConfiguration_RejectBeforeVerification()
    {
        var first = IdentityChallenge.Create("session-1", NOW + 60);
        var second = IdentityChallenge.Create("session-1", NOW + 60);
        Assert.That(first.Nonce, Is.Not.EqualTo(second.Nonce));
        Assert.That(Decode(first.Nonce).Length, Is.EqualTo(32));
        Assert.Throws<ArgumentException>(() => new IdentityChallenge(" ", first.Nonce, NOW));
        Assert.Throws<ArgumentException>(() => new IdentityChallenge("session-1", "bad", NOW));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IdentityChallenge("session-1", first.Nonce, 0));
        Assert.Throws<ArgumentException>(() => new IdentityProofPolicy("", "game", "env", "content", 60, 0));
        Assert.Throws<ArgumentException>(() => new IdentityProofPolicy("issuer", " ", "env", "content", 60, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IdentityProofPolicy("issuer", "game", "env", "content", 301, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IdentityProofPolicy("issuer", "game", "env", "content", 60, 31));
    }
}
