using System;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Animations.Rigging;
using UnityEngine.Experimental.Animations;
using UnityEngine.Playables;
using UnityEngine.XR;

// Read tracking after Update, place the body and targets, then evaluate IK once.
[DefaultExecutionOrder(10000)]
[RequireComponent(typeof(Animator), typeof(RigBuilder))]
public class VRRigSync : MonoBehaviour
{
    [Serializable]
    public class HandBinding
    {
        public Transform controller;
        public TwoBoneIKConstraint constraint;
        [Tooltip("Fine adjustment in controller space, in metres; not a starting-pose offset.")]
        public Vector3 positionAdjustment;
        [Tooltip("Fine adjustment to the grip orientation, in degrees.")]
        public Vector3 rotationAdjustment;
        [NonSerialized] public Quaternion wristRotationInGrip;
        [NonSerialized] public Vector3 wristPositionInGrip;
        [NonSerialized] public float weight;
    }

    [Header("Tracked Poses")]
    [SerializeField] private Transform headTarget;
    [SerializeField] private HandBinding leftHand = new HandBinding();
    [SerializeField] private HandBinding rightHand = new HandBinding();
    [Header("Body Alignment")]
    [SerializeField, Min(0f)] private float turnSpeed = 5f;
    [Tooltip("Fallback if the model has no eye bones, in anatomical head space, in metres.")]
    [SerializeField] private Vector3 eyeOffsetFromHead = new Vector3(0f, 0.1f, 0.08f);
    [SerializeField, Min(0f)] private float trackingWarmup = 0.25f;

    private Animator bodyAnimator;
    private RigBuilder rigBuilder;
    private XROrigin xrOrigin;
    private Transform headBone;
    private Vector3 neutralHeadLocalPosition;
    private Vector3 eyeOffsetInHead;
    private Quaternion rootRotationInFacing;
    private Quaternion headRotationInFacing;
    private Vector3 referenceScale;
    private DirectorUpdateMode previousUpdateMode;
    private float trackedTime;
    private bool initialized;
    private bool aligned;
    private bool ownsGraph;
    private PlayableGraph basePoseGraph;
    private AnimatorControllerPlayable baseController;
    private AnimationPlayableOutput basePoseOutput;
    private AnimatorControllerParameter[] controllerParameters;

    void Start() { Initialize(); }

    // Capture the un-solved skeleton once. Never calibrate from an already bent IK pose.
    private bool Initialize()
    {
        if (initialized) return PrepareGraph();
        bodyAnimator = GetComponent<Animator>();
        rigBuilder = GetComponent<RigBuilder>();
        xrOrigin = GetComponentInParent<XROrigin>();
        if (headTarget == null || !bodyAnimator.isHuman || !rigBuilder.enabled)
            return Fail("Assign the headset, a valid Humanoid Animator, and an enabled Rig Builder.");
        if (leftHand?.constraint == null || rightHand?.constraint == null)
            return Fail("Assign both arm constraints.");
        bodyAnimator.applyRootMotion = false;
        bodyAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        controllerParameters = bodyAnimator.parameters;
        float leftWeight = leftHand.constraint.weight;
        float rightWeight = rightHand.constraint.weight;
        leftHand.constraint.weight = rightHand.constraint.weight = 0f;
        if (!PrepareGraph()) return false;
        // Measure the pose Unity actually produces after humanoid retargeting, rather than
        // raw FBX transforms, which can differ slightly even for a static reference pose.
        rigBuilder.Evaluate(0f);
        leftHand.constraint.weight = leftWeight;
        rightHand.constraint.weight = rightWeight;
        headBone = bodyAnimator.GetBoneTransform(HumanBodyBones.Head);
        var leftArm = bodyAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        var rightArm = bodyAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        if (headBone == null || leftArm == null || rightArm == null)
            return Fail("The avatar is missing head or arm bones.");
        referenceScale = transform.lossyScale;
        // Anatomical facing comes from shoulder geometry, not arbitrary FBX root axes.
        Vector3 right = Vector3.ProjectOnPlane(rightArm.position - leftArm.position, Vector3.up);
        if (right.sqrMagnitude < 0.000001f)
            return Fail("Cannot determine the avatar's shoulder direction.");
        Quaternion facing = Quaternion.LookRotation(Vector3.Cross(right.normalized, Vector3.up), Vector3.up);
        rootRotationInFacing = Quaternion.Inverse(facing) * transform.rotation;
        headRotationInFacing = Quaternion.Inverse(facing) * headBone.rotation;
        neutralHeadLocalPosition = transform.InverseTransformPoint(headBone.position);
        var leftEye = bodyAnimator.GetBoneTransform(HumanBodyBones.LeftEye);
        var rightEye = bodyAnimator.GetBoneTransform(HumanBodyBones.RightEye);
        Vector3 eyes = leftEye != null && rightEye != null
            ? (leftEye.position + rightEye.position) * 0.5f
            : headBone.position + facing * eyeOffsetFromHead;
        eyeOffsetInHead = Quaternion.Inverse(headBone.rotation) * (eyes - headBone.position);
        if (!CacheHand(leftHand, false) || !CacheHand(rightHand, true))
            return Fail("Assign both controllers and arm constraints, with valid hand and finger bones.");
        initialized = true;
        return PrepareGraph();
    }

