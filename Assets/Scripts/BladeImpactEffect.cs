using UnityEngine;

namespace AltitudeZero
{
    public static class BladeImpactEffect
    {
        private static Material particleMaterial;

        public static void Spawn(Vector3 point, Vector3 normal)
        {
            var effect = new GameObject("刀の命中エフェクト");
            effect.transform.SetPositionAndRotation(point,
                Quaternion.LookRotation(normal.sqrMagnitude > 0.0001f ? normal : Vector3.up));
            var particles = effect.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.25f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.28f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.15f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.55f, 0.85f, 1f), Color.white);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 24;

            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 70f;
            shape.radius = 0.025f;

            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            if (particleMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                    ?? Shader.Find("Sprites/Default");
                if (shader != null) particleMaterial = new Material(shader);
            }
            if (particleMaterial != null) renderer.sharedMaterial = particleMaterial;

            particles.Emit(18);
            Object.Destroy(effect, 1f);
        }
    }
}
