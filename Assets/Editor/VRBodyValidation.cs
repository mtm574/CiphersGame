#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Runs against a private preview scene: it never saves or changes the user's open scene.
public static class VRBodyValidation
{
    private const string ReportPath = "Temp/IKValidation/results.txt";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/VR Body/Validate Calibration")]
    public static void Validate()
    {
        Directory.CreateDirectory("Temp/IKValidation");
        var report = new StringBuilder();
        Scene preview = default;
        try
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before running calibration validation.");
            preview = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BasicScene.unity");
            VRRigSync sync = null;
            foreach (var root in preview.GetRootGameObjects())
                sync = sync != null ? sync : root.GetComponentInChildren<VRRigSync>(true);
            Require(sync != null, "BasicScene has a VRRigSync component");
            RunChecks(sync, report);
        }
        catch (Exception error)
        {
            report.AppendLine("FAIL: " + (error.InnerException ?? error));
        }
        finally
        {
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            File.WriteAllText(ReportPath, report.ToString());
            Debug.Log(report.ToString());
        }
    }

    public static void RunChecks(VRRigSync sync, StringBuilder report)
    {
            Animator animator = sync.GetComponent<Animator>();
            animator.Rebind();
            animator.Update(0f);
            sync.GetComponent<UnityEngine.Animations.Rigging.RigBuilder>().Build();
            Require((bool)Invoke(sync, "Initialize"), "Humanoid bones and scene references initialize");
            Transform headset = Field<Transform>(sync, "headTarget");
            var left = Field<VRRigSync.HandBinding>(sync, "leftHand");
            var right = Field<VRRigSync.HandBinding>(sync, "rightHand");
            Vector3 initialScale = sync.transform.localScale;
            Transform origin = headset.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>().transform;

            TestPose(sync, headset, left, right, new Vector3(0f, 1.65f, 0f), Quaternion.identity, report, "standing");
            TestPose(sync, headset, left, right, new Vector3(4f, 1.1f, -3f), Quaternion.Euler(30f, 137f, 15f), report, "seated, translated, turned and tilted");
            origin.SetPositionAndRotation(new Vector3(8f, 0.4f, -2f), Quaternion.Euler(0f, 73f, 0f));
            TestPose(sync, headset, left, right, new Vector3(9f, 1.9f, 3f), Quaternion.Euler(-15f, 260f, 0f), report, "moved/rotated XR Origin");
            Require(Vector3.Distance(initialScale, sync.transform.localScale) < 0.00001f, "Starting height never resizes the avatar");

            Quaternion beforeVertical = sync.transform.rotation;
            headset.rotation = Quaternion.Euler(-90f, 0f, 0f);
            Invoke(sync, "ApplyTrackedPose", true, true, true, 1f / 90f);
            Require(Quaternion.Angle(beforeVertical, sync.transform.rotation) < 0.01f, "Vertical gaze keeps body heading");
            Invoke(sync, "ApplyTrackedPose", false, false, false, 1f / 90f);
            Require(left.constraint.weight == 0f && right.constraint.weight == 0f, "Lost tracking disables arm constraints");
            TestPose(sync, headset, left, right, new Vector3(-2f, 1.3f, 5f), Quaternion.Euler(0f, 35f, 0f), report, "tracking recovery/recenter");
            report.AppendLine("PASS: scale preservation, gaze fallback, tracking loss/recovery.");
            report.AppendLine("PASS: all calibration checks. Physical controller fit still requires a headset test.");
    }

    private static void TestPose(VRRigSync sync, Transform headset, VRRigSync.HandBinding left,
        VRRigSync.HandBinding right, Vector3 position, Quaternion rotation, StringBuilder report, string label)
    {
        sync.Recalibrate();
        headset.SetPositionAndRotation(position, rotation);
        Quaternion yaw = Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);
        left.controller.SetPositionAndRotation(position + yaw * new Vector3(-0.25f, -0.35f, 0.25f),
            yaw * Quaternion.LookRotation(Vector3.right, Vector3.back));
        right.controller.SetPositionAndRotation(position + yaw * new Vector3(0.25f, -0.35f, 0.25f),
            yaw * Quaternion.LookRotation(Vector3.left, Vector3.back));
        Transform origin = headset.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>().transform;
        Vector3 originPosition = origin.position;
        Quaternion originRotation = origin.rotation;
        // Two passes expose accumulated feedback/offset errors as well as initial placement errors.
        Invoke(sync, "ApplyTrackedPose", true, true, true, 0.3f);
        Invoke(sync, "ApplyTrackedPose", true, true, true, 1f / 90f);
        Transform headBone = Field<Transform>(sync, "headBone");
        Vector3 eyes = headBone.position + headBone.rotation * Field<Vector3>(sync, "eyeOffsetInHead");
        float eyeError = Vector3.Distance(eyes, headset.position);
        Require(eyeError < 0.005f, label + ": eyes align with headset; error=" + eyeError);
        float leftError = CheckHand(left, label + " left");
        float rightError = CheckHand(right, label + " right");
        Require(Vector3.Distance(headset.position, position) < 0.00001f && Quaternion.Angle(headset.rotation, rotation) < 0.01f,
            label + ": camera pose unchanged");
        Require(Vector3.Distance(origin.position, originPosition) < 0.00001f && Quaternion.Angle(origin.rotation, originRotation) < 0.01f,
            label + ": XR Origin unchanged");
        report.AppendLine($"PASS {label}: eye error {eyeError * 1000f:F2} mm; left/right wrist error {leftError * 1000f:F2}/{rightError * 1000f:F2} mm.");
    }

    private static float CheckHand(VRRigSync.HandBinding binding, string label)
    {
        Transform wrist = binding.constraint.data.tip;
        Transform target = binding.constraint.data.target;
        float error = Vector3.Distance(wrist.position, target.position);
        Require(error < 0.005f, label + ": solved wrist reaches target; error=" + error);
        Require(Quaternion.Angle(wrist.rotation, target.rotation) < 0.5f, label + ": wrist rotation matches target");
        // The solved palm centre, rather than the wrist, should meet the controller grip.
        Quaternion grip = wrist.rotation * Quaternion.Inverse(binding.wristRotationInGrip);
        Vector3 palm = wrist.position - grip * binding.wristPositionInGrip;
        Require(Vector3.Distance(palm, binding.controller.position) < 0.005f, label + ": palm matches grip position");
        return error;
    }

    private static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, PrivateInstance).GetValue(obj);
    private static object Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, PrivateInstance).Invoke(obj, args);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
