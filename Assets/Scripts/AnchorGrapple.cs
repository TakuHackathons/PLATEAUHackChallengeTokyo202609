using System.Collections.Generic;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AltitudeZero
{
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(CharacterController), typeof(FirstPersonController), typeof(PlayerInput))]
    public sealed class AnchorGrapple : MonoBehaviour
    {
        [Header("Anchor")]
        [SerializeField, Min(1f)] private float maxRange = 800f;
        [SerializeField] private LayerMask anchorLayers = ~0;

        [Header("Movement")]
        [SerializeField, Min(1f)] private float flightSpeed = 32f;
        [SerializeField, Min(1f)] private float acceleration = 80f;
        [SerializeField, Min(0.1f)] private float reelSpeed = 18f;
        [SerializeField, Min(0.5f)] private float minimumRopeLength = 1.5f;

        [Header("Visuals")]
        [SerializeField, Min(0.005f)] private float ropeWidth = 0.065f;
        [SerializeField] private Color ropeColor = new Color(0.08f, 0.1f, 0.13f, 1f);
        [SerializeField] private Color ropeHighlight = new Color(0.9f, 0.96f, 1f, 1f);
        [SerializeField, Min(1f)] private float missShotRange = 200f;

        private const int RopeSegments = 20;
        private const float MissDuration = 0.55f;
        private enum AnchorState { Detached, Attached, Reeling, Holding }

        private sealed class Anchor
        {
            public string name;
            public int side;
            public Collider collider;
            public AnchorState state;
            public Vector3 localPoint;
            public Vector3 localNormal;
            public float ropeLength;
            public int shotOrder;
            public float missTime = -1f;
            public Vector3 missDirection;
            public LineRenderer rope;
            public LineRenderer core;
            public GameObject marker;
            public Collider markerCollider;
            public readonly List<Collider> ignoredTargetColliders = new List<Collider>();
            public GameObject gauntlet;
            public Transform muzzle;
            public bool Attached => state != AnchorState.Detached;
        }

        private readonly Anchor _left = new Anchor { name = "Left", side = -1 };
        private readonly Anchor _right = new Anchor { name = "Right", side = 1 };
        private CharacterController _character;
        private FirstPersonController _firstPerson;
        private PlayerInput _playerInput;
        private Camera _camera;
        private InputAction _leftAction;
        private InputAction _rightAction;
        private InputAction _pullAction;
        private Material _ropeMaterial;
        private Material _leftCoreMaterial;
        private Material _rightCoreMaterial;
        private Material _leftMarkerMaterial;
        private Material _rightMarkerMaterial;
        private Material _armorMaterial;
        private Material _leftTrimMaterial;
        private Material _rightTrimMaterial;
        private Vector3 _velocity;
        private float _currentReelSpeed;
        private bool _pullHeld;
        private int _shotCounter;
        private Anchor _reelTarget;
        private Anchor _latchTarget;
        private Collider _holdCollider;
        private Vector3 _holdLocalPosition;
        private Vector3 _holdWorldPosition;
        private Vector3 _reelDirection;
        private Collider _blockingCollider;
        private bool _reelMoveActive;
        private bool _aimValid;
        private GUIStyle _reticleStyle;

        private void Awake()
        {
            _character = GetComponent<CharacterController>();
            _firstPerson = GetComponent<FirstPersonController>();
            _playerInput = GetComponent<PlayerInput>();
            _camera = Camera.main;
            CreateVisuals();
        }

        private void OnEnable()
        {
            _leftAction = _playerInput.actions.FindAction("Player/LeftGrapple", false);
            _rightAction = _playerInput.actions.FindAction("Player/Grapple", false);
            _pullAction = _playerInput.actions.FindAction("Player/Pull", false);
            if (_leftAction == null || _rightAction == null || _pullAction == null)
            {
                Debug.LogError("PlayerInput needs Player/LeftGrapple, Player/Grapple and Player/Pull actions.", this);
                enabled = false;
                return;
            }
            _leftAction.performed += OnLeftPerformed;
            _rightAction.performed += OnRightPerformed;
            _pullAction.performed += OnPullPerformed;
            _pullAction.canceled += OnPullCanceled;
            if (_left.gauntlet != null) _left.gauntlet.SetActive(true);
            if (_right.gauntlet != null) _right.gauntlet.SetActive(true);
        }

        private void OnDisable()
        {
            if (_leftAction != null) _leftAction.performed -= OnLeftPerformed;
            if (_rightAction != null) _rightAction.performed -= OnRightPerformed;
            if (_pullAction != null)
            {
                _pullAction.performed -= OnPullPerformed;
                _pullAction.canceled -= OnPullCanceled;
            }
            Detach(_left);
            Detach(_right);
            _pullHeld = false;
            if (_left.gauntlet != null) _left.gauntlet.SetActive(false);
            if (_right.gauntlet != null) _right.gauntlet.SetActive(false);
        }

        private void OnDestroy()
        {
            DestroyAnchorVisuals(_left);
            DestroyAnchorVisuals(_right);
            if (_ropeMaterial != null) Destroy(_ropeMaterial);
            if (_leftCoreMaterial != null) Destroy(_leftCoreMaterial);
            if (_rightCoreMaterial != null) Destroy(_rightCoreMaterial);
            if (_leftMarkerMaterial != null) Destroy(_leftMarkerMaterial);
            if (_rightMarkerMaterial != null) Destroy(_rightMarkerMaterial);
            if (_armorMaterial != null) Destroy(_armorMaterial);
            if (_leftTrimMaterial != null) Destroy(_leftTrimMaterial);
            if (_rightTrimMaterial != null) Destroy(_rightTrimMaterial);
        }

        private static void DestroyAnchorVisuals(Anchor anchor)
        {
            if (anchor.rope != null) Destroy(anchor.rope.gameObject);
            if (anchor.marker != null) Destroy(anchor.marker);
            if (anchor.gauntlet != null) Destroy(anchor.gauntlet);
        }

        private void OnLeftPerformed(InputAction.CallbackContext context) => FireOrDetach(_left);
        private void OnRightPerformed(InputAction.CallbackContext context) => FireOrDetach(_right);

        private void FireOrDetach(Anchor anchor)
        {
            if (anchor.Attached)
            {
                Detach(anchor);
                return;
            }
            if (!TryFindAnchor(out var selected))
            {
                var camera = GetCamera();
                if (camera == null) return;
                anchor.missDirection = camera.transform.forward;
                anchor.missTime = 0f;
                anchor.rope.enabled = true;
                anchor.core.enabled = true;
                return;
            }

            anchor.collider = selected.collider;
            anchor.state = AnchorState.Attached;
            anchor.localPoint = selected.collider.transform.InverseTransformPoint(selected.point);
            anchor.localNormal = selected.collider.transform.InverseTransformDirection(selected.normal);
            anchor.ropeLength = Mathf.Max(minimumRopeLength, Vector3.Distance(RopeStart(anchor), selected.point));
            anchor.shotOrder = ++_shotCounter;
            anchor.missTime = -1f;
            anchor.rope.enabled = true;
            anchor.core.enabled = true;
            anchor.marker.transform.position = selected.point - selected.normal * 0.025f;
            anchor.marker.SetActive(true);
            anchor.markerCollider.enabled = true;
            Physics.IgnoreCollision(_character, anchor.markerCollider, true);
            IgnoreAnchorTargetCollisions(anchor);
            if (_pullHeld)
                StartReeling(anchor);
            UpdateVisuals(anchor);
        }

        private void OnPullPerformed(InputAction.CallbackContext context)
        {
            _pullHeld = true;
            var target = MostRecentAnchor();
            if (target == _latchTarget && target != null && target.state == AnchorState.Holding)
                return;
            StartReeling(target);
        }

        private void StartReeling(Anchor target)
        {
            ReleaseHold();
            if (_reelTarget != null && _reelTarget.state == AnchorState.Reeling)
                _reelTarget.state = AnchorState.Attached;
            _reelTarget = target;
            _currentReelSpeed = target != null ? reelSpeed : 0f;
            if (target == null) return;
            if (!_firstPerson.IsExternalMovementActive)
                _firstPerson.BeginExternalMovement();
            _velocity = Vector3.zero;
            target.state = AnchorState.Reeling;
        }

        private void OnPullCanceled(InputAction.CallbackContext context)
        {
            _pullHeld = false;
            if (_reelTarget != null && _reelTarget.state == AnchorState.Reeling)
                _reelTarget.state = AnchorState.Attached;
            _reelTarget = null;
            _currentReelSpeed = 0f;
            if (_latchTarget == null && _firstPerson.IsExternalMovementActive)
                _firstPerson.EndExternalMovement(_velocity);
        }

        private bool AnyAttached => _left.Attached || _right.Attached;

        private Anchor MostRecentAnchor()
        {
            if (!_left.Attached) return _right.Attached ? _right : null;
            if (!_right.Attached) return _left;
            return _left.shotOrder > _right.shotOrder ? _left : _right;
        }

        private bool TryFindAnchor(out RaycastHit selected)
        {
            selected = default;
            var camera = GetCamera();
            if (camera == null) return false;
            var hits = Physics.RaycastAll(camera.transform.position, camera.transform.forward,
                maxRange, anchorLayers, QueryTriggerInteraction.Ignore);
            var nearest = float.PositiveInfinity;
            foreach (var hit in hits)
            {
                if (hit.collider == _left.markerCollider || hit.collider == _right.markerCollider ||
                    hit.collider.transform.IsChildOf(transform) || hit.distance >= nearest) continue;
                nearest = hit.distance;
                selected = hit;
            }
            return selected.collider != null;
        }

        private Camera GetCamera()
        {
            if (_camera == null) _camera = Camera.main;
            return _camera;
        }

        private void Update()
        {
            UpdateMissShot(_left);
            UpdateMissShot(_right);
            ValidateAnchor(_left);
            ValidateAnchor(_right);
            if (!AnyAttached) return;
            var dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (_latchTarget != null && _latchTarget.state == AnchorState.Holding)
            {
                _velocity = Vector3.zero;
                var holdPosition = _holdCollider != null && _holdCollider.enabled
                    ? _holdCollider.transform.TransformPoint(_holdLocalPosition) : _holdWorldPosition;
                var offset = holdPosition - transform.position;
                if (offset.sqrMagnitude > 0.0025f)
                    _character.Move(offset);
                return;
            }
            if (_reelTarget != null && _reelTarget.state == AnchorState.Reeling)
                MoveStraightToAnchor(_reelTarget, dt);
            else
            {
                ConstrainRope(_left);
                ConstrainRope(_right);
                TightenRope(_left);
                TightenRope(_right);
            }
        }

        private void UpdateMissShot(Anchor anchor)
        {
            if (anchor.Attached || anchor.missTime < 0f) return;
            anchor.missTime += Time.deltaTime;
            if (anchor.missTime < MissDuration) return;
            anchor.missTime = -1f;
            anchor.rope.enabled = false;
            anchor.core.enabled = false;
        }

        private void ValidateAnchor(Anchor anchor)
        {
            if (anchor.Attached && (anchor.collider == null || !anchor.collider.enabled ||
                !anchor.collider.gameObject.activeInHierarchy))
                Detach(anchor);
        }

        private Vector3 ReelDestination(Anchor anchor)
        {
            var normal = AnchorNormal(anchor);
            var clearance = Mathf.Lerp(_character.radius, _character.height * 0.5f, Mathf.Abs(normal.y))
                + _character.skinWidth + 0.08f;
            var center = anchor.collider.transform.TransformPoint(anchor.localPoint) + normal * clearance;
            return center - transform.TransformVector(_character.center);
        }

        private void MoveStraightToAnchor(Anchor target, float dt)
        {
            var toDestination = ReelDestination(target) - transform.position;
            var distance = toDestination.magnitude;
            if (distance <= 0.12f)
            {
                LatchTo(target, target.collider);
                return;
            }
            _currentReelSpeed = Mathf.MoveTowards(_currentReelSpeed, flightSpeed, acceleration * dt);
            var displacement = toDestination / distance * Mathf.Min(distance, _currentReelSpeed * dt);
            if (TryFindReelContact(displacement, out var contact))
            {
                var safeDistance = Mathf.Max(0f, contact.distance - 0.005f);
                _character.Move(displacement.normalized * safeDistance);
                TightenRope(target);
                LatchTo(target, contact.collider);
                return;
            }
            var before = transform.position;
            _reelDirection = displacement.normalized;
            _blockingCollider = null;
            _reelMoveActive = true;
            var collisionFlags = _character.Move(displacement);
            _reelMoveActive = false;
            var actualMove = transform.position - before;
            _velocity = actualMove / dt;
            TightenRope(target);
            var other = target == _left ? _right : _left;
            if (other.Attached)
                other.ropeLength = Mathf.Max(other.ropeLength, Vector3.Distance(RopeStart(other), AnchorPoint(other)) + 0.5f);
            if (_blockingCollider != null ||
                (collisionFlags != CollisionFlags.None &&
                 Vector3.Dot(actualMove, _reelDirection) < displacement.magnitude * 0.75f))
                LatchTo(target, _blockingCollider);
            else if ((ReelDestination(target) - transform.position).sqrMagnitude <= 0.12f * 0.12f)
                LatchTo(target, target.collider);
        }

        private bool TryFindReelContact(Vector3 displacement, out RaycastHit contact)
        {
            contact = default;
            var distance = displacement.magnitude;
            if (distance <= 0f) return false;
            var scale = transform.lossyScale;
            var radius = _character.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            var height = Mathf.Max(radius * 2f, _character.height * Mathf.Abs(scale.y));
            var center = transform.TransformPoint(_character.center);
            var halfSegment = height * 0.5f - radius;
            var top = center + transform.up * halfSegment;
            var bottom = center - transform.up * halfSegment;
            var direction = displacement / distance;
            var nearest = float.PositiveInfinity;
            var hits = Physics.CapsuleCastAll(top, bottom, radius, direction, distance,
                ~0, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                var collider = hit.collider;
                if (collider == null || collider == _left.markerCollider || collider == _right.markerCollider ||
                    collider.transform.IsChildOf(transform) ||
                    Physics.GetIgnoreLayerCollision(gameObject.layer, collider.gameObject.layer) ||
                    Vector3.Dot(direction, hit.normal) >= -0.05f || hit.distance >= nearest)
                    continue;
                nearest = hit.distance;
                contact = hit;
            }
            return contact.collider != null;
        }

        private void LatchTo(Anchor target, Collider contact)
        {
            target.state = AnchorState.Holding;
            _latchTarget = target;
            _holdCollider = contact;
            _holdWorldPosition = transform.position;
            _holdLocalPosition = contact != null
                ? contact.transform.InverseTransformPoint(transform.position) : Vector3.zero;
            _reelTarget = null;
            _velocity = Vector3.zero;
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (_reelMoveActive && Vector3.Dot(_reelDirection, hit.normal) < -0.15f &&
                _blockingCollider == null)
                _blockingCollider = hit.collider;
        }

        private void ReleaseHold()
        {
            if (_latchTarget != null && _latchTarget.state == AnchorState.Holding)
                _latchTarget.state = AnchorState.Attached;
            _latchTarget = null;
            _holdCollider = null;
        }

        private void TightenRope(Anchor anchor)
        {
            if (!anchor.Attached) return;
            anchor.ropeLength = Mathf.Max(minimumRopeLength,
                Mathf.Min(anchor.ropeLength, Vector3.Distance(RopeStart(anchor), AnchorPoint(anchor))));
        }

        private void ConstrainRope(Anchor anchor)
        {
            if (!anchor.Attached) return;
            var fromAnchor = RopeStart(anchor) - AnchorPoint(anchor);
            var distance = fromAnchor.magnitude;
            if (distance <= anchor.ropeLength || distance < 0.001f) return;
            _character.Move(-fromAnchor / distance * (distance - anchor.ropeLength));
        }

        private void LateUpdate()
        {
            _aimValid = TryFindAnchor(out _);
            if (_left.Attached) UpdateVisuals(_left);
            else if (_left.missTime >= 0f) DrawMissShot(_left);
            if (_right.Attached) UpdateVisuals(_right);
            else if (_right.missTime >= 0f) DrawMissShot(_right);
        }

        private Vector3 AnchorPoint(Anchor anchor) => anchor.collider.transform.TransformPoint(anchor.localPoint);
        private Vector3 AnchorNormal(Anchor anchor) => anchor.collider.transform.TransformDirection(anchor.localNormal).normalized;
        private Vector3 RopeStart(Anchor anchor) => anchor.muzzle != null
            ? anchor.muzzle.position : _firstPerson.CinemachineCameraTarget.transform.position;

        private static void IgnoreAnchorTargetCollisions(Anchor anchor)
        {
            // 刺さった球と対象の接触解決で、動的 Rigidbody を押し出さない。
            var body = anchor.collider.attachedRigidbody;
            if (body == null)
            {
                IgnoreTargetCollider(anchor, anchor.collider);
                return;
            }

            foreach (var target in body.GetComponentsInChildren<Collider>())
            {
                if (target.attachedRigidbody == body)
                    IgnoreTargetCollider(anchor, target);
            }
        }

        private static void IgnoreTargetCollider(Anchor anchor, Collider target)
        {
            if (target == null || target == anchor.markerCollider) return;
            Physics.IgnoreCollision(anchor.markerCollider, target, true);
            anchor.ignoredTargetColliders.Add(target);
        }

        private void Detach(Anchor anchor)
        {
            if (!anchor.Attached)
            {
                anchor.missTime = -1f;
                if (anchor.rope != null) anchor.rope.enabled = false;
                if (anchor.core != null) anchor.core.enabled = false;
                return;
            }
            var wasReelTarget = _reelTarget == anchor;
            var wasLatchTarget = _latchTarget == anchor;
            foreach (var target in anchor.ignoredTargetColliders)
            {
                if (target != null && anchor.markerCollider != null)
                    Physics.IgnoreCollision(anchor.markerCollider, target, false);
            }
            anchor.ignoredTargetColliders.Clear();
            anchor.collider = null;
            anchor.state = AnchorState.Detached;
            anchor.missTime = -1f;
            if (anchor.markerCollider != null) anchor.markerCollider.enabled = false;
            if (anchor.marker != null) anchor.marker.SetActive(false);
            if (anchor.rope != null) anchor.rope.enabled = false;
            if (anchor.core != null) anchor.core.enabled = false;
            if (wasReelTarget)
            {
                _reelTarget = null;
                _currentReelSpeed = 0f;
            }
            if (wasLatchTarget)
                ReleaseHold();
            if (AnyAttached)
            {
                if (_pullHeld && (wasReelTarget || wasLatchTarget))
                    StartReeling(MostRecentAnchor());
                else if ((wasReelTarget || wasLatchTarget) && _firstPerson.IsExternalMovementActive)
                    _firstPerson.EndExternalMovement(_velocity);
            }
            else
            {
                if (_firstPerson.IsExternalMovementActive)
                    _firstPerson.EndExternalMovement(_velocity);
                ReleaseHold();
                _velocity = Vector3.zero;
            }
        }

        private void CreateVisuals()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                _ropeMaterial = new Material(shader);
                SetMaterialColor(_ropeMaterial, ropeColor);
                _leftCoreMaterial = new Material(shader);
                SetMaterialColor(_leftCoreMaterial, new Color(0.35f, 0.88f, 1f));
                _rightCoreMaterial = new Material(shader);
                SetMaterialColor(_rightCoreMaterial, ropeHighlight);
                _leftMarkerMaterial = new Material(shader);
                SetMaterialColor(_leftMarkerMaterial, new Color(0.35f, 0.88f, 1f));
                _rightMarkerMaterial = new Material(shader);
                SetMaterialColor(_rightMarkerMaterial, ropeHighlight);
                _armorMaterial = new Material(shader);
                SetMaterialColor(_armorMaterial, new Color(0.075f, 0.085f, 0.11f));
                _leftTrimMaterial = new Material(shader);
                SetMaterialColor(_leftTrimMaterial, new Color(0.35f, 0.88f, 1f));
                _rightTrimMaterial = new Material(shader);
                SetMaterialColor(_rightTrimMaterial, new Color(0.8f, 0.62f, 0.32f));
            }
            CreateAnchorVisuals(_left, shader);
            CreateAnchorVisuals(_right, shader);
        }

        private void CreateAnchorVisuals(Anchor anchor, Shader shader)
        {
            var ropeObject = new GameObject(anchor.name + " Anchor Rope");
            ropeObject.transform.SetParent(transform, false);
            anchor.rope = ropeObject.AddComponent<LineRenderer>();
            anchor.rope.useWorldSpace = true;
            anchor.rope.positionCount = RopeSegments + 1;
            anchor.rope.widthMultiplier = ropeWidth;
            anchor.rope.numCapVertices = 4;
            anchor.rope.startColor = ropeColor;
            anchor.rope.endColor = ropeColor;
            anchor.rope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var coreObject = new GameObject(anchor.name + " Wire Highlight");
            coreObject.transform.SetParent(ropeObject.transform, false);
            anchor.core = coreObject.AddComponent<LineRenderer>();
            anchor.core.useWorldSpace = true;
            anchor.core.positionCount = RopeSegments + 1;
            anchor.core.widthMultiplier = ropeWidth * 0.28f;
            anchor.core.numCapVertices = 4;
            anchor.core.startColor = anchor.side < 0 ? new Color(0.35f, 0.88f, 1f) : ropeHighlight;
            anchor.core.endColor = anchor.core.startColor;
            anchor.core.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (shader != null)
            {
                anchor.rope.material = _ropeMaterial;
                anchor.core.material = anchor.side < 0 ? _leftCoreMaterial : _rightCoreMaterial;
            }
            anchor.rope.enabled = false;
            anchor.core.enabled = false;

            anchor.marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            anchor.marker.name = anchor.name + " Embedded Anchor";
            anchor.marker.layer = 2;
            anchor.marker.transform.localScale = Vector3.one * 0.2f;
            anchor.markerCollider = anchor.marker.GetComponent<Collider>();
            anchor.markerCollider.enabled = false;
            if (shader != null)
                anchor.marker.GetComponent<Renderer>().sharedMaterial = anchor.side < 0
                    ? _leftMarkerMaterial : _rightMarkerMaterial;
            anchor.marker.SetActive(false);
            CreateGauntlet(anchor);
        }

        private void CreateGauntlet(Anchor anchor)
        {
            var camera = GetCamera();
            if (camera == null) return;
            anchor.gauntlet = new GameObject(anchor.name + " Grapple Gauntlet");
            anchor.gauntlet.transform.SetParent(camera.transform, false);
            anchor.gauntlet.layer = 2;
            var side = anchor.side;
            var trim = side < 0 ? _leftTrimMaterial : _rightTrimMaterial;
            CreateGauntletPart(anchor, "Forearm", PrimitiveType.Cylinder,
                new Vector3(side * 0.54f, -0.60f, 0.48f), new Vector3(0.23f, 0.30f, 0.23f),
                Quaternion.Euler(34f, 0f, side * -38f), _armorMaterial);
            CreateGauntletPart(anchor, "Wrist Cuff", PrimitiveType.Cylinder,
                new Vector3(side * 0.34f, -0.35f, 0.67f), new Vector3(0.28f, 0.055f, 0.28f),
                Quaternion.Euler(34f, 0f, side * -38f), trim);
            CreateGauntletPart(anchor, "Glove", PrimitiveType.Cube,
                new Vector3(side * 0.30f, -0.24f, 0.71f), new Vector3(0.22f, 0.18f, 0.27f),
                Quaternion.Euler(12f, side * -12f, side * -12f), _armorMaterial);
            CreateGauntletPart(anchor, "Launcher", PrimitiveType.Cylinder,
                new Vector3(side * 0.28f, -0.19f, 0.79f), new Vector3(0.13f, 0.16f, 0.13f),
                Quaternion.Euler(90f, 0f, 0f), trim);
            anchor.muzzle = new GameObject(anchor.name + " Wire Muzzle").transform;
            anchor.muzzle.SetParent(anchor.gauntlet.transform, false);
            anchor.muzzle.localPosition = new Vector3(side * 0.28f, -0.19f, 0.92f);
            anchor.muzzle.gameObject.layer = 2;
        }

        private static void CreateGauntletPart(Anchor anchor, string partName, PrimitiveType shape,
            Vector3 position, Vector3 scale, Quaternion rotation, Material material)
        {
            var part = GameObject.CreatePrimitive(shape);
            part.name = anchor.name + " " + partName;
            part.transform.SetParent(anchor.gauntlet.transform, false);
            part.transform.localPosition = position;
            part.transform.localRotation = rotation;
            part.transform.localScale = scale;
            part.layer = 2;
            var partCollider = part.GetComponent<Collider>();
            partCollider.enabled = false;
            Destroy(partCollider);
            if (material != null) part.GetComponent<Renderer>().sharedMaterial = material;
            part.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }

        private void UpdateVisuals(Anchor anchor)
        {
            var point = AnchorPoint(anchor);
            var normal = AnchorNormal(anchor);
            var start = RopeStart(anchor);
            var end = point - normal * 0.035f;
            for (var i = 0; i <= RopeSegments; i++)
            {
                var t = i / (float)RopeSegments;
                SetRopePoint(anchor, i, Vector3.Lerp(start, end, t));
            }
            anchor.marker.transform.position = point - normal * 0.025f;
        }

        private void DrawMissShot(Anchor anchor)
        {
            var progress = anchor.missTime / MissDuration;
            var length = progress < 0.55f ? progress / 0.55f : (1f - progress) / 0.45f;
            var start = RopeStart(anchor);
            var end = start + anchor.missDirection * (missShotRange * Mathf.Clamp01(length));
            for (var i = 0; i <= RopeSegments; i++)
                SetRopePoint(anchor, i, Vector3.Lerp(start, end, i / (float)RopeSegments));
        }

        private void SetRopePoint(Anchor anchor, int index, Vector3 point)
        {
            anchor.rope.SetPosition(index, point);
            var camera = GetCamera();
            anchor.core.SetPosition(index, camera != null
                ? point + (camera.transform.position - point).normalized * 0.008f
                : point);
        }

        private void OnGUI()
        {
            if (!enabled) return;
            var cx = Screen.width * 0.5f;
            var cy = Screen.height * 0.5f;
            var previous = GUI.color;
            GUI.color = AnyAttached || _aimValid
                ? new Color(1f, 0.82f, 0.43f, 0.95f)
                : new Color(0.75f, 0.78f, 0.82f, 0.8f);
            DrawReticleLine(cx - 20f, cy - 1f, 11f, 2f);
            DrawReticleLine(cx + 9f, cy - 1f, 11f, 2f);
            DrawReticleLine(cx - 1f, cy - 20f, 2f, 11f);
            DrawReticleLine(cx - 1f, cy + 9f, 2f, 11f);
            DrawReticleLine(cx - 2f, cy - 2f, 4f, 4f);
            if (_reticleStyle == null)
                _reticleStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    fontStyle = FontStyle.Bold
                };
            _reticleStyle.normal.textColor = GUI.color;
            GUI.Label(new Rect(cx - 110f, cy + 22f, 220f, 20f),
                "L: " + (_left.Attached ? "LOCK" : "OPEN") +
                "   R: " + (_right.Attached ? "LOCK" : "OPEN") +
                (_latchTarget != null && _latchTarget.state == AnchorState.Holding ? "   HOLD" : ""),
                _reticleStyle);
            GUI.color = previous;
        }

        private static void DrawReticleLine(float x, float y, float width, float height)
        {
            GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);
        }
    }
}
