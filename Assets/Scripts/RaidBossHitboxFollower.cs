using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Prefab 直下に置いた編集可能な当たり判定を、Humanoid の骨に追従させる。</summary>
public sealed class RaidBossHitboxFollower : MonoBehaviour
{
    [Serializable]
    public struct Binding
    {
        public Transform bone;
        public Transform hitbox;
    }

    [SerializeField, HideInInspector] private List<Binding> bindings = new List<Binding>();

    public void Add(Transform bone, Transform hitbox)
    {
        bindings.Add(new Binding { bone = bone, hitbox = hitbox });
    }

    private void LateUpdate()
    {
        foreach (var binding in bindings)
        {
            if (binding.bone == null || binding.hitbox == null) continue;
            binding.hitbox.position = binding.bone.position;
            binding.hitbox.rotation = binding.bone.rotation;
        }
    }
}
