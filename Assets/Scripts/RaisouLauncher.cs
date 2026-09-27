using UnityEngine;
using UnityEngine.InputSystem;

namespace AltitudeZero
{
    [RequireComponent(typeof(PlayerInput))]
    public sealed class RaisouLauncher : MonoBehaviour
    {
        [Header("雷槍")]
        [SerializeField] private GameObject projectileModel;
        [SerializeField, Min(0.01f)] private float modelScale = 0.15f;
        [SerializeField, Min(1f)] private float projectileSpeed = 90f;
        [SerializeField, Min(1f)] private float maxRange = 300f;
        [SerializeField, Min(0.01f)] private float collisionRadius = 0.15f;
        [SerializeField, Min(0f)] private float fireInterval = 0.5f;
        [SerializeField, Min(0f)] private float mockDamage = 25f;
        [SerializeField] private LayerMask hitLayers = Physics.DefaultRaycastLayers;

        private PlayerInput playerInput;
        private InputAction fireAction;
        private Camera playerCamera;
        private float nextFireTime;

        private void Awake()
        {
            playerInput = GetComponent<PlayerInput>();
        }

        private void OnEnable()
        {
            fireAction = playerInput.actions.FindAction("Player/FireRaisou", false);
            if (fireAction == null)
            {
                Debug.LogError("PlayerInput に Player/FireRaisou アクションがありません。", this);
                enabled = false;
                return;
            }
            fireAction.performed += OnFire;
        }

        private void OnDisable()
        {
            if (fireAction != null) fireAction.performed -= OnFire;
        }

        private void OnFire(InputAction.CallbackContext context)
        {
            if (Time.time < nextFireTime || projectileModel == null) return;
            playerCamera = playerCamera != null ? playerCamera : Camera.main;
            if (playerCamera == null) return;
            nextFireTime = Time.time + fireInterval;

            var cameraTransform = playerCamera.transform;
            var direction = cameraTransform.forward;
            var aimPoint = cameraTransform.position + direction * maxRange;
            var hits = Physics.RaycastAll(cameraTransform.position, direction, maxRange,
                hitLayers, QueryTriggerInteraction.Collide);
            var nearest = float.PositiveInfinity;
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= nearest) continue;
                nearest = hit.distance;
                aimPoint = hit.point;
            }

            var muzzle = cameraTransform.position + direction * 0.55f
                + cameraTransform.right * 0.22f - cameraTransform.up * 0.18f;
            var flight = aimPoint - muzzle;
            if (flight.sqrMagnitude < 0.01f) flight = direction;
            var projectile = new GameObject("雷槍");
            projectile.transform.SetPositionAndRotation(muzzle, Quaternion.LookRotation(flight.normalized));
            projectile.AddComponent<RaisouProjectile>().Launch(projectileModel, modelScale,
                transform, projectileSpeed, maxRange, collisionRadius, mockDamage, hitLayers);
        }
    }
}
