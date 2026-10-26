using System.Collections;
using MultiplayerLiveOpsToolkit.Netcode.Avatars;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class AvatarRegistryTests
{
    [UnityTest]
    public IEnumerator RegisterAndReplace_LateOldDespawn_DoesNotRemoveReplacement()
    {
        var owner = new GameObject("Registry");
        var first = new GameObject("First"); var replacement = new GameObject("Replacement");
        var registry = owner.AddComponent<AvatarRegistry>();
        int changes = 0; System.Action changed = () => changes++;
        registry.LocalAvatarChanged += changed;
        try
        {
            registry.RegisterAvatar(7, first, true);
            Assert.That(registry.LocalAvatar, Is.SameAs(first));
            registry.RegisterAvatar(7, replacement, true);
            registry.RemoveAvatar(7, first);
            Assert.That(registry.GetAvatar(7), Is.SameAs(replacement));
            registry.RemoveAvatar(7, replacement);
            Assert.That(registry.LocalAvatar, Is.Null);
            Assert.That(changes, Is.EqualTo(3));
        }
        finally { registry.LocalAvatarChanged -= changed; Object.Destroy(owner); Object.Destroy(first); Object.Destroy(replacement); }
        yield return null;
    }
    [UnityTest]
    public IEnumerator EquipmentInitialization_MissingNetworkObject_ReportsCompositionError()
    {
        var avatar = new GameObject("Invalid equipment avatar");
        try
        {
            var equipment = avatar.AddComponent<PlayerAccessory>();
            var loadout = new MultiplayerLiveOpsToolkit.Equipment.EquipmentLoadout(0, new System.Collections.Generic.Dictionary<string, string>());
            var error = Assert.Throws<System.InvalidOperationException>(() => equipment.InitializeVerifiedEquipment(loadout));
            Assert.That(error.Message, Does.Contain("NetworkObject"));
        }
        finally { Object.Destroy(avatar); }
        yield return null;
    }
    [UnityTest]
    public IEnumerator IndependentRegistries_SubscriberFailure_DoesNotShareStateOrSuppressLaterObserver()
    {
        var firstOwner = new GameObject("First registry"); var secondOwner = new GameObject("Second registry"); var avatar = new GameObject("Avatar");
        var first = firstOwner.AddComponent<AvatarRegistry>(); var second = secondOwner.AddComponent<AvatarRegistry>();
        System.Action throwing = () => throw new System.InvalidOperationException("Expected subscriber error");
        bool hasNotified = false; System.Action later = () => hasNotified = true;
        first.LocalAvatarChanged += throwing; first.LocalAvatarChanged += later;
        try
        {
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: Expected subscriber error");
            first.RegisterAvatar(1, avatar, true);
            Assert.That(hasNotified, Is.True);
            Assert.That(second.GetAvatar(1), Is.Null);
        }
        finally { first.LocalAvatarChanged -= throwing; first.LocalAvatarChanged -= later; Object.Destroy(firstOwner); Object.Destroy(secondOwner); Object.Destroy(avatar); }
        yield return null;
    }
}
