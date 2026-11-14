using UnityEngine;

public class WindmillRotator : MonoBehaviour
{
    [Header("Rotation")]
    [Tooltip("Degrees per second. Negative values spin the other way.")]
    public float speed = 40f;

    [Tooltip("Axis to spin around, in the Windmill's local space.")]
    public Vector3 axis = Vector3.up;

    [Header("Optional Speed Variation")]
    public bool varySpeed = false;
    [Tooltip("How far the speed swings above and below the base speed (0.5 = ±50%).")]
    [Range(0f, 1f)] public float variationAmount = 0.3f;
    [Tooltip("How fast the speed oscillates, in cycles per second.")]
    public float variationRate = 0.2f;

    public CourseHitController Course { get; set; }
    float motionTime;
    void Awake() { motionTime = Time.time; }

    void Update()
    {
        if (Course != null && Course.IsPaused) return;
        motionTime += Time.deltaTime;
        float currentSpeed = speed;

        if (varySpeed)
        {
            currentSpeed *= 1f + Mathf.Sin(motionTime * variationRate * Mathf.PI * 2f) * variationAmount;
        }

        transform.Rotate(axis.normalized, currentSpeed * Time.deltaTime, Space.Self);
    }
}