using DG.Tweening;
using UnityEngine;

public sealed class GlassPourAnimator : MonoBehaviour
{
    [Header("Free Drag")]
    [Tooltip("World Y where the glass body centre hovers while being dragged.")]
    [SerializeField] private float freeHoldHeight = 2.2f;
    [SerializeField] private float freeFollowDamping = 14f;

    [Header("Engagement: Rise Phase")]
    [SerializeField] private float riseDuration = 0.18f;
    [SerializeField] private float riseExtraHeight = 0.35f;
    [SerializeField] private Ease riseEase = Ease.OutQuad;

    [Header("Engagement: Position Phase")]
    [SerializeField] private float positionDuration = 0.42f;
    [SerializeField] private Ease positionEase = Ease.InOutQuad;

    [Header("Pour Pose")]
    [SerializeField] private float pourTiltAngle = 55f;
    [Tooltip("Gap (in world units) between the source's tilted rim edge and the target's rim edge. 0 = lips touch exactly.")]
    [SerializeField] private float pourLipClearance = 0f;

    [Header("Disengagement")]
    [SerializeField] private float disengageDuration = 0.32f;
    [SerializeField] private Ease disengageEase = Ease.InOutQuad;

    [Header("Return To Rest")]
    [SerializeField] private float returnDuration = 0.45f;
    [SerializeField] private Ease returnEase = Ease.OutBack;

    private enum AnimState
    {
        Idle,
        FreeDragging,
        Engaging,
        Pouring,
        Disengaging,
        Returning,
    }

    private AnimState state = AnimState.Idle;
    private Sequence activeSequence;
    private Tween activePositionTween;
    private Tween activeRotationTween;
    private HexSortGlassController activeTarget;
    private HexSortGlassController sourceController;
    private Vector3 freeDragTargetPosition;

    private void Awake()
    {
        sourceController = GetComponent<HexSortGlassController>();
    }

    private float SourceMidLocalY => sourceController != null ? sourceController.BodyMidLocalY : 1.18f;
    private float SourceRimLocalY => sourceController != null ? sourceController.RimLocalY : 2.18f;
    private float SourceRimRadius => sourceController != null ? sourceController.RimRadius : 0.34f;

    public bool IsPouring => state == AnimState.Pouring;
    public bool IsEngaged => state == AnimState.Engaging || state == AnimState.Pouring;
    public bool IsTransitioning =>
        state == AnimState.Engaging ||
        state == AnimState.Disengaging ||
        state == AnimState.Returning;
    public HexSortGlassController ActiveTarget => activeTarget;

    public void BeginHold(Vector3 cursorWorld)
    {
        KillTweens();
        activeTarget = null;
        state = AnimState.FreeDragging;
        freeDragTargetPosition = ComputeFreeDragPosition(cursorWorld);
    }

    public void UpdateCursor(Vector3 cursorWorld)
    {
        if (state != AnimState.FreeDragging)
        {
            return;
        }

        freeDragTargetPosition = ComputeFreeDragPosition(cursorWorld);
    }

    public void EngageTarget(HexSortGlassController target)
    {
        if (target == null)
        {
            return;
        }

        if ((state == AnimState.Engaging || state == AnimState.Pouring) && activeTarget == target)
        {
            return;
        }

        KillTweens();
        activeTarget = target;
        state = AnimState.Engaging;

        Quaternion pourRotation = ComputePourRotation(target);
        Vector3 pourPosition = ComputePourPosition(target, pourRotation);
        Vector3 risePosition = new Vector3(transform.position.x, pourPosition.y + riseExtraHeight, transform.position.z);

        Sequence sequence = DOTween.Sequence();
        sequence.Append(transform.DOMove(risePosition, riseDuration).SetEase(riseEase));
        sequence.Append(transform.DOMove(pourPosition, positionDuration).SetEase(positionEase));
        sequence.Join(transform.DORotateQuaternion(pourRotation, positionDuration).SetEase(positionEase));
        sequence.OnComplete(() =>
        {
            if (state == AnimState.Engaging)
            {
                state = AnimState.Pouring;
            }
        });

        activeSequence = sequence;
    }

