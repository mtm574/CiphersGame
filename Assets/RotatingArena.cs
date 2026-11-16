using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class RotatingArena : MonoBehaviour
{
    [SerializeField] private float rotationSpeed = 12.0f;
    [SerializeField] private float rotationDelay = 79.0f;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioSource secondAudioSource;
    [SerializeField] private float secondAudioDelay = 68.0f;

    [SerializeField] private JumpWaveController jumpWaveController;

    private float delayRemaining;
    private float secondAudioTimer;
    private Rigidbody rb;
    private bool hasStarted;
    private bool secondAudioPlayed;

    public CourseHitController Course { get; set; }
    public bool IsCoursePaused => Course != null && Course.IsPaused;

    public float RotationSpeed => rotationSpeed;
    public float RotationDelay => rotationDelay;

    public Vector3 GetSurfaceVelocity(Vector3 worldPosition)
    {
        if (!isActiveAndEnabled || IsCoursePaused || !hasStarted)
            return Vector3.zero;

        Vector3 angularVelocity =
            transform.up * (rotationSpeed * Mathf.Deg2Rad);

        return Vector3.Cross(
            angularVelocity,
            worldPosition - transform.position
        );
    }

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;

        delayRemaining = Mathf.Max(0f, rotationDelay);
        secondAudioTimer = secondAudioDelay;

        // Play the first audio immediately.
        if (audioSource != null)
            audioSource.Play();
    }

    void Update()
    {
        if (IsCoursePaused || secondAudioPlayed)
            return;

        secondAudioTimer -= Time.deltaTime;

        if (secondAudioTimer <= 0f)
        {
            secondAudioPlayed = true;

            // Play the second audio 68 seconds after game start.
            if (secondAudioSource != null)
                secondAudioSource.Play();
        }
    }

    void FixedUpdate()
    {
        if (IsCoursePaused)
            return;

        if (!hasStarted)
        {
            delayRemaining -= Time.fixedDeltaTime;

            if (delayRemaining > 0f)
                return;

            hasStarted = true;

            if (jumpWaveController != null)
                jumpWaveController.StartJumping();
        }

        Quaternion deltaRotation = Quaternion.Euler(
            0f,
            rotationSpeed * Time.fixedDeltaTime,
            0f
        );

        rb.MoveRotation(rb.rotation * deltaRotation);
    }
}