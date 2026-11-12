using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class RotatingArena : MonoBehaviour
{
    [Tooltip("Rotation speed in degrees per second")]
    [SerializeField] private float rotationSpeed = 12.0f;

    private Rigidbody rb;

    // Animation can read the floor motion without changing how the arena rotates.
    public float RotationSpeed => rotationSpeed;

    public Vector3 GetSurfaceVelocity(Vector3 worldPosition)
    {
        if (!isActiveAndEnabled) return Vector3.zero;
        Vector3 angularVelocity = transform.up * (rotationSpeed * Mathf.Deg2Rad);
        return Vector3.Cross(angularVelocity, worldPosition - transform.position);
    }

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
    }

    void FixedUpdate()
    {
        // Rotate around the Y axis
        Quaternion deltaRotation = Quaternion.Euler(0f, rotationSpeed * Time.fixedDeltaTime, 0f);
        rb.MoveRotation(rb.rotation * deltaRotation);
    }
}
