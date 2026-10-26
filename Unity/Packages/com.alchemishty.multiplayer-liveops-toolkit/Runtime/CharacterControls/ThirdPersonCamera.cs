using System;
using MultiplayerLiveOpsToolkit.Netcode.Avatars;
using UnityEngine;
namespace MultiplayerLiveOpsToolkit.CharacterControls
{
    public class ThirdPersonCamera : MonoBehaviour
    {
        [SerializeField] private float distance = 2f;
        [SerializeField] private float heightOffset;
        [SerializeField] private float smoothSpeed = 8f;
        [SerializeField] private float orbitSpeed = 90f;
        [SerializeField] private float characterHeight = 0.85f;
        [SerializeField] private AvatarRegistry avatarRegistry;
        private Transform target;
        private float orbitAngle = 180f;
        public void Bind(AvatarRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (isActiveAndEnabled && avatarRegistry != null) avatarRegistry.LocalAvatarChanged -= ResolveTarget;
            avatarRegistry = registry;
            if (isActiveAndEnabled) { avatarRegistry.LocalAvatarChanged += ResolveTarget; ResolveTarget(); }
        }
        private void OnEnable()
        {
            if (avatarRegistry == null) return; // A consuming game can bind its registry after creating the camera.
            avatarRegistry.LocalAvatarChanged += ResolveTarget;
            ResolveTarget();
        }
        private void OnDisable()
        {
            if (avatarRegistry != null) avatarRegistry.LocalAvatarChanged -= ResolveTarget;
            target = null;
        }
        private void ResolveTarget()
        {
            target = avatarRegistry.LocalAvatar != null ? avatarRegistry.LocalAvatar.transform : null;
            if (target != null) UpdateCamera(true);
        }
        private void LateUpdate()
        {
            if (target == null) return; // No local avatar exists before spawn or after despawn.
            if (Input.GetKey(KeyCode.Q)) orbitAngle -= orbitSpeed * Time.deltaTime;
            if (Input.GetKey(KeyCode.E)) orbitAngle += orbitSpeed * Time.deltaTime;
            UpdateCamera(false);
        }
        private void UpdateCamera(bool shouldSnap)
        {
            var lookTarget = target.position + Vector3.up * (characterHeight + heightOffset);
            var desiredPosition = lookTarget + Quaternion.Euler(0, orbitAngle, 0) * new Vector3(0, 0, -distance);
            transform.position = shouldSnap ? desiredPosition : Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
            transform.LookAt(lookTarget);
        }
    }
}
