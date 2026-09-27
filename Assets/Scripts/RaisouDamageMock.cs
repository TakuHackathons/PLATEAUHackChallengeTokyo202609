using System;
using UnityEngine;

namespace AltitudeZero
{
    public enum RaisouTargetKind { RaidBoss, Building, Other }
    public enum WeaponKind { Raisou, Blade }

    public readonly struct RaisouHit
    {
        public readonly Collider Collider;
        public readonly RaisouTargetKind Kind;
        public readonly WeaponKind Weapon;
        public readonly float Damage;
        public readonly Vector3 Point;
        public readonly Vector3 Normal;

        public RaisouHit(Collider collider, RaisouTargetKind kind, float damage,
            Vector3 point, Vector3 normal)
            : this(collider, kind, damage, point, normal, WeaponKind.Raisou) { }

        public RaisouHit(Collider collider, RaisouTargetKind kind, float damage,
            Vector3 point, Vector3 normal, WeaponKind weapon)
        {
            Collider = collider;
            Kind = kind;
            Weapon = weapon;
            Damage = damage;
            Point = point;
            Normal = normal;
        }
    }

    /// <summary>命中結果だけを通知する仮のダメージ窓口。耐久値は変更しない。</summary>
    public static class RaisouDamageMock
    {
        public static event Action<RaisouHit> Hit;

        public static void Apply(Collider collider, float damage, Vector3 point, Vector3 normal,
            WeaponKind weapon = WeaponKind.Raisou)
        {
            var kind = collider.GetComponentInParent<RaidBossLocomotion>() != null
                ? RaisouTargetKind.RaidBoss
                : IsBuilding(collider.transform) ? RaisouTargetKind.Building : RaisouTargetKind.Other;
            var result = new RaisouHit(collider, kind, damage, point, normal, weapon);
            Hit?.Invoke(result);
            Debug.Log($"{(weapon == WeaponKind.Blade ? "刀" : "雷槍")}命中: {kind} / {collider.name} / 仮ダメージ {damage:0.##}", collider);
        }

        private static bool IsBuilding(Transform target)
        {
            for (var current = target; current != null; current = current.parent)
                if (current.name.StartsWith("bldg_", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
