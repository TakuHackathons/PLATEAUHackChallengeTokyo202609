using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AltitudeZero
{
    [RequireComponent(typeof(CharacterController), typeof(FirstPersonController), typeof(PlayerInput))]
    public sealed class AnchorGrapple : MonoBehaviour
    {
        [Header("Anchor")]
        [SerializeField, Min(1f)] private float maxRange = 150f;
        [SerializeField] private LayerMask anchorLayers = ~0;
        [SerializeField, Min(0f)] private float surfaceClearance = 0.12f;

        [Header("Flight")]
        [Tooltip("Maximum travel speed toward the anchor, in meters per second.")]
        [SerializeField, Min(1f)] private float flightSpeed = 32f;
        [Tooltip("How quickly travel speed increases, in meters per second squared.")]
        [SerializeField, Min(1f)] private float acceleration = 80f;
        [SerializeField, Min(0.05f)] private float arrivalDistance = 0.4f;
        [SerializeField, Min(0.1f)] private float maxFlightTime = 8f;

        [Header("Visuals")]
        [SerializeField, Min(0.005f)] private float ropeWidth = 0.04f;
        [SerializeField] private Color ropeColor = new Color(0.9f, 0.95f, 1f);

        private CharacterController _character;
        private FirstPersonController _firstPerson;
        private PlayerInput _playerInput;
        private LineRenderer _rope;
        private GameObject _marker;
        private Material _ropeMaterial;
        private Collider _anchorCollider;
        private Vector3 _localAnchorPoint;
        private Vector3 _localAnchorNormal;
        private float _speed;
        private float _flightTime;
        private float _blockedTime;
        private bool _flying;

        private void Awake()
        {
            _character = GetComponent<CharacterController>();
            _firstPerson = GetComponent<FirstPersonController>();
            _playerInput = GetComponent<PlayerInput>();
            CreateVisuals();
        }

        private void Start()
        {
            if (_playerInput.actions.FindAction("Grapple", false) == null)
            {
                Debug.LogError("The Player input actions need a Grapple action.", this);
                enabled = false;
            }
        }

        private void OnDisable()
        {
            StopFlight();
        }

        private void OnDestroy()
        {
            if (_rope != null) Destroy(_rope.gameObject);
            if (_marker != null) Destroy(_marker);
            if (_ropeMaterial != null) Destroy(_ropeMaterial);
        }

        // PlayerInput uses Send Messages, as configured by the Starter Assets prefab.
        public void OnGrapple(InputValue value)
        {
            if (!enabled || !value.isPressed) return;
            if (_flying)
            {
                StopFlight();
                return;
            }

            var camera = Camera.main;
            if (camera == null) return;

            var ray = new Ray(camera.transform.position, camera.transform.forward);
            var hits = Physics.RaycastAll(ray, maxRange, anchorLayers, QueryTriggerInteraction.Ignore);
            var closest = float.PositiveInfinity;
            RaycastHit selected = default;
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= closest) continue;
                closest = hit.distance;
                selected = hit;
            }
            if (selected.collider == null) return;

            _anchorCollider = selected.collider;
            _localAnchorPoint = _anchorCollider.transform.InverseTransformPoint(selected.point);
            _localAnchorNormal = _anchorCollider.transform.InverseTransformDirection(selected.normal);
            _speed = 0f;
            _flightTime = 0f;
            _blockedTime = 0f;
            _flying = true;
            _firstPerson.BeginExternalMovement();
            _rope.enabled = true;
            _marker.SetActive(true);
            UpdateVisuals();
        }

        private void Update()
        {
            if (!_flying) return;
            if (_anchorCollider == null || !_anchorCollider.enabled || !_anchorCollider.gameObject.activeInHierarchy)
            {
                StopFlight();
                return;
            }

            _flightTime += Time.deltaTime;
            if (_flightTime >= maxFlightTime)
            {
                StopFlight();
                return;
            }

            var anchor = _anchorCollider.transform.TransformPoint(_localAnchorPoint);
            var normal = _anchorCollider.transform.TransformDirection(_localAnchorNormal).normalized;
            // A capsule needs more room from a floor or ceiling than from a wall.
            var radius = _character.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
            var halfHeight = _character.height * transform.lossyScale.y * 0.5f;
            var support = radius + Mathf.Max(0f, halfHeight - radius) * Mathf.Abs(normal.y);
            var centerOffset = transform.TransformVector(_character.center);
            var destination = anchor + normal * (support + surfaceClearance) - centerOffset;
            var toDestination = destination - transform.position;
            if (toDestination.magnitude <= arrivalDistance)
            {
                StopFlight();
                return;
            }

            _speed = Mathf.MoveTowards(_speed, flightSpeed, acceleration * Time.deltaTime);
            var step = Vector3.ClampMagnitude(toDestination, _speed * Time.deltaTime);
            var before = transform.position;
            _character.Move(step);
            _blockedTime = (transform.position - before).sqrMagnitude < 0.000001f
                ? _blockedTime + Time.deltaTime : 0f;
            if ((destination - transform.position).magnitude <= arrivalDistance || _blockedTime > 0.2f)
                StopFlight();
        }

        private void LateUpdate()
        {
            if (_flying) UpdateVisuals();
        }

        private void StopFlight()
        {
            if (_flying) _firstPerson.EndExternalMovement();
            _flying = false;
            _anchorCollider = null;
            if (_rope != null) _rope.enabled = false;
            if (_marker != null) _marker.SetActive(false);
        }

        private void CreateVisuals()
        {
            var ropeObject = new GameObject("Anchor Rope");
            ropeObject.transform.SetParent(transform, false);
            _rope = ropeObject.AddComponent<LineRenderer>();
            _rope.useWorldSpace = true;
            _rope.positionCount = 2;
            _rope.startWidth = ropeWidth;
            _rope.endWidth = ropeWidth;
            _rope.startColor = ropeColor;
            _rope.endColor = ropeColor;
            _rope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                _ropeMaterial = new Material(shader);
                _ropeMaterial.color = ropeColor;
                _rope.material = _ropeMaterial;
            }
            _rope.enabled = false;

            _marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _marker.name = "Anchor Point";
            _marker.transform.localScale = Vector3.one * 0.16f;
            var markerCollider = _marker.GetComponent<Collider>();
            markerCollider.enabled = false;
            Destroy(markerCollider);
            _marker.layer = 2; // Ignore Raycast
            _marker.SetActive(false);
        }

        private void UpdateVisuals()
        {
            var anchor = _anchorCollider.transform.TransformPoint(_localAnchorPoint);
            var eye = _firstPerson.CinemachineCameraTarget.transform.position;
            _rope.SetPosition(0, eye);
            _rope.SetPosition(1, anchor);
            _marker.transform.position = anchor;
        }
    }
}
