using UnityEngine;

namespace AltitudeZero
{
    public static class RaisouImpactEffect
    {
        private static Material particleMaterial;

        public static void Spawn(Vector3 point, Vector3 normal)
        {
            var effect = new GameObject("雷槍 着弾エフェクト");
            effect.transform.SetPositionAndRotation(point,
                Quaternion.LookRotation(normal.sqrMagnitude > 0.0001f ? normal : Vector3.up));
            var particles = effect.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.35f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.28f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.33f, 0.02f), new Color(1f, 0.92f, 0.3f));
            main.gravityModifier = 0.5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;

            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 65f;
            shape.radius = 0.05f;

            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            if (particleMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                    ?? Shader.Find("Sprites/Default");
                if (shader != null) particleMaterial = new Material(shader);
            }
            if (particleMaterial != null) renderer.sharedMaterial = particleMaterial;

            particles.Emit(28);
            Object.Destroy(effect, 1.5f);
        }
    }
}
