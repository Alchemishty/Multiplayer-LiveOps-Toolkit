using UnityEngine;
using Unity.Netcode;

namespace MultiplayerLiveOpsToolkit.CharacterControls
{
public class SimplePlayerMovement : NetworkBehaviour
{
    [SerializeField] private float walkSpeed = 3f;
    [SerializeField] private float runSpeed = 6f;
    private Animator animator;
    private static readonly int SpeedParam = Animator.StringToHash("Speed");

    private Camera movementCamera;

    void Awake()
    {
        animator = GetComponent<Animator>();
        movementCamera = Camera.main;
    }

    void Update()
    {
        if (!IsSpawned || !IsOwner) return;

        if (movementCamera == null) return; // A headless scene has no local movement camera.

        float forward = 0f;
        float strafe = 0f;

        if (Input.GetKey(KeyCode.W)) forward += 1f;
        if (Input.GetKey(KeyCode.S)) forward -= 1f;
        if (Input.GetKey(KeyCode.A)) strafe -= 1f;
        if (Input.GetKey(KeyCode.D)) strafe += 1f;

        Vector3 cameraForward = movementCamera.transform.forward;
        Vector3 cameraRight = movementCamera.transform.right;
        cameraForward.y = 0;
        cameraRight.y = 0;
        cameraForward.Normalize();
        cameraRight.Normalize();

        Vector3 input = cameraForward * forward + cameraRight * strafe;
        float inputMagnitude = Mathf.Clamp01(input.magnitude);

        bool isRunning = Input.GetKey(KeyCode.LeftShift);
        float currentSpeed = isRunning ? runSpeed : walkSpeed;
        float animSpeed = isRunning ? 1f : inputMagnitude * 0.5f;

        if (inputMagnitude > 0.1f)
        {
            transform.position += input.normalized * currentSpeed * Time.deltaTime;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(input.normalized), 10f * Time.deltaTime);
        }

        if (animator != null)
            animator.SetFloat(SpeedParam, inputMagnitude > 0.1f ? animSpeed : 0f);
    }
}

}
