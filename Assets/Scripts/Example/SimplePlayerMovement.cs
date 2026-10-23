using UnityEngine;
using Unity.Netcode;

public class SimplePlayerMovement : NetworkBehaviour
{
    private float walkSpeed = 3f;
    private float runSpeed = 6f;
    private Animator animator;
    private static readonly int SpeedParam = Animator.StringToHash("Speed");

    void Start()
    {
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        if (!IsOwner) return;

        var mainCamera = Camera.main;
        if (mainCamera == null) return;

        float forward = 0f;
        float strafe = 0f;

        if (Input.GetKey(KeyCode.W)) forward += 1f;
        if (Input.GetKey(KeyCode.S)) forward -= 1f;
        if (Input.GetKey(KeyCode.A)) strafe -= 1f;
        if (Input.GetKey(KeyCode.D)) strafe += 1f;

        Vector3 cameraForward = mainCamera.transform.forward;
        Vector3 cameraRight = mainCamera.transform.right;
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
