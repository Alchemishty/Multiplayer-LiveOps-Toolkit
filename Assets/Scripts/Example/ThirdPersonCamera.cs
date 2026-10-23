using UnityEngine;

public class ThirdPersonCamera : MonoBehaviour
{
    [SerializeField] private float distance = 2f;
    [SerializeField] private float heightOffset = 0f;
    [SerializeField] private float smoothSpeed = 8f;
    [SerializeField] private float orbitSpeed = 90f;

    private Transform target;
    private float orbitAngle = 180f;
    private float characterHeight = 1.5f;
    private bool isTargetResolved;

    void LateUpdate()
    {
        if (!isTargetResolved)
        {
            if (ClientPlayerAvatars.Instance != null && ClientPlayerAvatars.Instance.currentClientAvatar != null)
            {
                target = ClientPlayerAvatars.Instance.currentClientAvatar.transform;
                characterHeight = 0.85f;

                isTargetResolved = true;
                SnapToTarget();
            }
            else
                return;
        }

        if (Input.GetKey(KeyCode.Q))
            orbitAngle -= orbitSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.E))
            orbitAngle += orbitSpeed * Time.deltaTime;

        float eyeHeight = characterHeight + heightOffset;
        Vector3 lookTarget = target.position + Vector3.up * eyeHeight;
        Vector3 offset = Quaternion.Euler(0, orbitAngle, 0) * new Vector3(0, 0, -distance);
        Vector3 desiredPosition = lookTarget + offset;

        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
        transform.LookAt(lookTarget);
    }

    private void SnapToTarget()
    {
        float initialEyeHeight = characterHeight + heightOffset;
        Vector3 initialLookTarget = target.position + Vector3.up * initialEyeHeight;
        Vector3 initialOffset = Quaternion.Euler(0, orbitAngle, 0) * new Vector3(0, 0, -distance);
        transform.position = initialLookTarget + initialOffset;
        transform.LookAt(initialLookTarget);
    }
}
