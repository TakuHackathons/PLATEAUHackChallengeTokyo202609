using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AltitudeZero
{
    [RequireComponent(typeof(PlayerInput))]
    public sealed class BladeMelee : MonoBehaviour
    {
        [Header("刀")]
        [SerializeField] private GameObject bladeModel;
        [SerializeField, Min(0.01f)] private float modelScale = 0.3f;
        [SerializeField] private Vector3 heldPosition = new Vector3(0.48f, -0.48f, 0.4f);
        [SerializeField] private Vector3 modelOffset = new Vector3(-0.3f, -0.02f, 0.97f);
        [SerializeField, Min(0.05f)] private float swingDuration = 0.36f;
        [SerializeField, Min(0f)] private float swingInterval = 0.48f;
        [SerializeField, Min(0.1f)] private float bladeLength = 1.7f;
        [SerializeField, Min(0.01f)] private float hitRadius = 0.18f;
        [SerializeField, Min(0f)] private float mockDamage = 10f;
        [SerializeField] private LayerMask hitLayers = Physics.DefaultRaycastLayers;

        private static readonly Quaternion RestRotation = Quaternion.Euler(18f, 24f, -15f);
        private static readonly Quaternion WindupRotation = Quaternion.Euler(20f, 65f, 25f);
        private static readonly Quaternion FollowThroughRotation = Quaternion.Euler(-15f, -55f, -30f);

        private readonly Collider[] nearby = new Collider[128];
        private readonly HashSet<int> struckTargets = new HashSet<int>();
        private PlayerInput playerInput;
        private InputAction slashAction;
        private Transform bladePivot;
        private float elapsed;
        private float nextSwingTime;
        private bool swinging;

        private void Awake()
        {
            playerInput = GetComponent<PlayerInput>();
        }

        private void OnEnable()
        {
            slashAction = playerInput.actions.FindAction("Player/Slash", false);
            if (slashAction == null)
            {
                Debug.LogError("PlayerInput に Player/Slash アクションがありません。", this);
                enabled = false;
                return;
            }
            slashAction.performed += OnSlash;
            if (bladePivot != null) bladePivot.gameObject.SetActive(true);
        }

        private void OnDisable()
        {
            if (slashAction != null) slashAction.performed -= OnSlash;
            swinging = false;
            struckTargets.Clear();
            if (bladePivot != null) bladePivot.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (bladePivot != null) Destroy(bladePivot.gameObject);
        }

        private void OnSlash(InputAction.CallbackContext context) => Swing();

        public void Swing()
        {
            if (!isActiveAndEnabled || Time.time < nextSwingTime || !EnsureBlade()) return;
            nextSwingTime = Time.time + swingInterval;
            elapsed = 0f;
            swinging = true;
            struckTargets.Clear();
        }

        private void Update()
        {
            if (!EnsureBlade() || !swinging) return;
            Step(Time.deltaTime);
        }

        public void Step(float deltaTime)
        {
            if (!swinging || deltaTime <= 0f) return;
            elapsed += deltaTime;
            var phase = Mathf.Clamp01(elapsed / swingDuration);
            bladePivot.localRotation = phase < 0.2f
                ? Quaternion.Slerp(RestRotation, WindupRotation, phase / 0.2f)
                : phase < 0.75f
                    ? Quaternion.Slerp(WindupRotation, FollowThroughRotation, (phase - 0.2f) / 0.55f)
                    : Quaternion.Slerp(FollowThroughRotation, RestRotation, (phase - 0.75f) / 0.25f);

            if (phase >= 0.2f && phase <= 0.75f) CheckBladeHits();
            if (phase < 1f) return;
            swinging = false;
            bladePivot.localRotation = RestRotation;
        }

        private bool EnsureBlade()
        {
            if (bladePivot != null) return true;
            if (bladeModel == null || Camera.main == null) return false;

            var pivotObject = new GameObject("刀の振り支点");
            bladePivot = pivotObject.transform;
            bladePivot.SetParent(Camera.main.transform, false);
            bladePivot.localPosition = heldPosition;
            bladePivot.localRotation = RestRotation;

            var visual = Instantiate(bladeModel, bladePivot);
            visual.name = bladeModel.name;
            visual.transform.localPosition = modelOffset;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one * modelScale;
            foreach (var collider in visual.GetComponentsInChildren<Collider>()) collider.enabled = false;
            return true;
        }

        private void CheckBladeHits()
        {
            var grip = bladePivot.position;
            var tip = grip + bladePivot.forward * bladeLength;
            var count = Physics.OverlapCapsuleNonAlloc(grip + bladePivot.forward * 0.25f,
                tip, hitRadius, nearby, hitLayers, QueryTriggerInteraction.Collide);
            Collider nearest = null;
            var nearestDistance = float.PositiveInfinity;
            for (var i = 0; i < count; i++)
            {
                var target = nearby[i];
                if (target == null || target.transform.IsChildOf(transform) ||
                    target.transform.IsChildOf(bladePivot)) continue;
                var point = target.ClosestPoint(grip);
                var distance = (point - grip).sqrMagnitude;
                if (distance >= nearestDistance) continue;
                nearest = target;
                nearestDistance = distance;
            }
            if (nearest == null) return;

            var key = nearest.attachedRigidbody != null
                ? nearest.attachedRigidbody.GetInstanceID() : nearest.GetInstanceID();
            if (!struckTargets.Add(key)) return;
            var impact = nearest.ClosestPoint(grip + bladePivot.forward * (bladeLength * 0.7f));
            var normal = (grip - impact).normalized;
            BladeImpactEffect.Spawn(impact, normal);
            RaisouDamageMock.Apply(nearest, mockDamage, impact, normal, WeaponKind.Blade);
        }
    }
}
