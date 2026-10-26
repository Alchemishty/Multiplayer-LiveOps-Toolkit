using System.Collections;
using MultiplayerLiveOpsToolkit.CharacterControls;
using MultiplayerLiveOpsToolkit.Netcode.Avatars;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class CharacterControlsTests
{
    [UnityTest]
    public IEnumerator Camera_ReplacementDespawnAndReenable_TracksCurrentAvatar()
    {
        var owner = new GameObject("Registry"); var cameraObject = new GameObject("Camera");
        var first = new GameObject("First avatar"); var replacement = new GameObject("Replacement avatar");
        var registry = owner.AddComponent<AvatarRegistry>();
        var camera = cameraObject.AddComponent<ThirdPersonCamera>();
        try
        {
            first.transform.position = new Vector3(10, 0, 0); replacement.transform.position = new Vector3(50, 0, 0);
            camera.Bind(registry); registry.RegisterAvatar(0, first, true);
            Assert.That(camera.transform.position.x, Is.EqualTo(10).Within(0.01));
            registry.RegisterAvatar(0, replacement, true);
            Assert.That(camera.transform.position.x, Is.EqualTo(50).Within(0.01));
            camera.enabled = false;
            registry.RegisterAvatar(0, first, true);
            Assert.That(camera.transform.position.x, Is.EqualTo(50).Within(0.01));
            camera.enabled = true;
            Assert.That(camera.transform.position.x, Is.EqualTo(10).Within(0.01));
            registry.RemoveAvatar(0, first); Object.Destroy(first);
            yield return null;
        }
        finally { Object.Destroy(owner); Object.Destroy(cameraObject); Object.Destroy(replacement); if (first != null) Object.Destroy(first); }
    }
}
