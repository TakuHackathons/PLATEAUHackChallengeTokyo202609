using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>VRMA の姿勢を Humanoid AnimationClip に焼き、VRM 用 Animator Controller を作る。</summary>
public static class RaidBossAnimatorBuilder
{
    private const string VrmPath = "Assets/Models/VRM/A_I_VOICE_akane_ver1_02.vrm";
    private const string MotionFolder = "Assets/Models/CC0-animation-retarget-vrm/";
    private const string IdlePath = "Assets/Starter Assets/Runtime/ThirdPersonController/Character/Animations/Stand--Idle.anim.fbx";
    private const string OutputFolder = "Assets/Animations/RaidBoss";
    private const string BossPrefabPath = "Assets/Prefabs/RaidBossVRM.prefab";
    // 取り込んだ VRMA は RootT.y=0 だと足が腰の高さ基準になり、地面の下へ入る。
    private const float WalkRootHeight = 1.07f;
    private const float RunRootHeight = 1.11f;

    [MenuItem("Tools/VRM/レイドボスの Animator を作成")]
    public static void Build()
    {
        var vrm = AssetDatabase.LoadAssetAtPath<GameObject>(VrmPath);
        var idle = AssetDatabase.LoadAllAssetsAtPath(IdlePath).OfType<AnimationClip>().FirstOrDefault(x => x.name == "Idle");
        if (vrm == null || idle == null)
            throw new InvalidOperationException("VRM または Idle AnimationClip が見つかりません。");

        EnsureFolder("Assets/Animations");
        EnsureFolder(OutputFolder);
        EnsureFolder("Assets/Prefabs");

        var walk = BakeVrma(MotionFolder + "CC0-walk.vrma", OutputFolder + "/Walk.anim", WalkRootHeight);
        var run = BakeVrma(MotionFolder + "CC0-run.vrma", OutputFolder + "/Run.anim", RunRootHeight);

        var controllerPath = OutputFolder + "/RaidBoss.controller";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        else
        {
            var stateMachine = controller.layers[0].stateMachine;
            foreach (var child in stateMachine.states)
                stateMachine.RemoveState(child.state);
        }

        if (controller.parameters.All(x => x.name != "Speed"))
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        var state = controller.CreateBlendTreeInController("Locomotion", out var tree);
        tree.blendParameter = "Speed";
        tree.blendType = BlendTreeType.Simple1D;
        tree.useAutomaticThresholds = false;
        tree.AddChild(idle, 0f);
        tree.AddChild(walk, 0.5f);
        tree.AddChild(run, 1f);
        controller.layers[0].stateMachine.defaultState = state;

        var hasBossPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefabPath) != null;
        var instance = hasBossPrefab ? PrefabUtility.LoadPrefabContents(BossPrefabPath) : (GameObject)PrefabUtility.InstantiatePrefab(vrm);
        try
        {
            var animator = instance.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                throw new InvalidOperationException("VRM の Humanoid Avatar が見つかりません。");

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            if (PrefabUtility.SaveAsPrefabAsset(instance, BossPrefabPath) == null)
                throw new InvalidOperationException("VRM Prefab を保存できませんでした。");
        }
        finally
        {
            if (hasBossPrefab) PrefabUtility.UnloadPrefabContents(instance);
            else UnityEngine.Object.DestroyImmediate(instance);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("RaidBoss Animator を作成しました: " + controllerPath);
    }

    private static AnimationClip BakeVrma(string sourcePath, string outputPath, float rootHeight)
    {
        var vrma = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        if (vrma == null)
            throw new InvalidOperationException("VRMA が見つかりません: " + sourcePath);

        var source = UnityEngine.Object.Instantiate(vrma);
        try
        {
            var animator = source.GetComponent<Animator>();
            var animation = source.GetComponent<Animation>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman || animation == null || animation.clip == null)
                throw new InvalidOperationException("VRMA の Humanoid AnimationClip を読み込めません: " + sourcePath);

            var sourceClip = animation.clip;
            var frames = Mathf.Max(2, Mathf.CeilToInt(sourceClip.length * 30f) + 1);
            var curves = new AnimationCurve[HumanTrait.MuscleCount];
            for (var i = 0; i < curves.Length; i++) curves[i] = new AnimationCurve();

            using (var handler = new HumanPoseHandler(animator.avatar, source.transform))
            {
                var pose = new HumanPose();
                for (var frame = 0; frame < frames; frame++)
                {
                    var time = Mathf.Min(frame / 30f, sourceClip.length);
                    sourceClip.SampleAnimation(source, time);
                    handler.GetHumanPose(ref pose);
                    for (var i = 0; i < curves.Length; i++)
                        curves[i].AddKey(time, pose.muscles[i]);
                }
            }

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath);
            if (clip == null)
            {
                clip = new AnimationClip { name = System.IO.Path.GetFileNameWithoutExtension(outputPath) };
                AssetDatabase.CreateAsset(clip, outputPath);
            }
            clip.frameRate = 30f;
            clip.legacy = false;

            // 水平方向の移動は後からゲーム側で決める。高さは足が地面に合う値にする。
            SetConstant(clip, "RootT.x", 0f, sourceClip.length);
            SetConstant(clip, "RootT.y", rootHeight, sourceClip.length);
            SetConstant(clip, "RootT.z", 0f, sourceClip.length);
            SetConstant(clip, "RootQ.x", 0f, sourceClip.length);
            SetConstant(clip, "RootQ.y", 0f, sourceClip.length);
            SetConstant(clip, "RootQ.z", 0f, sourceClip.length);
            SetConstant(clip, "RootQ.w", 1f, sourceClip.length);

            for (var i = 0; i < curves.Length; i++)
            {
                var name = HumanTrait.MuscleName[i];
                if (name.Contains(" Stretched") || name.EndsWith(" Spread")) continue;
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), name), curves[i]);
            }

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
        }
    }

    private static void SetConstant(AnimationClip clip, string property, float value, float duration)
    {
        var curve = AnimationCurve.Linear(0f, value, duration, value);
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), property), curve);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
