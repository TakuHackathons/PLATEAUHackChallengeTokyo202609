using UnityEngine;

namespace AltitudeZero
{
    public sealed class RaisouProjectile : MonoBehaviour
    {
        private Transform owner;
        private float speed;
        private float remainingRange;
        private float radius;
        private float damage;
        private LayerMask hitLayers;
        private bool launched;

        public void Launch(GameObject model, float modelScale, Transform shooter, float flightSpeed,
            float maxRange, float collisionRadius, float mockDamage, LayerMask layers)
        {
            owner = shooter;
            speed = flightSpeed;
            remainingRange = maxRange;
            radius = collisionRadius;
            damage = mockDamage;
            hitLayers = layers;

            var visual = Instantiate(model, transform);
            visual.name = model.name;
            visual.transform.localScale = Vector3.one * modelScale;
            var renderers = visual.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                visual.transform.position += transform.position - bounds.center;
            }

            // FBX 由来の Collider や Animator は弾道判定に使用しない。
            foreach (var collider in visual.GetComponentsInChildren<Collider>()) collider.enabled = false;
            launched = true;
        }

        private void Update() => Step(Time.deltaTime);

        public void Step(float deltaTime)
        {
            if (!launched) return;
            if (deltaTime <= 0f) return;
            var travel = Mathf.Min(speed * deltaTime, remainingRange);
            if (travel <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            var direction = transform.forward;
            var overlaps = Physics.OverlapSphere(transform.position, radius, hitLayers,
                QueryTriggerInteraction.Collide);
            foreach (var collider in overlaps)
            {
                if (IsIgnored(collider)) continue;
                var point = collider.ClosestPoint(transform.position);
                Hit(collider, point, -direction);
                return;
            }

            var hits = Physics.SphereCastAll(transform.position, radius, direction, travel,
                hitLayers, QueryTriggerInteraction.Collide);
            var nearest = float.PositiveInfinity;
            RaycastHit contact = default;
            foreach (var hit in hits)
            {
                if (IsIgnored(hit.collider) || hit.distance >= nearest) continue;
                nearest = hit.distance;
                contact = hit;
            }

            if (contact.collider != null)
            {
                transform.position += direction * contact.distance;
                Hit(contact.collider, contact.point, contact.normal);
                return;
            }

            transform.position += direction * travel;
            remainingRange -= travel;
        }

        private bool IsIgnored(Collider collider) => collider == null ||
            (owner != null && collider.transform.IsChildOf(owner)) ||
            collider.transform.IsChildOf(transform);

        private void Hit(Collider collider, Vector3 point, Vector3 normal)
        {
            RaisouImpactEffect.Spawn(point, normal);
            RaisouDamageMock.Apply(collider, damage, point, normal);
            Destroy(gameObject);
        }
    }
}
