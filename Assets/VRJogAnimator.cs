using UnityEngine;
using UnityEngine.XR;

// Run after tracking updates but before VRRigSync's manual animation/IK evaluation.
[DefaultExecutionOrder(9000)]
[RequireComponent(typeof(Animator))]
public class VRJogAnimator : MonoBehaviour
{
    [SerializeField] private Transform headset;
    [SerializeField] private RotatingArena arena;
    [Tooltip("Travel speed represented by the jogging clip at normal playback, in metres/second.")]
    [SerializeField, Min(0.01f)] private float referenceSpeed = 4.23f;
    [SerializeField, Min(0f)] private float velocitySmoothing = 0.15f;
    [SerializeField, Min(0.01f)] private float blendDuration = 0.2f;
    [SerializeField, Min(0f)] private float startSpeed = 0.15f;
    [SerializeField, Min(0f)] private float stopSpeed = 0.08f;
    [SerializeField, Min(0.01f)] private float teleportDistance = 0.5f;

    private static readonly int JogWeight = Animator.StringToHash("JogWeight");
    private static readonly int JogRate = Animator.StringToHash("JogRate");
    private Animator animator;
    private Vector3 previousPosition;
    private Vector3 relativeVelocity;
    private float weight;
    private bool sampled;
    private bool jogging;
    private bool ready;

    void Awake()
    {
        animator = GetComponent<Animator>();
        bool hasWeight = false, hasRate = false;
        foreach (var parameter in animator.parameters)
        {
            if (parameter.type != AnimatorControllerParameterType.Float) continue;
            hasWeight |= parameter.nameHash == JogWeight;
            hasRate |= parameter.nameHash == JogRate;
        }
        ready = headset != null && arena != null && hasWeight && hasRate;
        if (!ready)
        {
            Debug.LogWarning("VR jogging needs a headset, arena, and JogWeight/JogRate float parameters.", this);
            enabled = false;
        }
    }

    void OnEnable() { ResetMotion(); }
    void OnDisable() { ResetMotion(); }
    void OnApplicationFocus(bool hasFocus) { ResetMotion(); }

    public void ResetForCoursePause() { ResetMotion(); }

    private void ResetMotion()
    {
        sampled = jogging = false;
        relativeVelocity = Vector3.zero;
        weight = 0f;
        if (ready)
        {
            animator.SetFloat(JogWeight, 0f);
            animator.SetFloat(JogRate, 1f);
        }
    }

    void LateUpdate()
    {
        var device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        bool tracked = device.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked) && isTracked;
        if (device.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState state))
            tracked &= (state & InputTrackingState.Position) != 0;
        UpdateLocomotion(tracked, Time.deltaTime);
    }

    // Tracking-independent entry point also allows repeatable editor validation.
    private void UpdateLocomotion(bool tracked, float deltaTime)
    {
        if (!ready) return;
        if (!tracked || (arena != null && arena.IsCoursePaused) || headset == null || arena == null || !animator.enabled || deltaTime <= 0f || deltaTime > 0.25f)
        {
            ResetMotion();
            return;
        }

        Vector3 position = headset.position;
        Vector3 displacement = sampled ? position - previousPosition : Vector3.zero;
        previousPosition = position;
        sampled = true;
        if (displacement.sqrMagnitude > teleportDistance * teleportDistance)
        {
            ResetMotion();
            return;
        }

        Vector3 velocity = Vector3.ProjectOnPlane(displacement / deltaTime - arena.GetSurfaceVelocity(position), Vector3.up);
        float blend = velocitySmoothing <= 0f ? 1f : 1f - Mathf.Exp(-deltaTime / velocitySmoothing);
        relativeVelocity = Vector3.Lerp(relativeVelocity, velocity, blend);
        float speed = relativeVelocity.magnitude;
        jogging = jogging ? speed > stopSpeed : speed > startSpeed;
        weight = Mathf.MoveTowards(weight, jogging ? 1f : 0f, deltaTime / Mathf.Max(0.01f, blendDuration));
        animator.SetFloat(JogWeight, weight);
        animator.SetFloat(JogRate, Mathf.Clamp(speed / Mathf.Max(0.01f, referenceSpeed), 0.5f, 2f));
    }
}
