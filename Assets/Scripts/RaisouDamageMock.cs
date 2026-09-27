using System;
using UnityEngine;

namespace AltitudeZero
{
    public enum RaisouTargetKind { RaidBoss, Building, Other }

    public readonly struct RaisouHit
    {
        public readonly Collider Collider;
        public readonly RaisouTargetKind Kind;
        public readonly float Damage;
        public readonly Vector3 Point;
        public readonly Vector3 Normal;

        public RaisouHit(Collider collider, RaisouTargetKind kind, float damage,
            Vector3 point, Vector3 normal)
        {
            Collider = collider;
            Kind = kind;
            Damage = damage;
            Point = point;
            Normal = normal;
        }
    }

    /// <summary>命中結果だけを通知する仮のダメージ窓口。耐久値は変更しない。</summary>
    public static class RaisouDamageMock
    {
        public static event Action<RaisouHit> Hit;

        public static void Apply(Collider collider, float damage, Vector3 point, Vector3 normal)
        {
            var kind = collider.GetComponentInParent<RaidBossLocomotion>() != null
                ? RaisouTargetKind.RaidBoss
                : collider.name.StartsWith("bldg_", StringComparison.Ordinal)
                    ? RaisouTargetKind.Building : RaisouTargetKind.Other;
            var result = new RaisouHit(collider, kind, damage, point, normal);
            Hit?.Invoke(result);
            Debug.Log($"雷槍命中: {kind} / {collider.name} / 仮ダメージ {damage:0.##}", collider);
        }
    }
}
