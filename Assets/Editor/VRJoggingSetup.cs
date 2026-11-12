#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Builds dedicated assets; never edits the open scene or the original controller.
public static class VRJoggingSetup
{
    public const string Folder = "Assets/VR Body/Animations/Jogging";
    public const string ControllerPath = Folder + "/Spacesuit_Jogging.controller";
    public const string ClipPath = Folder + "/JogLegs.anim";
    public const string MaskPath = Folder + "/LegsOnly.mask";

    [MenuItem("Tools/VR Body/Build Jogging Assets")]
    public static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            throw new InvalidOperationException("Jogging assets already exist; leaving them unchanged.");

        var source = AssetDatabase.LoadAllAssetsAtPath(Folder + "/JogForward.fbx")
            .OfType<AnimationClip>().FirstOrDefault(clip => !clip.name.StartsWith("__"));
        if (source == null || !source.humanMotion)
            throw new InvalidOperationException("Import JogForward.fbx as Humanoid before building jogging assets.");

        // Only muscle curves for the legs survive into the runtime clip. No hips,
        // root, arms, spine, fingers or head curves can disturb the tracked body.
        var legs = new AnimationClip { name = "JogLegs", frameRate = source.frameRate };
        int count = 0;
        foreach (var binding in AnimationUtility.GetCurveBindings(source))
        {
            if (!IsLegMuscle(binding.propertyName) || binding.type != typeof(Animator)) continue;
            AnimationUtility.SetEditorCurve(legs, binding, AnimationUtility.GetEditorCurve(source, binding));
            count++;
        }
        if (count < 8) throw new InvalidOperationException("Expected Humanoid leg muscle curves in the imported jog.");
        var settings = AnimationUtility.GetAnimationClipSettings(source);
        settings.loopTime = true;
        settings.loopBlend = true;
        settings.loopBlendOrientation = true;
        settings.loopBlendPositionY = true;
        settings.loopBlendPositionXZ = true;
        AnimationUtility.SetAnimationClipSettings(legs, settings);
        AssetDatabase.CreateAsset(legs, ClipPath);

        var mask = new AvatarMask { name = "LegsOnly" };
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i,
                i == (int)AvatarMaskBodyPart.LeftLeg || i == (int)AvatarMaskBodyPart.RightLeg);
        AssetDatabase.CreateAsset(mask, MaskPath);

        if (!AssetDatabase.CopyAsset("Assets/Spacesuit_Animator.controller", ControllerPath))
            throw new InvalidOperationException("Could not copy the original Animator controller.");
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        controller.AddParameter("JogWeight", AnimatorControllerParameterType.Float);
        controller.AddParameter("JogRate", AnimatorControllerParameterType.Float);
        var parameters = controller.parameters;
        foreach (var parameter in parameters)
            if (parameter.name == "JogRate") parameter.defaultFloat = 1f;
        controller.parameters = parameters;
        var machine = new AnimatorStateMachine { name = "Lower Body Jog" };
        AssetDatabase.AddObjectToAsset(machine, controller);
        var state = machine.AddState("Rest To Jog");
        machine.defaultState = state;
        // Match the existing controller: disabling defaults locks rig constraints in the animation stream.
        state.writeDefaultValues = true;
        state.speedParameter = "JogRate";
        state.speedParameterActive = true;
        var tree = new BlendTree
        {
            name = "Rest To Jog", blendType = BlendTreeType.Simple1D,
            blendParameter = "JogWeight", useAutomaticThresholds = false
        };
        AssetDatabase.AddObjectToAsset(tree, controller);
        tree.AddChild(AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/VR Body/Animations/VRBodyReferencePose.anim"), 0f);
        tree.AddChild(legs, 1f);
        state.motion = tree;
        controller.AddLayer(new AnimatorControllerLayer
        {
            name = "Lower Body Jog", stateMachine = machine, avatarMask = mask,
            defaultWeight = 1f, blendingMode = AnimatorLayerBlendingMode.Override
        });
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log($"Built leg-only jog ({count} curves, {legs.length:F2}s). Source travel speed: {source.averageSpeed.magnitude:F3} m/s.");
    }

    public static bool IsLegMuscle(string name)
    {
        return (name.StartsWith("Left ", StringComparison.Ordinal) || name.StartsWith("Right ", StringComparison.Ordinal))
            && (name.Contains(" Leg ") || name.Contains(" Foot ") || name.Contains(" Toes "));
    }
}
#endif
