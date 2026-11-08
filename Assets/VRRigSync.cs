using UnityEngine;

public class VRRigSync : MonoBehaviour
{
    [Header("Tracking Target")]
    [SerializeField] private Transform headTarget;
    [SerializeField] private float bodyHeightOffset = 0.0f;
    [SerializeField] private float turnSpeed = 5f;

    void LateUpdate()
    {
        if (headTarget == null) return;

        // Keep root grounded at floor level while following head horizontally
        transform.position = new Vector3(headTarget.position.x, bodyHeightOffset, headTarget.position.z);

        // Project head forward vector onto horizontal plane (keeps body upright)
        Vector3 headForward = headTarget.forward;
        headForward.y = 0;

        if (headForward.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(headForward);
            
            // Constantly and smoothly rotate the body to follow the camera
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * turnSpeed);
        }
    }
}