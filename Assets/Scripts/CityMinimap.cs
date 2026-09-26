using UnityEngine;

namespace AltitudeZero
{
    /// <summary>North-up camera map with a street-level collision overlay.</summary>
    [DefaultExecutionOrder(200)]
    public sealed class CityMinimap : MonoBehaviour
    {
        [Header("Targets")]
        [SerializeField] private Transform raidBoss;

        [Header("Map")]
        [SerializeField, Min(40f)] private float mapSpan = 220f;
        [SerializeField, Range(64, 256)] private int collisionResolution = 128;
        [SerializeField, Min(0.2f)] private float collisionRefreshSeconds = 0.8f;
        [SerializeField, Min(0.2f)] private float obstacleSampleHeight = 1.1f;

        private Camera _mapCamera;
        private RenderTexture _view;
        private Texture2D _collisionOverlay;
        private Texture2D _playerArrow;
        private Texture2D _bossDot;
        private Color32[] _pixels;
        private float _streetY;
        private float _cameraY;
        private float _nextCollisionRefresh;
        private float _nextBossSearch;
        private GUIStyle _labelStyle;
        private GUIStyle _smallStyle;
        private bool _ready;

        private void Start()
        {
            FindBoss();
            _streetY = FindStreetHeight();
            CreateCamera();
            CreateTextures();
            _ready = true;
            PositionCamera();
            RebuildCollisionOverlay();
        }

        private void OnDestroy()
        {
            if (_mapCamera != null) Destroy(_mapCamera.gameObject);
            if (_view != null)
            {
                _view.Release();
                Destroy(_view);
            }
            if (_collisionOverlay != null) Destroy(_collisionOverlay);
            if (_playerArrow != null) Destroy(_playerArrow);
            if (_bossDot != null) Destroy(_bossDot);
        }

        private void LateUpdate()
        {
            if (!_ready) return;
            PositionCamera();
            if (raidBoss == null && Time.unscaledTime >= _nextBossSearch)
            {
                FindBoss();
                _nextBossSearch = Time.unscaledTime + 2f;
            }
            if (Time.unscaledTime >= _nextCollisionRefresh)
            {
                RebuildCollisionOverlay();
                _nextCollisionRefresh = Time.unscaledTime + collisionRefreshSeconds;
            }
        }

        private void FindBoss()
        {
            if (raidBoss != null) return;
            var animators = FindObjectsByType<Animator>(FindObjectsSortMode.None);
            foreach (var animator in animators)
            {
                if (animator.transform.IsChildOf(transform) || animator.gameObject.scene != gameObject.scene)
                    continue;
                if (animator.avatar != null && animator.avatar.isHuman)
                {
                    raidBoss = animator.transform.root;
                    return;
                }
            }
            // Imported VRM prefabs can have no avatar until their runtime setup completes.
            var skinnedMeshes = FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
            foreach (var mesh in skinnedMeshes)
            {
                if (mesh.transform.IsChildOf(transform) || mesh.gameObject.scene != gameObject.scene)
                    continue;
                raidBoss = mesh.transform.root;
                return;
            }
        }

