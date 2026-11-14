using UnityEngine;

public enum PlayerBodyRegion { Head, Torso, Arm, Hand, Leg, Foot }

[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public sealed class PlayerBodyHitbox : MonoBehaviour
{
    public CourseHitController Owner { get; set; }
    public PlayerBodyRegion Region { get; set; }
    public Transform From { get; set; }
    public Transform To { get; set; }
    public Vector3 LocalOffset { get; set; }
    public float Radius;
    public float MinimumLength;
    Vector3 cachedPosition;
    Quaternion cachedRotation;
    Rigidbody body;
    CapsuleCollider shape;

    public void Initialize()
    {
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        shape = GetComponent<CapsuleCollider>();
        shape.isTrigger = true;
        shape.direction = 1;
        CachePose();
        body.position = cachedPosition;
        body.rotation = cachedRotation;
        shape.enabled = false;
    }

    public void CachePose()
    {
        Vector3 a = From.position;
        Vector3 b = To != null ? To.position : a;
        cachedPosition = (a + b) * 0.5f + From.rotation * LocalOffset;
        Vector3 direction = b - a;
        cachedRotation = direction.sqrMagnitude > 0.000001f
            ? Quaternion.FromToRotation(Vector3.up, direction.normalized) : From.rotation;
        shape = shape != null ? shape : GetComponent<CapsuleCollider>();
        shape.radius = Radius;
        shape.height = Mathf.Max(Radius * 2f, direction.magnitude + Radius * 2f, MinimumLength);
    }

    void FixedUpdate()
    {
        if (Owner == null) return;
        shape.enabled = Owner.TrackingReady;
        body.MovePosition(cachedPosition);
        body.MoveRotation(cachedRotation);
    }

    void OnTriggerEnter(Collider other) { Owner?.Contact(this, other, true); }
    void OnTriggerStay(Collider other) { Owner?.Contact(this, other, true); }
    void OnTriggerExit(Collider other) { Owner?.Contact(this, other, false); }
}