    private bool CacheHand(HandBinding hand, bool isRight)
    {
        if (hand == null || hand.controller == null || hand.constraint == null || hand.constraint.data.target == null)
            return false;
        Transform wrist = bodyAnimator.GetBoneTransform(isRight ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
        Transform index = bodyAnimator.GetBoneTransform(isRight ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
        Transform middle = bodyAnimator.GetBoneTransform(isRight ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
        Transform little = bodyAnimator.GetBoneTransform(isRight ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
        if (wrist == null || index == null || middle == null || little == null || hand.constraint.data.tip != wrist)
            return false;
        // Unity OpenXR grip: +Z toward the thumb, +Y back toward the wrist.
        // Derive the wrist correction from finger geometry rather than guessed Euler angles.
        Vector3 fingers = middle.position - wrist.position;
        Vector3 thumbSide = index.position - little.position;
        if (Vector3.Cross(fingers, thumbSide).sqrMagnitude < 0.00000001f)
            return false;
        Quaternion grip = Quaternion.LookRotation(thumbSide, -fingers);
        Vector3 palmCentre = Vector3.Lerp(wrist.position, middle.position, 0.5f);
        hand.wristRotationInGrip = Quaternion.Inverse(grip) * wrist.rotation;
        hand.wristPositionInGrip = Quaternion.Inverse(grip) * (wrist.position - palmCentre);
        hand.weight = hand.constraint.weight;
        return true;
    }

    private bool PrepareGraph()
    {
        if (!rigBuilder.enabled) return false;
        if (!rigBuilder.graph.IsValid() && !rigBuilder.Build())
            return Fail("Rig Builder could not build the avatar constraints.");
        if (!ownsGraph)
        {
            previousUpdateMode = rigBuilder.graph.GetTimeUpdateMode();
            ownsGraph = true;
        }
        rigBuilder.graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        if (!baseController.IsValid() || !basePoseGraph.Equals(rigBuilder.graph))
        {
            if (bodyAnimator.runtimeAnimatorController == null)
                return Fail("Assign the avatar's reference-pose Animator Controller.");
            // Manual IK needs its base animation in the SAME graph. PreviousInputs cannot
            // reliably consume the Animator's separate automatic graph during a late solve.
            basePoseGraph = rigBuilder.graph;
            baseController = AnimatorControllerPlayable.Create(basePoseGraph, bodyAnimator.runtimeAnimatorController);
            basePoseOutput = AnimationPlayableOutput.Create(basePoseGraph, "VR Body Base Pose", bodyAnimator);
            basePoseOutput.SetSourcePlayable(baseController);
            basePoseOutput.SetAnimationStreamSource(AnimationStreamSource.DefaultValues);
            basePoseOutput.SetSortingOrder(0);
        }
        return true;
    }

    private bool Fail(string message)
    {
        Debug.LogError("VR body setup: " + message, this);
        initialized = false;
        enabled = false;
        return false;
    }

    [ContextMenu("Recalibrate Body To Current Headset Pose")]
    public void Recalibrate()
    {
        // Keep anatomical offsets and proportions. No standing pose or T-pose is required.
        aligned = false;
        trackedTime = 0f;
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus) Recalibrate();
    }

    void OnDisable()
    {
        if (basePoseGraph.IsValid() && baseController.IsValid())
        {
            basePoseGraph.DestroyOutput(basePoseOutput);
            basePoseGraph.DestroySubgraph(baseController);
        }
        if (ownsGraph && rigBuilder != null && rigBuilder.graph.IsValid())
            rigBuilder.graph.SetTimeUpdateMode(previousUpdateMode);
        ownsGraph = false;
        if (initialized)
        {
            leftHand.constraint.weight = leftHand.weight;
            rightHand.constraint.weight = rightHand.weight;
        }
        Recalibrate();
    }

    private static bool IsTracked(XRNode node)
    {
        var device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked)
            return false;
        return !device.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState state)
            || (state & (InputTrackingState.Position | InputTrackingState.Rotation))
                == (InputTrackingState.Position | InputTrackingState.Rotation);
    }

    void LateUpdate()
    {
        if (!initialized || !PrepareGraph()) return;
        ApplyTrackedPose(IsTracked(XRNode.Head), IsTracked(XRNode.LeftHand), IsTracked(XRNode.RightHand), Time.deltaTime);
    }

    // Device polling is separate so synthetic poses can regression-test the actual skeleton.
    private void ApplyTrackedPose(bool headTracked, bool leftTracked, bool rightTracked, float deltaTime)
    {
        leftHand.constraint.weight = headTracked && leftTracked ? leftHand.weight : 0f;
        rightHand.constraint.weight = headTracked && rightTracked ? rightHand.weight : 0f;
        if (!headTracked)
        {
            Recalibrate();
            return;
        }
        trackedTime += deltaTime;
        if (trackedTime < trackingWarmup) return;
        Vector3 forward = Vector3.ProjectOnPlane(headTarget.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
        {
            // Looking straight up/down must not produce an arbitrary heading.
            forward = aligned
                ? transform.rotation * Quaternion.Inverse(rootRotationInFacing) * Vector3.forward
                : (xrOrigin != null ? xrOrigin.transform.forward : Vector3.forward);
        }
        Quaternion desiredRotation = Quaternion.LookRotation(forward, Vector3.up) * rootRotationInFacing;
        transform.rotation = aligned
            ? Quaternion.Slerp(transform.rotation, desiredRotation, 1f - Mathf.Exp(-turnSpeed * deltaTime))
            : desiredRotation;
        Quaternion desiredHeadRotation = headTarget.rotation * headRotationInFacing;
        float scale = transform.lossyScale.x / referenceScale.x;
        Vector3 rootToEyes = transform.TransformVector(neutralHeadLocalPosition)
            + desiredHeadRotation * (eyeOffsetInHead * scale);
        // Match the headset in ALL axes, preserving scale even for seated/crouched startup.
        // Only the visual avatar moves; the camera and XR Origin are never repositioned.
        transform.position = headTarget.position - rootToEyes;
        MapHand(leftHand, scale);
        MapHand(rightHand, scale);
        foreach (var parameter in controllerParameters)
        {
            switch (parameter.type)
            {
                case AnimatorControllerParameterType.Float:
                    baseController.SetFloat(parameter.nameHash, bodyAnimator.GetFloat(parameter.nameHash));
                    break;
                case AnimatorControllerParameterType.Bool:
                    baseController.SetBool(parameter.nameHash, bodyAnimator.GetBool(parameter.nameHash));
                    break;
                case AnimatorControllerParameterType.Int:
                    baseController.SetInteger(parameter.nameHash, bodyAnimator.GetInteger(parameter.nameHash));
                    break;
            }
        }
        rigBuilder.Evaluate(deltaTime);
        headBone.rotation = desiredHeadRotation;
        if (!aligned)
            Debug.Log("VR body aligned to headset pose. Scale preserved; controller grips mapped to palms.", this);
        aligned = true;
    }

    private static void MapHand(HandBinding hand, float scale)
    {
        Quaternion grip = hand.controller.rotation * Quaternion.Euler(hand.rotationAdjustment);
        Vector3 position = hand.controller.position + hand.controller.rotation * hand.positionAdjustment
            + grip * (hand.wristPositionInGrip * scale);
        hand.constraint.data.target.SetPositionAndRotation(position, grip * hand.wristRotationInGrip);
    }
}
