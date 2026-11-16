using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

[DefaultExecutionOrder(11000)]
public sealed class CourseHitController : MonoBehaviour
{
    [SerializeField] Animator avatar;
    [SerializeField] Camera playerCamera;
    [SerializeField] Shader flashShader;
    [SerializeField] AudioSource audioSource;
    [SerializeField, Min(0f)] float pauseSeconds = 2f;
    [SerializeField, Min(0f)] float rearmSeconds = 0.1f;
    [System.Serializable]
    public sealed class HitboxSizes
    {
        [Min(.005f)] public float head = .11f, torso = .15f, upperArm = .06f, forearm = .05f, hand = .07f, thigh = .09f, calf = .07f, foot = .06f;
    }
    [SerializeField] HitboxSizes hitboxSizes = new HitboxSizes();
    readonly List<PlayerBodyHitbox> hitboxes = new List<PlayerBodyHitbox>();
    sealed class ContactState
    {
        public readonly HashSet<(PlayerBodyHitbox, Collider)> Pairs = new HashSet<(PlayerBodyHitbox, Collider)>();
        public float ClearTime;
    }
    readonly Dictionary<CourseObstacle, ContactState> contacts = new Dictionary<CourseObstacle, ContactState>();
    readonly List<CourseObstacle> clear = new List<CourseObstacle>();
    float pauseRemaining;
    float clock;
    float trackedTime;
    VRHitFlash flash;
    GameObject proxyRoot;
    bool wasPaused;
    public bool IsPaused => isActiveAndEnabled && pauseRemaining > 0f;
    public bool TrackingReady { get; private set; }
    public float PauseRemaining => pauseRemaining;

