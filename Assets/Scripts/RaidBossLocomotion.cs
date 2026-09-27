using System.Collections.Generic;
using UnityEngine;

/// <summary>ボスのモーション選択と、重力を残した前進を同期する。</summary>
[RequireComponent(typeof(Animator), typeof(Rigidbody))]
[DefaultExecutionOrder(100)]
public sealed class RaidBossLocomotion : MonoBehaviour
{
    public enum Motion { Idle, Walk, Run }

    [SerializeField] private Motion motion = Motion.Idle;
    [SerializeField, Min(0f), Tooltip("モデルが等倍のときの歩行速度 (m/s)。実際の速度はモデルの Scale に比例します。")]
    private float walkSpeed = 2.2f;
    [SerializeField, Min(0f), Tooltip("モデルが等倍のときの走行速度 (m/s)。実際の速度はモデルの Scale に比例します。")]
    private float runSpeed = 3f;
    [SerializeField, Min(0f)] private float groundSnapDistance = 0.15f;
    [SerializeField, Min(0f)] private float groundClearance = 0.01f;
    [SerializeField, Min(0f)] private float motionBlendTime = 0.2f;

    private Animator animator;
    private Rigidbody body;
    private BoxCollider bodyCollider;
    private BoxCollider leftFootCollider;
    private BoxCollider rightFootCollider;
    private BoxCollider headCollider;
    private readonly RaycastHit[] groundHits = new RaycastHit[64];
    private readonly HashSet<Collider> groundColliders = new HashSet<Collider>();
    private static readonly int SpeedId = Animator.StringToHash("Speed");

    private void Awake()
    {
        animator = GetComponent<Animator>();
        body = GetComponent<Rigidbody>();
        bodyCollider = transform.Find("GroundingCollider")?.GetComponent<BoxCollider>();
        var hitboxes = transform.Find("BodyColliders");
        if (hitboxes != null)
        {
            leftFootCollider = hitboxes.Find("LeftFoot")?.GetComponent<BoxCollider>();
            rightFootCollider = hitboxes.Find("RightFoot")?.GetComponent<BoxCollider>();
            headCollider = hitboxes.Find("Head")?.GetComponent<BoxCollider>();
        }
        animator.applyRootMotion = false;
    }

    private void FixedUpdate()
    {
        var grounded = AlignFeetWithGround() || groundColliders.Count > 0;
        var animationSpeed = !grounded ? 0f : motion == Motion.Run ? 1f : motion == Motion.Walk ? 0.5f : 0f;
        animator.SetFloat(SpeedId, animationSpeed, motionBlendTime, Time.fixedDeltaTime);
        var blend = Mathf.Clamp01(animator.GetFloat(SpeedId));
        var localSpeed = blend <= 0.5f
            ? Mathf.Lerp(0f, walkSpeed, blend * 2f)
            : Mathf.Lerp(walkSpeed, runSpeed, (blend - 0.5f) * 2f);
        var metresPerSecond = grounded ? localSpeed * Mathf.Abs(transform.lossyScale.x) : 0f;
        var forward = transform.forward * metresPerSecond;
        body.useGravity = !grounded;
        body.linearVelocity = new Vector3(forward.x, grounded ? 0f : body.linearVelocity.y, forward.z);
    }

    private void LateUpdate()
    {
        // Animator と足の当たり判定が更新された後、描画前にも足元を合わせる。
        if (!AlignFeetWithGround()) return;
        body.useGravity = false;
        var velocity = body.linearVelocity;
        velocity.y = 0f;
        body.linearVelocity = velocity;
    }

    private bool AlignFeetWithGround()
    {
        if (bodyCollider == null || leftFootCollider == null || rightFootCollider == null || headCollider == null)
            return false;

        Physics.SyncTransforms();
        var footBottom = Mathf.Min(leftFootCollider.bounds.min.y, rightFootCollider.bounds.min.y);
        var headTop = headCollider.bounds.max.y;
        var worldScale = Mathf.Abs(transform.lossyScale.y);
        var localBottom = (footBottom - transform.position.y) / worldScale;
        var localTop = (headTop - transform.position.y) / worldScale;
        if (localTop > localBottom)
        {
            // 物理用 Box も現在の姿勢に合わせ、足と胴体の判定がずれないようにする。
            var size = bodyCollider.size;
            size.y = localTop - localBottom;
            bodyCollider.size = size;
            var center = bodyCollider.center;
            center.y = (localTop + localBottom) * 0.5f;
            bodyCollider.center = center;
        }

        var hasSurface = false;
        var correction = float.NegativeInfinity;
        CheckFoot(leftFootCollider, headTop, worldScale, ref hasSurface, ref correction);
        CheckFoot(rightFootCollider, headTop, worldScale, ref hasSurface, ref correction);
        if (!hasSurface || correction < -groundSnapDistance * worldScale) return false;

        // 足の BoxCollider が床に入り込んでいれば、その分だけ Rigidbody 全体を持ち上げる。
        body.position += Vector3.up * correction;
        return true;
    }

    private void CheckFoot(BoxCollider foot, float headTop, float worldScale, ref bool hasSurface, ref float correction)
    {
        var bounds = foot.bounds;
        var origin = new Vector3(bounds.center.x, Mathf.Max(headTop, bounds.max.y) + 0.5f * worldScale, bounds.center.z);
        var count = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits, 100f * worldScale, ~0, QueryTriggerInteraction.Ignore);
        var nearestDistance = float.PositiveInfinity;
        var groundY = 0f;
        for (var i = 0; i < count; i++)
        {
            var hit = groundHits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform) || hit.normal.y < 0.6f || hit.distance >= nearestDistance)
                continue;
            nearestDistance = hit.distance;
            groundY = hit.point.y;
        }
        if (!float.IsFinite(nearestDistance)) return;
        hasSurface = true;
        correction = Mathf.Max(correction, groundY + groundClearance * worldScale - bounds.min.y);
    }

    private void OnCollisionEnter(Collision collision) => CheckGroundContact(collision);

    private void OnCollisionStay(Collision collision) => CheckGroundContact(collision);

    private void OnCollisionExit(Collision collision) => groundColliders.Remove(collision.collider);

    private void CheckGroundContact(Collision collision)
    {
        for (var i = 0; i < collision.contactCount; i++)
        {
            if (collision.GetContact(i).normal.y < 0.6f) continue;
            groundColliders.Add(collision.collider);
            return;
        }
        groundColliders.Remove(collision.collider);
    }

    public void SetMotion(Motion nextMotion) => motion = nextMotion;
}
