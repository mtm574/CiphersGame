#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Synthetic tracking in a private preview scene; never saves the user's scene.
public static class VRJoggingValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/VR Body/Validate Jogging")]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play mode before validating jogging.");
        var report = new StringBuilder();
        Scene preview = default;
        try
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(VRJoggingSetup.ClipPath);
            Require(clip != null && clip.humanMotion && clip.isLooping, "Humanoid jogging loop imports");
            var bindings = AnimationUtility.GetCurveBindings(clip);
            Require(bindings.Length >= 8 && bindings.All(b => b.type == typeof(Animator) && VRJoggingSetup.IsLegMuscle(b.propertyName)),
                "Runtime clip contains only leg muscle curves");
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(VRJoggingSetup.MaskPath);
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                Require(mask.GetHumanoidBodyPartActive((AvatarMaskBodyPart)i) ==
                    (i == (int)AvatarMaskBodyPart.LeftLeg || i == (int)AvatarMaskBodyPart.RightLeg), "Leg-only mask");
            report.AppendLine("PASS: looping Humanoid clip; no root, hips, torso, head, arm or finger curves; leg-only mask.");

            preview = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BasicScene.unity");
            var sync = preview.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<VRRigSync>(true)).Single();
            var jog = sync.GetComponent<VRJogAnimator>();
            Require(jog != null, "Jogging component is wired into BasicScene");
            var animator = sync.GetComponent<Animator>();
            Require(AssetDatabase.GetAssetPath(animator.runtimeAnimatorController) == VRJoggingSetup.ControllerPath,
                "Scene uses dedicated jogging controller");
            VRBodyValidation.RunChecks(sync, report);
            CheckAnimation(sync, animator, report);
            CheckPacing(jog, animator, report);
            report.AppendLine("PASS: all jogging checks. Headset visual tuning remains a manual check.");
        }
        catch (Exception error)
        {
            report.AppendLine("FAIL: " + (error.InnerException ?? error));
            throw;
        }
        finally
        {
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            Directory.CreateDirectory("Library/JoggingValidation");
            File.WriteAllText("Library/JoggingValidation/results.txt", report.ToString());
            Debug.Log(report.ToString());
        }
    }

    private static void CheckAnimation(VRRigSync sync, Animator animator, StringBuilder report)
    {
        var headset = Field<Transform>(sync, "headTarget");
        var origin = headset.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>().transform;
        Vector3 cameraPosition = headset.position, originPosition = origin.position;
        Quaternion cameraRotation = headset.rotation, originRotation = origin.rotation;
        var left = Field<VRRigSync.HandBinding>(sync, "leftHand");
        var right = Field<VRRigSync.HandBinding>(sync, "rightHand");
        animator.SetFloat("JogWeight", 0f);
        animator.SetFloat("JogRate", 1f);
        Invoke(sync, "ApplyTrackedPose", true, true, true, 0.3f);
        var upperBones = new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftHand, HumanBodyBones.RightHand };
        var bones = upperBones.Select(animator.GetBoneTransform).Where(b => b != null).ToArray();
        var rotations = bones.Select(b => b.localRotation).ToArray();
        var positions = bones.Select(b => b.localPosition).ToArray();
        var upperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        Quaternion restLeg = upperLeg.localRotation;
        Quaternion previousLeg = restLeg;
        float excursion = 0f, frameMotion = 0f;
        animator.SetFloat("JogWeight", 1f);
        for (int frame = 0; frame < 180; frame++)
        {
            Invoke(sync, "ApplyTrackedPose", true, true, true, 1f / 90f);
            excursion = Mathf.Max(excursion, Quaternion.Angle(restLeg, upperLeg.localRotation));
            // This test switches weight instantly to inspect the complete gait.
            // Exclude that artificial first-frame jump when checking loop seams.
            if (frame > 0) frameMotion = Mathf.Max(frameMotion, Quaternion.Angle(previousLeg, upperLeg.localRotation));
            previousLeg = upperLeg.localRotation;
            for (int i = 0; i < bones.Length; i++)
            {
                Require(Quaternion.Angle(rotations[i], bones[i].localRotation) < 0.1f, "Upper-body rotation unchanged during jogging: " + bones[i].name);
                Require(Vector3.Distance(positions[i], bones[i].localPosition) < 0.0001f, "Upper-body position unchanged during jogging: " + bones[i].name);
            }
            var head = Field<Transform>(sync, "headBone");
            Vector3 eyes = head.position + head.rotation * Field<Vector3>(sync, "eyeOffsetInHead");
            Require(Vector3.Distance(eyes, headset.position) < 0.005f, "Jogging preserves eye alignment");
            foreach (var hand in new[] { left, right })
            {
                Require(Vector3.Distance(hand.constraint.data.tip.position, hand.constraint.data.target.position) < 0.005f,
                    "Jogging preserves wrist alignment");
                Require(Quaternion.Angle(hand.constraint.data.tip.rotation, hand.constraint.data.target.rotation) < 0.5f,
                    "Jogging preserves wrist rotation");
            }
        }
        Require(excursion > 10f, "Legs visibly animate");
        Require(frameMotion < 20f, $"No large frame discontinuities in jogging loop; largest step={frameMotion:F2} degrees");
        Require(Vector3.Distance(cameraPosition, headset.position) < 0.000001f && Quaternion.Angle(cameraRotation, headset.rotation) < 0.01f,
            "Jogging never moves the camera");
        Require(Vector3.Distance(originPosition, origin.position) < 0.000001f && Quaternion.Angle(originRotation, origin.rotation) < 0.01f,
            "Jogging never moves XR Origin");
        report.AppendLine($"PASS: legs animate ({excursion:F1} degree excursion, {frameMotion:F2} degree maximum frame step); 180 frames preserve hips/upper body, eye/hand alignment, camera and XR Origin.");
    }

    private static void CheckPacing(VRJogAnimator jog, Animator animator, StringBuilder report)
    {
        Invoke(jog, "Awake");
        Set(jog, "referenceSpeed", 3f);
        var headset = Field<Transform>(jog, "headset");
        var arena = Field<RotatingArena>(jog, "arena");
        Set(arena, "rotationSpeed", -10f);
        arena.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Vector3 expected = Vector3.Cross(Vector3.up * (-10f * Mathf.Deg2Rad), new Vector3(0f, 0f, 10f));
        Require(Vector3.Distance(arena.GetSurfaceVelocity(new Vector3(0f, 2f, 10f)), expected) < 0.00001f,
            "Surface velocity uses signed angular speed and radius");
        headset.position = new Vector3(0f, 1.7f, 10f);
        Invoke(jog, "ResetMotion");
        Step(jog, 2);
        Require(animator.GetFloat("JogWeight") > 0f && animator.GetFloat("JogWeight") < 0.1f, "Jogging blends in gradually");
        Step(jog, 178);
        float radius10Rate = animator.GetFloat("JogRate");
        Require(animator.GetFloat("JogWeight") > 0.99f, "Stationary player jogs on moving floor");
        headset.position = new Vector3(0f, 1.7f, 20f);
        Invoke(jog, "ResetMotion");
        Step(jog, 180);
        Require(animator.GetFloat("JogRate") > radius10Rate * 1.9f, "Pace scales with radius");
        Set(arena, "rotationSpeed", -20f);
        Step(jog, 180);
        Require(Mathf.Abs(animator.GetFloat("JogRate") - 2f) < 0.001f, "Playback rate is capped");
        Set(arena, "rotationSpeed", 0f);
        Step(jog, 180);
        Require(animator.GetFloat("JogWeight") < 0.001f, "Stopped floor fades to idle");
        Set(arena, "rotationSpeed", -10f);
        headset.position = new Vector3(0f, 1.7f, 0f);
        Invoke(jog, "ResetMotion");
        Step(jog, 180);
        Require(animator.GetFloat("JogWeight") < 0.001f, "Center of disk is idle");
        Set(jog, "velocitySmoothing", 0f);
        foreach (Vector3 physicalVelocity in new[] { Vector3.left, Vector3.right, Vector3.forward, Vector3.back })
        {
            headset.position = new Vector3(0f, 1.7f, 20f);
            Invoke(jog, "ResetMotion");
            Step(jog, 1);
            headset.position += physicalVelocity / 90f;
            Step(jog, 1);
            var relative = Vector3.ProjectOnPlane(physicalVelocity - arena.GetSurfaceVelocity(headset.position), Vector3.up);
            Require(Vector3.Distance(relative, Field<Vector3>(jog, "relativeVelocity")) < 0.001f,
                "Physical movement combines with floor velocity: " + physicalVelocity);
        }
        Invoke(jog, "UpdateLocomotion", false, 1f / 90f);
        Require(animator.GetFloat("JogWeight") == 0f, "Lost tracking resets jogging");
        Step(jog, 1);
        headset.position += Vector3.right * 5f;
        Step(jog, 1);
        Require(animator.GetFloat("JogWeight") == 0f, "Teleport resets velocity sampling");
        Step(jog, 30);
        Require(animator.GetFloat("JogWeight") > 0.99f, "Tracking resumes without a velocity spike");
        Invoke(jog, "OnDisable");
        Require(animator.GetFloat("JogWeight") == 0f, "Disabling restores rest pose");
        report.AppendLine("PASS: radius/speed pacing, stopped/center idle, four physical movement directions, rate cap, tracking loss/recovery, teleport and disable reset.");
    }

    private static void Step(VRJogAnimator jog, int count)
    {
        for (int i = 0; i < count; i++) Invoke(jog, "UpdateLocomotion", true, 1f / 90f);
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
