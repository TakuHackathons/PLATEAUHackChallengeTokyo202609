using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace AltitudeZero
{
    // The Canvas and its preview image are saved in SampleScene. This component only
    // refreshes that image from the scene's colliders while the game is running.
    public sealed class CityMinimap : MonoBehaviour
    {
        [SerializeField] private Transform player;
        [SerializeField] private Transform raidBoss;
        [SerializeField] private RawImage mapImage;
        [SerializeField] private RectTransform playerMarker;
        [SerializeField] private RectTransform bossMarker;
        [SerializeField] private Text rangeLabel;
        [SerializeField] private Text bossLabel;
        [SerializeField] private Button rangeButton;
        [SerializeField] private float detailSpan = 120f;
        [SerializeField] private float overviewSpan = 400f;
        [SerializeField] private float sampleHeight = 1.1f;
        [SerializeField] private float refreshInterval = 2f;

        private const int Resolution = 128;
        private const int MeshSampleStride = 4;
        private readonly Color32 open = new Color32(23, 42, 57, 255);
        private readonly Color32 blocked = new Color32(232, 92, 97, 255);
        private Texture2D mapTexture;
        private Color32[] pixels;
        private float nextRefresh;
        private bool overview;
        private float streetHeight;
        private Collider[] mapColliders;

        private void Start()
        {
            if (player == null || mapImage == null) { enabled = false; return; }
            pixels = new Color32[Resolution * Resolution];
            mapTexture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false);
            mapTexture.name = "City Map Collider Footprints";
            mapTexture.filterMode = FilterMode.Point;
            mapTexture.wrapMode = TextureWrapMode.Clamp;
            mapImage.texture = mapTexture;
            if (rangeButton != null) rangeButton.onClick.AddListener(ToggleRange);
            streetHeight = FindStreetHeight();
            mapColliders = FindObjectsByType<Collider>(FindObjectsSortMode.None);
            RefreshMap();
            nextRefresh = Time.unscaledTime + refreshInterval;
        }

        private void OnDestroy()
        {
            if (rangeButton != null) rangeButton.onClick.RemoveListener(ToggleRange);
            if (mapTexture != null) Destroy(mapTexture);
        }

        private void Update()
        {
            if (player == null) return;
            if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame) ToggleRange();
            if (Time.unscaledTime >= nextRefresh)
            {
                RefreshMap();
                nextRefresh = Time.unscaledTime + refreshInterval;
            }
            UpdateMarkers();
        }

        private void ToggleRange()
        {
            overview = !overview;
            RefreshMap();
        }

        private float FindStreetHeight()
        {
            var hits = Physics.RaycastAll(player.position + Vector3.up * 0.2f,
                Vector3.down, 5000f, ~0, QueryTriggerInteraction.Ignore);
            var nearest = float.PositiveInfinity;
            var height = player.position.y;
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(player) ||
                    (raidBoss != null && hit.collider.transform.IsChildOf(raidBoss)) ||
                    hit.distance >= nearest) continue;
                nearest = hit.distance;
                height = hit.point.y;
            }
            return height;
        }

        private void RefreshMap()
        {
            if (mapTexture == null || player == null) return;
            var span = overview ? overviewSpan : detailSpan;
            var startX = player.position.x - span * 0.5f;
            var startZ = player.position.z - span * 0.5f;
            var step = span / Resolution;
            System.Array.Fill(pixels, open);
            foreach (var collider in mapColliders)
            {
                if (collider == null || !collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy ||
                    collider.transform.IsChildOf(player) ||
                    (raidBoss != null && collider.transform.IsChildOf(raidBoss))) continue;
                // 非凸 MeshCollider は ClosestPoint 非対応。呼ぶと毎回警告が出て Play を重くする。
                if (collider is MeshCollider meshCollider && !meshCollider.convex) continue;
                var b = collider.bounds;
                if (b.max.y < streetHeight + sampleHeight - 0.4f ||
                    b.min.y > streetHeight + sampleHeight + 0.4f ||
                    b.max.x < startX || b.min.x > startX + span ||
                    b.max.z < startZ || b.min.z > startZ + span) continue;
                var minX = Mathf.Clamp(Mathf.FloorToInt((b.min.x - startX) / step), 0, Resolution - 1);
                var maxX = Mathf.Clamp(Mathf.FloorToInt((b.max.x - startX) / step), 0, Resolution - 1);
                var minZ = Mathf.Clamp(Mathf.FloorToInt((b.min.z - startZ) / step), 0, Resolution - 1);
                var maxZ = Mathf.Clamp(Mathf.FloorToInt((b.max.z - startZ) / step), 0, Resolution - 1);
                if (collider is BoxCollider)
                {
                    for (var z = minZ; z <= maxZ; z++)
                    for (var x = minX; x <= maxX; x++) pixels[z * Resolution + x] = blocked;
                    continue;
                }

                // Box 以外の対応 Collider は 4x4 画素ごとに代表点を調べる。
                for (var z = minZ; z <= maxZ; z += MeshSampleStride)
                for (var x = minX; x <= maxX; x += MeshSampleStride)
                {
                    var endX = Mathf.Min(x + MeshSampleStride - 1, maxX);
                    var endZ = Mathf.Min(z + MeshSampleStride - 1, maxZ);
                    var point = new Vector3(startX + (x + endX + 1) * 0.5f * step,
                        streetHeight + sampleHeight, startZ + (z + endZ + 1) * 0.5f * step);
                    var maxDistance = step * MeshSampleStride * 0.75f;
                    if ((collider.ClosestPoint(point) - point).sqrMagnitude > maxDistance * maxDistance)
                        continue;
                    for (var fillZ = z; fillZ <= endZ; fillZ++)
                    for (var fillX = x; fillX <= endX; fillX++) pixels[fillZ * Resolution + fillX] = blocked;
                }
            }
            mapTexture.SetPixels32(pixels);
            mapTexture.Apply(false);
            if (rangeLabel != null) rangeLabel.text = overview ? "WIDE  400m   [M]" : "LOCAL  120m   [M]";
            UpdateMarkers();
        }

        private void UpdateMarkers()
        {
            if (playerMarker != null) playerMarker.localRotation = Quaternion.Euler(0, 0, -player.eulerAngles.y);
            if (bossMarker == null || raidBoss == null || mapImage == null) return;
            var delta = raidBoss.position - player.position;
            var span = overview ? overviewSpan : detailSpan;
            var square = mapImage.rectTransform.rect;
            bossMarker.anchoredPosition = new Vector2(
                Mathf.Clamp(delta.x / span, -0.48f, 0.48f) * square.width,
                Mathf.Clamp(delta.z / span, -0.48f, 0.48f) * square.height);
            if (bossLabel != null)
                bossLabel.text = "BOSS  " + Mathf.RoundToInt(new Vector2(delta.x, delta.z).magnitude) + "m";
        }
    }
}