        private float FindStreetHeight()
        {
            var origin = transform.position + Vector3.up * 0.2f;
            var hits = Physics.RaycastAll(origin, Vector3.down, 5000f, ~0,
                QueryTriggerInteraction.Ignore);
            var nearest = float.PositiveInfinity;
            var result = transform.position.y;
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform) ||
                    (raidBoss != null && hit.collider.transform.IsChildOf(raidBoss)) ||
                    hit.distance >= nearest)
                    continue;
                nearest = hit.distance;
                result = hit.point.y;
            }
            return result;
        }

        private void CreateCamera()
        {
            var top = transform.position.y;
            var bottom = transform.position.y;
            var renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            foreach (var renderer in renderers)
            {
                if (!renderer.enabled || renderer.transform.IsChildOf(transform)) continue;
                top = Mathf.Max(top, renderer.bounds.max.y);
                bottom = Mathf.Min(bottom, renderer.bounds.min.y);
            }
            _cameraY = top + 25f;
            var objectWithCamera = new GameObject("Minimap Top-Down Camera");
            objectWithCamera.layer = 2;
            _mapCamera = objectWithCamera.AddComponent<Camera>();
            _mapCamera.orthographic = true;
            _mapCamera.orthographicSize = mapSpan * 0.5f;
            _mapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _mapCamera.clearFlags = CameraClearFlags.SolidColor;
            _mapCamera.backgroundColor = new Color(0.07f, 0.10f, 0.14f);
            _mapCamera.nearClipPlane = 0.1f;
            _mapCamera.farClipPlane = Mathf.Max(100f, _cameraY - bottom + 50f);
            _mapCamera.allowHDR = false;
            _mapCamera.allowMSAA = false;
            _mapCamera.cullingMask = ~(1 << 2);
            _view = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGB32);
            _view.name = "City Minimap View";
            _view.Create();
            _mapCamera.targetTexture = _view;
        }

        private void CreateTextures()
        {
            collisionResolution = Mathf.Clamp(collisionResolution, 64, 256);
            _collisionOverlay = new Texture2D(collisionResolution, collisionResolution,
                TextureFormat.RGBA32, false);
            _collisionOverlay.name = "Blocked Streets";
            _collisionOverlay.filterMode = FilterMode.Point;
            _collisionOverlay.wrapMode = TextureWrapMode.Clamp;
            _pixels = new Color32[collisionResolution * collisionResolution];
            _playerArrow = CreateMarker(true);
            _bossDot = CreateMarker(false);
        }

        private static Texture2D CreateMarker(bool arrow)
        {
            const int size = 21;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            var pixels = new Color32[size * size];
            var fill = arrow ? new Color32(84, 231, 255, 255)
                : new Color32(255, 92, 222, 255);
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = x - size / 2;
                var dy = y - size / 2;
                var inside = arrow
                    ? dy >= -8 && dy <= 9 && Mathf.Abs(dx) <= (9 - dy) * 0.55f
                    : dx * dx + dy * dy <= 64;
                pixels[y * size + x] = inside ? fill : new Color32(0, 0, 0, 0);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false);
            return texture;
        }

        private void PositionCamera()
        {
            _mapCamera.transform.position = new Vector3(
                transform.position.x, _cameraY, transform.position.z);
            _mapCamera.orthographicSize = mapSpan * 0.5f;
        }

        private void RebuildCollisionOverlay()
        {
            var step = mapSpan / collisionResolution;
            var startX = _mapCamera.transform.position.x - mapSpan * 0.5f;
            var startZ = _mapCamera.transform.position.z - mapSpan * 0.5f;
            var blocked = new Color32(218, 67, 69, 195);
            var open = new Color32(0, 0, 0, 0);
            for (var i = 0; i < _pixels.Length; i++) _pixels[i] = open;

            // Rasterize live collider footprints in the same world rectangle as
            // the orthographic camera. Broad collider bounds are conservative.
            var colliders = FindObjectsByType<Collider>(FindObjectsSortMode.None);
            foreach (var collider in colliders)
            {
                if (!collider.enabled || collider.isTrigger ||
                    collider.transform.IsChildOf(transform) ||
                    (raidBoss != null && collider.transform.IsChildOf(raidBoss)))
                    continue;
                var bounds = collider.bounds;
                var sampleY = _streetY + obstacleSampleHeight;
                if (bounds.max.y <= _streetY + 0.5f ||
                    bounds.min.y > sampleY + 0.48f || bounds.max.y < sampleY - 0.48f)
                    continue;

                var minX = Mathf.Clamp(Mathf.FloorToInt((bounds.min.x - startX) / step), 0, collisionResolution - 1);
                var maxX = Mathf.Clamp(Mathf.FloorToInt((bounds.max.x - startX) / step), 0, collisionResolution - 1);
                var minZ = Mathf.Clamp(Mathf.FloorToInt((bounds.min.z - startZ) / step), 0, collisionResolution - 1);
                var maxZ = Mathf.Clamp(Mathf.FloorToInt((bounds.max.z - startZ) / step), 0, collisionResolution - 1);
                if (bounds.max.x < startX || bounds.min.x > startX + mapSpan ||
                    bounds.max.z < startZ || bounds.min.z > startZ + mapSpan)
                    continue;
                for (var z = minZ; z <= maxZ; z++)
                for (var x = minX; x <= maxX; x++)
                {
                    if (!(collider is BoxCollider))
                    {
                        var point = new Vector3(startX + (x + 0.5f) * step,
                            sampleY, startZ + (z + 0.5f) * step);
                        if ((collider.ClosestPoint(point) - point).sqrMagnitude > step * step * 0.5f)
                            continue;
                    }
                    _pixels[z * collisionResolution + x] = blocked;
                }
            }
            _collisionOverlay.SetPixels32(_pixels);
            _collisionOverlay.Apply(false);
        }

        private void OnGUI()
        {
            if (!_ready || _view == null) return;
            var size = Mathf.Clamp(Mathf.Min(Screen.width * 0.27f, Screen.height * 0.43f),
                170f, 270f);
            var panel = new Rect(Screen.width - size - 24f, 18f, size + 12f, size + 56f);
            var map = new Rect(panel.x + 6f, panel.y + 26f, size, size);
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft
                };
                _smallStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11,
                    alignment = TextAnchor.MiddleLeft
                };
            }

            var oldColor = GUI.color;
            GUI.color = new Color(0.025f, 0.045f, 0.07f, 0.92f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.DrawTexture(map, _view, ScaleMode.StretchToFill, false);
            GUI.DrawTexture(map, _collisionOverlay);
            GUI.color = new Color(0.35f, 0.9f, 1f);
            GUI.Label(new Rect(panel.x + 8f, panel.y + 3f, size, 20f), "CITY MAP  /  NORTH UP", _labelStyle);
            GUI.color = new Color(1f, 0.82f, 0.82f);
            GUI.Label(new Rect(panel.x + 8f, map.yMax + 4f, size, 20f),
                "RED: BLOCKED  CYAN: YOU  PINK: BOSS", _smallStyle);

            var center = new Vector2(map.center.x, map.center.y);
            var oldMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(transform.eulerAngles.y, center);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(center.x - 11f, center.y - 11f, 22f, 22f), _playerArrow);
            GUI.matrix = oldMatrix;

            if (raidBoss != null)
            {
                var delta = raidBoss.position - transform.position;
                var x = Mathf.Clamp(delta.x / mapSpan, -0.46f, 0.46f);
                var y = Mathf.Clamp(delta.z / mapSpan, -0.46f, 0.46f);
                GUI.DrawTexture(new Rect(center.x + x * size - 10f,
                    center.y - y * size - 10f, 20f, 20f), _bossDot);
                GUI.color = Color.white;
                GUI.Label(new Rect(panel.x + 8f, map.yMax + 22f, size, 18f),
                    "BOSS  " + Mathf.RoundToInt(new Vector2(delta.x, delta.z).magnitude) + " m", _smallStyle);
            }
            else
            {
                GUI.color = Color.white;
                GUI.Label(new Rect(panel.x + 8f, map.yMax + 22f, size, 18f),
                    "BOSS NOT FOUND", _smallStyle);
            }
            GUI.color = oldColor;
        }
    }
}