    public void DisengageTarget(Vector3 cursorWorld)
    {
        if (state == AnimState.FreeDragging || state == AnimState.Idle)
        {
            return;
        }

        KillTweens();
        activeTarget = null;
        state = AnimState.Disengaging;

        Vector3 freePos = ComputeFreeDragPosition(cursorWorld);
        freeDragTargetPosition = freePos;

        activePositionTween = transform.DOMove(freePos, disengageDuration).SetEase(disengageEase);
        activeRotationTween = transform.DORotateQuaternion(Quaternion.identity, disengageDuration)
            .SetEase(disengageEase)
            .OnComplete(() =>
            {
                if (state == AnimState.Disengaging)
                {
                    state = AnimState.FreeDragging;
                }
            });
    }

    public void ReturnToRest(Vector3 restPosition)
    {
        KillTweens();
        activeTarget = null;
        state = AnimState.Returning;

        activePositionTween = transform.DOMove(restPosition, returnDuration).SetEase(returnEase);
        activeRotationTween = transform.DORotateQuaternion(Quaternion.identity, returnDuration)
            .SetEase(returnEase)
            .OnComplete(() =>
            {
                if (state == AnimState.Returning)
                {
                    state = AnimState.Idle;
                }
            });
    }

    public void CancelAll(Vector3 snapPosition, Quaternion snapRotation)
    {
        KillTweens();
        activeTarget = null;
        state = AnimState.Idle;
        transform.position = snapPosition;
        transform.rotation = snapRotation;
    }

    private Vector3 ComputeFreeDragPosition(Vector3 cursorWorld)
    {
        return new Vector3(cursorWorld.x, freeHoldHeight - SourceMidLocalY, cursorWorld.z);
    }

    private Quaternion ComputePourRotation(HexSortGlassController target)
    {
        Vector3 toTarget = target.transform.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f)
        {
            return Quaternion.identity;
        }

        Vector3 toTargetDir = toTarget.normalized;
        Vector3 axis = Vector3.Cross(Vector3.up, toTargetDir);
        if (axis.sqrMagnitude < 0.0001f)
        {
            return Quaternion.identity;
        }

        return Quaternion.AngleAxis(pourTiltAngle, axis.normalized);
    }

    private Vector3 ComputePourPosition(HexSortGlassController target, Quaternion pourRotation)
    {
        Vector3 toTarget = target.transform.position - transform.position;
        toTarget.y = 0f;
        Vector3 toTargetDir = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector3.right;

        // Land the source's tilted downhill rim point exactly at the target's source-facing rim edge,
        // pushed away by `pourLipClearance` for an optional visible gap.
        Vector3 desiredLipWorld = target.transform.position
            + Vector3.up * target.RimLocalY
            - toTargetDir * (target.RimRadius + pourLipClearance);

        // The source's downhill rim point in pre-rotation space is (rimRadius * toTargetDir, rimLocalY, 0).
        // After pourRotation, this offset relative to source.position lands at the world lip.
        Vector3 lipLocalOffset = (Vector3.up * SourceRimLocalY) + (toTargetDir * SourceRimRadius);
        Vector3 lipWorldOffset = pourRotation * lipLocalOffset;

        // Solve: source.position + lipWorldOffset == desiredLipWorld.
        return desiredLipWorld - lipWorldOffset;
    }

    private void KillTweens()
    {
        if (activeSequence != null && activeSequence.IsActive())
        {
            activeSequence.Kill();
        }

        if (activePositionTween != null && activePositionTween.IsActive())
        {
            activePositionTween.Kill();
        }

        if (activeRotationTween != null && activeRotationTween.IsActive())
        {
            activeRotationTween.Kill();
        }

        activeSequence = null;
        activePositionTween = null;
        activeRotationTween = null;
    }

    private void LateUpdate()
    {
        if (state != AnimState.FreeDragging)
        {
            return;
        }

        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        float lerp = 1f - Mathf.Exp(-freeFollowDamping * deltaTime);
        transform.position = Vector3.Lerp(transform.position, freeDragTargetPosition, lerp);
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.identity, lerp);
    }

    private void OnDisable()
    {
        KillTweens();
    }

    private void OnDestroy()
    {
        KillTweens();
    }
}
