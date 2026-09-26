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
        [SerializeField, Min(1f)] private float maxRange = 150f;
        [SerializeField] private LayerMask anchorLayers = ~0;

        [Header("Movement")]
        [SerializeField, Min(1f)] private float flightSpeed = 32f;
        [SerializeField, Min(1f)] private float acceleration = 80f;
        [SerializeField, Min(0f)] private float airSteering = 12f;
        [SerializeField, Min(0.1f)] private float reelSpeed = 18f;
        [SerializeField, Min(0.5f)] private float minimumRopeLength = 1.5f;
        [SerializeField, Min(0f)] private float launchLift = 11f;

        [Header("Visuals")]
        [SerializeField, Min(0.005f)] private float ropeWidth = 0.065f;
        [SerializeField] private Color ropeColor = new Color(0.08f, 0.1f, 0.13f, 1f);
        [SerializeField] private Color ropeHighlight = new Color(0.9f, 0.96f, 1f, 1f);
        [SerializeField, Min(0f)] private float maximumSag = 4f;
        [SerializeField, Min(1f)] private float missShotRange = 35f;

        private const int RopeSegments = 20;
        private CharacterController _character;
        private FirstPersonController _firstPerson;
        private StarterAssetsInputs _input;
        private PlayerInput _playerInput;
        private Camera _camera;
        private InputAction _grappleAction;
        private InputAction _pullAction;
        private LineRenderer _rope;
        private LineRenderer _ropeCore;
        private GameObject _marker;
        private GameObject _gauntlet;
        private Transform _muzzle;
        private Material _ropeMaterial;
        private Material _coreMaterial;
        private Material _markerMaterial;
        private Material _armorMaterial;
        private Material _trimMaterial;
        private Collider _anchorCollider;
        private Vector3 _localAnchorPoint;
        private Vector3 _localAnchorNormal;
        private Vector3 _velocity;
        private float _ropeLength;
        private bool _attached;
        private bool _pulling;
        private bool _launched;
        private float _missShotTime = -1f;
        private Vector3 _missDirection;
        private bool _aimValid;
        private GUIStyle _reticleStyle;

        private void Awake()
        {
            _character = GetComponent<CharacterController>();
            _firstPerson = GetComponent<FirstPersonController>();
            _input = GetComponent<StarterAssetsInputs>();
            _playerInput = GetComponent<PlayerInput>();
            _camera = Camera.main;
            CreateVisuals();
        }

        private void OnEnable()
        {
            _grappleAction = _playerInput.actions.FindAction("Player/Grapple", false);
            _pullAction = _playerInput.actions.FindAction("Player/Pull", false);
            if (_grappleAction == null || _pullAction == null)
            {
                Debug.LogError("PlayerInput needs Player/Grapple and Player/Pull actions.", this);
                enabled = false;
                return;
            }
            _grappleAction.performed += OnGrapplePerformed;
            _pullAction.performed += OnPullPerformed;
            _pullAction.canceled += OnPullCanceled;
            if (_gauntlet != null) _gauntlet.SetActive(true);
        }

        private void OnDisable()
        {
            if (_grappleAction != null) _grappleAction.performed -= OnGrapplePerformed;
            if (_pullAction != null)
            {
                _pullAction.performed -= OnPullPerformed;
                _pullAction.canceled -= OnPullCanceled;
            }
            Detach();
            if (_gauntlet != null) _gauntlet.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_rope != null) Destroy(_rope.gameObject);
            if (_marker != null) Destroy(_marker);
            if (_gauntlet != null) Destroy(_gauntlet);
            if (_ropeMaterial != null) Destroy(_ropeMaterial);
            if (_coreMaterial != null) Destroy(_coreMaterial);
            if (_markerMaterial != null) Destroy(_markerMaterial);
            if (_armorMaterial != null) Destroy(_armorMaterial);
            if (_trimMaterial != null) Destroy(_trimMaterial);
        }

        private void OnGrapplePerformed(InputAction.CallbackContext context)
        {
            if (_attached)
            {
                Detach();
                return;
            }

            if (!TryFindAnchor(out var selected))
            {
                var camera = GetCamera();
                if (camera == null) return;
                _missDirection = camera.transform.forward;
                _missShotTime = 0f;
                _rope.enabled = true;
                _ropeCore.enabled = true;
                return;
            }

            _missShotTime = -1f;
            _anchorCollider = selected.collider;
            _attached = true;
            _localAnchorPoint = _anchorCollider.transform.InverseTransformPoint(selected.point);
            _localAnchorNormal = _anchorCollider.transform.InverseTransformDirection(selected.normal);
            _ropeLength = Mathf.Max(minimumRopeLength, Vector3.Distance(RopeStart(), selected.point));
            _velocity = _character.velocity;
            _pulling = _pullAction.IsPressed();
            _launched = false;
            _firstPerson.BeginExternalMovement();
            _rope.enabled = true;
            _ropeCore.enabled = true;
            _marker.SetActive(true);
            if (_pulling) Launch();
            UpdateVisuals();
        }

        private void OnPullPerformed(InputAction.CallbackContext context)
        {
            _pulling = true;
            if (_attached) Launch();
        }
        private void OnPullCanceled(InputAction.CallbackContext context) { _pulling = false; }

        private void Launch()
        {
            if (_launched) return;
            _launched = true;
            _velocity.y = Mathf.Max(_velocity.y, launchLift);
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
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= nearest) continue;
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
            if (!_attached)
            {
                if (_missShotTime >= 0f)
                {
                    _missShotTime += Time.deltaTime;
                    if (_missShotTime >= 0.55f)
                    {
                        _missShotTime = -1f;
                        _rope.enabled = false;
                        _ropeCore.enabled = false;
                    }
                }
                return;
            }
            if (_anchorCollider == null || !_anchorCollider.enabled || !_anchorCollider.gameObject.activeInHierarchy)
            {
                Detach();
                return;
            }

            var dt = Time.deltaTime;
            if (dt <= 0f) return;
            var anchor = AnchorPoint();
            var start = RopeStart();
            var toward = anchor - start;
            var distance = toward.magnitude;
            var direction = distance > 0.001f ? toward / distance : Vector3.zero;

            // The cable only pulls when extended. Inside its length it is slack.
            _velocity += Vector3.up * _firstPerson.Gravity * dt;
            if (_input != null)
            {
                var steering = transform.right * _input.move.x + transform.forward * _input.move.y;
                _velocity += Vector3.ClampMagnitude(steering, 1f) * airSteering * dt;
            }
            if (_pulling && distance > minimumRopeLength)
            {
                _velocity += direction * acceleration * dt;
                _velocity = Vector3.ClampMagnitude(_velocity, flightSpeed);
            }

            var before = transform.position;
            var flags = _character.Move(_velocity * dt);
            _velocity = (transform.position - before) / dt;

            // Keep sideways velocity for a swing. An unextended cable never pushes.
            var fromAnchor = RopeStart() - anchor;
            var currentDistance = fromAnchor.magnitude;
            if (_pulling && currentDistance < _ropeLength)
                _ropeLength = Mathf.Max(minimumRopeLength, currentDistance, _ropeLength - reelSpeed * dt);
            if (currentDistance > _ropeLength && currentDistance > 0.001f)
            {
                var outward = fromAnchor / currentDistance;
                before = transform.position;
                _character.Move(-outward * (currentDistance - _ropeLength));
                _velocity += (transform.position - before) / dt;
                var outwardSpeed = Vector3.Dot(_velocity, outward);
                if (outwardSpeed > 0f) _velocity -= outward * outwardSpeed;
            }

            if ((flags & CollisionFlags.Above) != 0 && _velocity.y > 0f) _velocity.y = 0f;
            if ((flags & CollisionFlags.Below) != 0 && _velocity.y < 0f) _velocity.y = 0f;
        }

        private void LateUpdate()
        {
            _aimValid = TryFindAnchor(out _);
            if (_attached && _anchorCollider != null) UpdateVisuals();
            else if (_missShotTime >= 0f) UpdateMissShot();
        }

        private Vector3 AnchorPoint()
        {
            return _anchorCollider.transform.TransformPoint(_localAnchorPoint);
        }

        private Vector3 RopeStart()
        {
            return _muzzle != null ? _muzzle.position : _firstPerson.CinemachineCameraTarget.transform.position;
        }

        private void Detach()
        {
            if (_attached) _firstPerson.EndExternalMovement(_velocity);
            _attached = false;
            _anchorCollider = null;
            _pulling = false;
            _launched = false;
            _missShotTime = -1f;
            if (_rope != null) _rope.enabled = false;
            if (_ropeCore != null) _ropeCore.enabled = false;
            if (_marker != null) _marker.SetActive(false);
        }

        private void CreateVisuals()
        {
            var ropeObject = new GameObject("Anchor Rope");
            ropeObject.transform.SetParent(transform, false);
            _rope = ropeObject.AddComponent<LineRenderer>();
            _rope.useWorldSpace = true;
            _rope.positionCount = RopeSegments + 1;
            _rope.widthMultiplier = ropeWidth;
            _rope.numCapVertices = 4;
            _rope.startColor = ropeColor;
            _rope.endColor = ropeColor;
            _rope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var coreObject = new GameObject("Wire Highlight");
            coreObject.transform.SetParent(ropeObject.transform, false);
            _ropeCore = coreObject.AddComponent<LineRenderer>();
            _ropeCore.useWorldSpace = true;
            _ropeCore.positionCount = RopeSegments + 1;
            _ropeCore.widthMultiplier = ropeWidth * 0.28f;
            _ropeCore.numCapVertices = 4;
            _ropeCore.startColor = ropeHighlight;
            _ropeCore.endColor = ropeHighlight;
            _ropeCore.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                _ropeMaterial = new Material(shader);
                SetMaterialColor(_ropeMaterial, ropeColor);
                _rope.material = _ropeMaterial;
                _coreMaterial = new Material(shader);
                SetMaterialColor(_coreMaterial, ropeHighlight);
                _ropeCore.material = _coreMaterial;
            }
            _rope.enabled = false;
            _ropeCore.enabled = false;

            _marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _marker.name = "Embedded Anchor";
            _marker.layer = 2; // Ignore Raycast
            _marker.transform.localScale = Vector3.one * 0.2f;
            Destroy(_marker.GetComponent<Collider>());
            if (shader != null)
            {
                _markerMaterial = new Material(shader);
                SetMaterialColor(_markerMaterial, ropeHighlight);
                _marker.GetComponent<Renderer>().material = _markerMaterial;
            }
            _marker.SetActive(false);
            CreateGauntlet(shader);
        }

        private void CreateGauntlet(Shader shader)
        {
            var camera = GetCamera();
            if (camera == null) return;
            _gauntlet = new GameObject("Grapple Gauntlet");
            _gauntlet.transform.SetParent(camera.transform, false);
            _gauntlet.layer = 2;
            if (shader != null)
            {
                _armorMaterial = new Material(shader);
                SetMaterialColor(_armorMaterial, new Color(0.075f, 0.085f, 0.11f));
                _trimMaterial = new Material(shader);
                SetMaterialColor(_trimMaterial, new Color(0.8f, 0.62f, 0.32f));
            }

            CreateGauntletPart("Forearm", PrimitiveType.Cylinder,
                new Vector3(0.54f, -0.60f, 0.48f), new Vector3(0.23f, 0.30f, 0.23f),
                Quaternion.Euler(34f, 0f, -38f), _armorMaterial);
            CreateGauntletPart("Wrist Cuff", PrimitiveType.Cylinder,
                new Vector3(0.34f, -0.35f, 0.67f), new Vector3(0.28f, 0.055f, 0.28f),
                Quaternion.Euler(34f, 0f, -38f), _trimMaterial);
            CreateGauntletPart("Glove", PrimitiveType.Cube,
                new Vector3(0.30f, -0.24f, 0.71f), new Vector3(0.22f, 0.18f, 0.27f),
                Quaternion.Euler(12f, -12f, -12f), _armorMaterial);
            CreateGauntletPart("Launcher", PrimitiveType.Cylinder,
                new Vector3(0.28f, -0.19f, 0.79f), new Vector3(0.13f, 0.16f, 0.13f),
                Quaternion.Euler(90f, 0f, 0f), _trimMaterial);
            _muzzle = new GameObject("Wire Muzzle").transform;
            _muzzle.SetParent(_gauntlet.transform, false);
            _muzzle.localPosition = new Vector3(0.28f, -0.19f, 0.92f);
            _muzzle.gameObject.layer = 2;
        }

        private void CreateGauntletPart(string partName, PrimitiveType shape,
            Vector3 position, Vector3 scale, Quaternion rotation, Material material)
        {
            var part = GameObject.CreatePrimitive(shape);
            part.name = partName;
            part.transform.SetParent(_gauntlet.transform, false);
            part.transform.localPosition = position;
            part.transform.localRotation = rotation;
            part.transform.localScale = scale;
            part.layer = 2;
            Destroy(part.GetComponent<Collider>());
            if (material != null) part.GetComponent<Renderer>().sharedMaterial = material;
            part.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }

        private void UpdateVisuals()
        {
            var anchor = AnchorPoint();
            var normal = _anchorCollider.transform.TransformDirection(_localAnchorNormal).normalized;
            var start = RopeStart();
            var end = anchor - normal * 0.035f; // The cable ends inside the hit surface.
            var slack = Mathf.Max(0f, _ropeLength - Vector3.Distance(start, anchor));
            var sag = Mathf.Min(maximumSag, slack * 0.5f);
            for (var i = 0; i <= RopeSegments; i++)
            {
                var t = i / (float)RopeSegments;
                SetRopePoint(i, Vector3.Lerp(start, end, t) +
                    Vector3.down * (4f * t * (1f - t) * sag));
            }
            _marker.transform.position = anchor - normal * 0.025f;
        }

        private void UpdateMissShot()
        {
            var progress = _missShotTime / 0.55f;
            var length = progress < 0.55f ? progress / 0.55f : (1f - progress) / 0.45f;
            var start = RopeStart();
            var end = start + _missDirection * (missShotRange * Mathf.Clamp01(length));
            for (var i = 0; i <= RopeSegments; i++)
                SetRopePoint(i, Vector3.Lerp(start, end, i / (float)RopeSegments));
        }

        private void SetRopePoint(int index, Vector3 point)
        {
            _rope.SetPosition(index, point);
            var camera = GetCamera();
            _ropeCore.SetPosition(index, camera != null
                ? point + (camera.transform.position - point).normalized * 0.008f
                : point);
        }

        private void OnGUI()
        {
            if (!enabled) return;
            var cx = Screen.width * 0.5f;
            var cy = Screen.height * 0.5f;
            var previous = GUI.color;
            GUI.color = _attached || _aimValid
                ? new Color(1f, 0.82f, 0.43f, 0.95f)
                : new Color(0.75f, 0.78f, 0.82f, 0.8f);
            DrawReticleLine(cx - 20f, cy - 1f, 11f, 2f);
            DrawReticleLine(cx + 9f, cy - 1f, 11f, 2f);
            DrawReticleLine(cx - 1f, cy - 20f, 2f, 11f);
            DrawReticleLine(cx - 1f, cy + 9f, 2f, 11f);
            DrawReticleLine(cx - 2f, cy - 2f, 4f, 4f);
            if (_reticleStyle == null)
            {
                _reticleStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                    fontStyle = FontStyle.Bold
                };
            }
            _reticleStyle.normal.textColor = GUI.color;
            GUI.Label(new Rect(cx - 80f, cy + 22f, 160f, 20f),
                _attached ? "WIRE LOCKED" : _aimValid ? "ANCHOR READY" : "NO ANCHOR", _reticleStyle);
            GUI.color = previous;
        }

        private static void DrawReticleLine(float x, float y, float width, float height)
        {
            GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);
        }
    }
}
