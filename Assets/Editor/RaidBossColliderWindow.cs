using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Humanoid VRM のスキン頂点から、各骨に追従する BoxCollider を作る。</summary>
public sealed class RaidBossColliderWindow : EditorWindow
{
    private const string GeneratedName = "__AutoBodyCollider";
    private const string BodyCollidersName = "BodyColliders";
    private static readonly HumanBodyBones[] TargetBones =
    {
        HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest,
        HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head,
        HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
        HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
        HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot
    };

    private GameObject target;
    [SerializeField, Range(0f, 0.3f)] private float padding = 0.08f;
    [SerializeField, Range(0f, 0.1f)] private float trimPercent = 0.02f;

    [MenuItem("Tools/VRM/レイドボスの Collider を自動生成")]
    private static void Open()
    {
        GetWindow<RaidBossColliderWindow>("VRM Collider").Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("ボスの歩行用 Collider を生成", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("RaidBossVRM.prefab をドラッグ＆ドロップしてください。部位別 BoxCollider は Prefab 直下の BodyColliders で確認・編集できます。地面との接触はルートの BoxCollider が担当します。", MessageType.Info);
        target = (GameObject)EditorGUILayout.ObjectField("VRM / Prefab", target, typeof(GameObject), false);
        padding = EditorGUILayout.Slider("余白の割合", padding, 0f, 0.3f);
        trimPercent = EditorGUILayout.Slider("外れ値の除外率", trimPercent, 0f, 0.1f);

        using (new EditorGUI.DisabledScope(target == null))
        {
            if (GUILayout.Button("Collider と歩行設定を適用", GUILayout.Height(32)))
                Apply();
        }
    }

    private void Apply()
    {
        var sourcePath = AssetDatabase.GetAssetPath(target);
        if (string.IsNullOrEmpty(sourcePath) || !sourcePath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError("Project 内の RaidBossVRM.prefab を指定してください。");
            return;
        }

        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(sourcePath);
            if (root == null) throw new InvalidOperationException("VRM を読み込めませんでした。");

            var animator = root.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                throw new InvalidOperationException("Humanoid Avatar を持つ VRM / Prefab が必要です。");

            var count = Generate(root, animator, padding, trimPercent);
            if (count == 0) throw new InvalidOperationException("計測できるスキン頂点がありませんでした。Collider は保存していません。");

            if (PrefabUtility.SaveAsPrefabAsset(root, sourcePath) == null)
                throw new InvalidOperationException("Prefab を保存できませんでした。");
            AssetDatabase.SaveAssets();
            target = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            EditorGUIUtility.PingObject(target);
            Debug.Log($"{sourcePath}: 部位別 BoxCollider {count} 個と接地用 BoxCollider を設定しました。", target);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            if (root != null) PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static int Generate(GameObject root, Animator animator, float paddingValue, float trimValue)
    {
        var groups = new Dictionary<Transform, List<Vector3>>();
        var targetBones = new HashSet<Transform>();
        var boneNames = new Dictionary<Transform, string>();
        foreach (var humanBone in TargetBones)
        {
            var bone = animator.GetBoneTransform(humanBone);
            if (bone == null) continue;
            targetBones.Add(bone);
            groups[bone] = new List<Vector3>();
            boneNames[bone] = humanBone.ToString();
        }

        foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var source = renderer.sharedMesh;
            if (source == null || !source.isReadable || source.vertexCount == 0) continue;
            var weights = source.boneWeights;
            if (weights.Length != source.vertexCount) continue;
            var baked = new Mesh();
            try
            {
                // VRM の Renderer には縮小 Scale が入る。true で焼いてから TransformPoint で一度だけ Scale を適用する。
                renderer.BakeMesh(baked, true);
                var vertices = baked.vertices;
                var bones = renderer.bones;
                for (var i = 0; i < vertices.Length && i < weights.Length; i++)
                {
                    var weight = weights[i];
                    var boneIndex = DominantBone(weight);
                    if (boneIndex < 0 || boneIndex >= bones.Length) continue;
                    var bone = NearestTargetAncestor(bones[boneIndex], targetBones);
                    if (bone == null) continue;
                    groups[bone].Add(bone.InverseTransformPoint(renderer.transform.TransformPoint(vertices[i])));
                }
            }
            finally { DestroyImmediate(baked); }
        }

        // 自動生成したものだけ差し替える。既存の手動 Collider は消さない。
        foreach (var bone in targetBones)
        {
            var old = bone.Find(GeneratedName);
            if (old != null) DestroyImmediate(old.gameObject);
        }
        var oldContainer = root.transform.Find(BodyCollidersName);
        if (oldContainer != null) DestroyImmediate(oldContainer.gameObject);

        var container = new GameObject(BodyCollidersName);
        container.transform.SetParent(root.transform, false);
        var follower = container.AddComponent<RaidBossHitboxFollower>();
        var generated = new Dictionary<Transform, BoxCollider>();

        var count = 0;
        foreach (var entry in groups)
        {
            if (entry.Value.Count < 16) continue;
            if (!TryFit(entry.Value, paddingValue, trimValue, out var center, out var size)) continue;
            var holder = new GameObject(boneNames[entry.Key]);
            holder.transform.SetParent(container.transform, false);
            holder.transform.position = entry.Key.position;
            holder.transform.rotation = entry.Key.rotation;
            // VRM の骨は縮小された階層にあるため、その実寸 Scale を維持する。
            holder.transform.localScale = new Vector3(
                entry.Key.lossyScale.x / root.transform.lossyScale.x,
                entry.Key.lossyScale.y / root.transform.lossyScale.y,
                entry.Key.lossyScale.z / root.transform.lossyScale.z);
            var collider = holder.AddComponent<BoxCollider>();
            collider.center = center;
            collider.size = size;
            collider.isTrigger = true;
            follower.Add(entry.Key, holder.transform);
            generated[entry.Key] = collider;
            count++;
        }
        if (count > 0) ConfigureLocomotion(root, animator, generated);
        return count;
    }

    private static void ConfigureLocomotion(GameObject root, Animator animator, Dictionary<Transform, BoxCollider> generated)
    {
        var leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        var rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        if (leftFoot == null || rightFoot == null || head == null || hips == null)
            throw new InvalidOperationException("歩行に必要な足・頭・腰の骨が見つかりません。");

        var footY = Mathf.Min(GetBoxBottom(generated, leftFoot, root.transform), GetBoxBottom(generated, rightFoot, root.transform));
        var headY = GetBoxTop(generated, head, root.transform);
        generated.TryGetValue(hips, out var hipBox);
        if (!float.IsFinite(footY) || !float.IsFinite(headY) || headY <= footY || hipBox == null)
            throw new InvalidOperationException("足元と胴体の寸法を計測できませんでした。");

        var hipWorldSize = Vector3.Scale(hipBox.size, hips.lossyScale);
        var radius = Mathf.Max(hipWorldSize.x, hipWorldSize.z) * 0.45f / root.transform.lossyScale.x;
        var capsule = root.GetComponent<CapsuleCollider>();
        if (capsule != null) DestroyImmediate(capsule);
        var oldRootBox = root.GetComponent<BoxCollider>();
        if (oldRootBox != null) DestroyImmediate(oldRootBox);
        var groundingObject = root.transform.Find("GroundingCollider");
        if (groundingObject == null)
        {
            var gameObject = new GameObject("GroundingCollider");
            gameObject.transform.SetParent(root.transform, false);
            groundingObject = gameObject.transform;
        }
        var groundingBox = groundingObject.GetComponent<BoxCollider>();
        if (groundingBox == null) groundingBox = groundingObject.gameObject.AddComponent<BoxCollider>();
        var width = Mathf.Max(0.16f, radius * 2f);
        groundingBox.isTrigger = false;
        groundingBox.size = new Vector3(width, headY - footY, width);
        groundingBox.center = new Vector3(0f, (headY + footY) * 0.5f, 0f);

        var body = root.GetComponent<Rigidbody>();
        if (body == null) body = root.AddComponent<Rigidbody>();
        body.useGravity = true;
        body.isKinematic = false;
        body.constraints |= RigidbodyConstraints.FreezeRotation;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        animator.applyRootMotion = false;

        if (root.GetComponent<RaidBossLocomotion>() == null)
            root.AddComponent<RaidBossLocomotion>();
    }

    private static float GetBoxBottom(Dictionary<Transform, BoxCollider> generated, Transform bone, Transform root)
    {
        return generated.TryGetValue(bone, out var collider) ? root.InverseTransformPoint(collider.bounds.min).y : float.NaN;
    }

    private static float GetBoxTop(Dictionary<Transform, BoxCollider> generated, Transform bone, Transform root)
    {
        return generated.TryGetValue(bone, out var collider) ? root.InverseTransformPoint(collider.bounds.max).y : float.NaN;
    }

    private static int DominantBone(BoneWeight weight)
    {
        var index = weight.boneIndex0;
        var max = weight.weight0;
        if (weight.weight1 > max) { index = weight.boneIndex1; max = weight.weight1; }
        if (weight.weight2 > max) { index = weight.boneIndex2; max = weight.weight2; }
        if (weight.weight3 > max) { index = weight.boneIndex3; max = weight.weight3; }
        return max > 0f ? index : -1;
    }

    private static Transform NearestTargetAncestor(Transform bone, HashSet<Transform> targets)
    {
        for (var current = bone; current != null; current = current.parent)
            if (targets.Contains(current)) return current;
        return null;
    }

    private static bool TryFit(List<Vector3> vertices, float paddingValue, float trimValue, out Vector3 center, out Vector3 size)
    {
        var x = new float[vertices.Count];
        var y = new float[vertices.Count];
        var z = new float[vertices.Count];
        for (var i = 0; i < vertices.Count; i++)
        {
            x[i] = vertices[i].x;
            y[i] = vertices[i].y;
            z[i] = vertices[i].z;
        }
        Array.Sort(x); Array.Sort(y); Array.Sort(z);
        var lower = Mathf.FloorToInt((vertices.Count - 1) * trimValue);
        var upper = vertices.Count - 1 - lower;
        var min = new Vector3(x[lower], y[lower], z[lower]);
        var max = new Vector3(x[upper], y[upper], z[upper]);
        center = (min + max) * 0.5f;
        size = (max - min) * (1f + 2f * paddingValue);
        return size.x > 0.001f && size.y > 0.001f && size.z > 0.001f &&
               float.IsFinite(size.x) && float.IsFinite(size.y) && float.IsFinite(size.z);
    }
}