    void Awake()
    {
        if (avatar == null || playerCamera == null || flashShader == null || !avatar.isHuman)
        {
            Debug.LogError("Course feedback requires a Humanoid avatar, VR camera, and flash shader.", this);
            enabled = false;
            return;
        }
        foreach (var mover in GetComponentsInChildren<RotatingArena>(true)) mover.Course = this;
        foreach (var mover in GetComponentsInChildren<WindmillRotator>(true)) mover.Course = this;
        foreach (var mover in GetComponentsInChildren<SideToSide>(true)) mover.Course = this;
        flash = gameObject.AddComponent<VRHitFlash>();
        flash.Initialize(playerCamera, flashShader);

        proxyRoot = new GameObject("Player Hitboxes (runtime)");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(proxyRoot, gameObject.scene);
        proxyRoot.transform.SetParent(transform.parent, true); // Never inherit the rotating arena or tiny head scale.
        float scale = Mathf.Abs(avatar.transform.lossyScale.x);
        Add(PlayerBodyRegion.Head, playerCamera.transform, null, hitboxSizes.head * scale, Vector3.zero);
        Add(PlayerBodyRegion.Torso, Bone(HumanBodyBones.Hips), Bone(HumanBodyBones.Neck), hitboxSizes.torso * scale, Vector3.zero);
        for (int side = 0; side < 2; side++)
        {
            bool left = side == 0;
            var upperArm = Bone(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            var forearm = Bone(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            var hand = Bone(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            var thigh = Bone(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
            var shin = Bone(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            var foot = Bone(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            Add(PlayerBodyRegion.Arm, upperArm, forearm, hitboxSizes.upperArm * scale, Vector3.zero);
            Add(PlayerBodyRegion.Arm, forearm, hand, hitboxSizes.forearm * scale, Vector3.zero);
            Add(PlayerBodyRegion.Hand, hand, Bone(left ? HumanBodyBones.LeftMiddleDistal : HumanBodyBones.RightMiddleDistal), hitboxSizes.hand * scale, Vector3.zero);
            Add(PlayerBodyRegion.Leg, thigh, shin, hitboxSizes.thigh * scale, Vector3.zero);
            Add(PlayerBodyRegion.Leg, shin, foot, hitboxSizes.calf * scale, Vector3.zero);
            // Offset uses rotation only; bone scale cannot collapse the proxy.
            Add(PlayerBodyRegion.Foot, foot, Bone(left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes), hitboxSizes.foot * scale, Vector3.zero);
        }
    }
    Transform Bone(HumanBodyBones bone) => avatar.GetBoneTransform(bone);
    void Add(PlayerBodyRegion region, Transform from, Transform to, float radius, Vector3 offset)
    {
        if (from == null) { Debug.LogWarning("Missing hitbox bone for " + region, this); return; }
        var go = new GameObject(region + " Hitbox");
        go.transform.SetParent(proxyRoot.transform, false);
        go.layer = LayerMask.NameToLayer("PlayerHitbox");
        var hitbox = go.AddComponent<PlayerBodyHitbox>();
        hitbox.Owner = this; hitbox.Region = region; hitbox.From = from; hitbox.To = to;
        hitbox.Radius = radius; hitbox.LocalOffset = offset;
        hitbox.Initialize();
        hitboxes.Add(hitbox);
    }
    void Update() { Tick(Time.unscaledDeltaTime); }
    public void Tick(float seconds)
    {
        clock += seconds;
        pauseRemaining = Mathf.Max(0f, pauseRemaining - seconds);
        if (wasPaused && !IsPaused) ResetJogging();
        wasPaused = IsPaused;
        clear.Clear();
        foreach (var entry in contacts)
        {
            int before = entry.Value.Pairs.Count;
            entry.Value.Pairs.RemoveWhere(pair => pair.Item1 == null || !pair.Item1.isActiveAndEnabled
                || pair.Item2 == null || !pair.Item2.enabled || !pair.Item2.gameObject.activeInHierarchy);
            if (before > 0 && entry.Value.Pairs.Count == 0) entry.Value.ClearTime = clock;
            if (entry.Key == null || (entry.Value.Pairs.Count == 0 && clock - entry.Value.ClearTime >= rearmSeconds)) clear.Add(entry.Key);
        }
        foreach (var key in clear) contacts.Remove(key);
    }
    void LateUpdate()
    {
        var device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        TrackingReady = device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
        if (device.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState state))
            TrackingReady &= (state & (InputTrackingState.Position | InputTrackingState.Rotation)) == (InputTrackingState.Position | InputTrackingState.Rotation);
        if (!TrackingReady) { trackedTime = 0f; contacts.Clear(); return; }
        trackedTime += Time.unscaledDeltaTime;
        TrackingReady = trackedTime >= 0.3f;
        if (!TrackingReady) return;
        foreach (var box in hitboxes) box.CachePose();
    }
    public void Contact(PlayerBodyHitbox hitbox, Collider other, bool entering)
    {
        if (!GetComponent<RotatingArena>().HasStarted) return;
        if (!isActiveAndEnabled || !TrackingReady || other == null) return;
        var obstacle = other.GetComponentInParent<CourseObstacle>();
        if (obstacle == null || !obstacle.isActiveAndEnabled) return;
        if (!contacts.TryGetValue(obstacle, out var state))
        {
            if (!entering) return;
            state = new ContactState(); contacts.Add(obstacle, state);
            state.Pairs.Add((hitbox, other));
            TryRegisterHit(obstacle, hitbox.Region);
            return;
        }
        if (entering) state.Pairs.Add((hitbox, other));
        else if (state.Pairs.Remove((hitbox, other)) && state.Pairs.Count == 0) state.ClearTime = clock;
    }
    public bool TryRegisterHit(CourseObstacle obstacle, PlayerBodyRegion region)
    {
        if (!isActiveAndEnabled || IsPaused || obstacle == null || !obstacle.isActiveAndEnabled) return false;
        pauseRemaining = pauseSeconds;
        wasPaused = true;
            flash?.Flash();
            audioSource?.Play();
        ResetJogging();
        return true;
    }
    void ResetJogging() { if (avatar != null && avatar.TryGetComponent<VRJogAnimator>(out var jog)) jog.ResetForCoursePause(); }
    void OnDisable()
    {
        pauseRemaining = 0f; TrackingReady = false; wasPaused = false;
        contacts.Clear(); flash?.Clear();
        if (proxyRoot != null) proxyRoot.SetActive(false);
    }
    void OnEnable() { if (proxyRoot != null) proxyRoot.SetActive(true); }
    void OnDestroy()
    {
        if (proxyRoot == null) return;
        if (Application.isPlaying) Destroy(proxyRoot); else DestroyImmediate(proxyRoot);
    }
}
