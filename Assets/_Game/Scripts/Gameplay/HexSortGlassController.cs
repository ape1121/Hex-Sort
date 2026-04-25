using UnityEngine;

public sealed class HexSortGlassController : MonoBehaviour
{
    private const float RimHeight = 2.18f;
    private const float RimRadius = 0.34f;
    private const float MaxTiltAngle = 45f;
    private const float MidGlassHeight = 1.18f;
    private const float HoldHeight = 2.2f;
    private const float MaxTiltLift = 0.9f;

    private GlassLiquidView liquidView;
    private Renderer highlightRenderer;
    private GlassState state;
    private Vector3 restPosition;
    private Quaternion restRotation;
    private Vector3 desiredPosition;
    private Quaternion desiredRotation;
    private Vector3 previousPosition;
    private Quaternion previousRotation;
    private float currentEngagement;
    private bool isHeld;
    private bool isPouring;

    public int Index { get; private set; }

    public GlassState State => state;

    public void Initialize(int index, int capacity, HexSortMaterialLibrary materials)
    {
        Index = index;
        state = new GlassState(capacity);

        GlassVisualReferences visuals = GlassVisualBuilder.Build(transform, materials);
        highlightRenderer = visuals.HighlightRenderer;

        liquidView = gameObject.AddComponent<GlassLiquidView>();
        liquidView.Initialize(visuals.LiquidRoot, state, materials, capacity);

        CapsuleCollider collider = gameObject.GetComponent<CapsuleCollider>();
        if (collider == null)
        {
            collider = gameObject.AddComponent<CapsuleCollider>();
        }

        collider.center = new Vector3(0f, 1.28f, 0f);
        collider.radius = 0.52f;
        collider.height = 2.85f;

        restPosition = transform.position;
        restRotation = Quaternion.identity;
        desiredPosition = transform.position;
        desiredRotation = restRotation;
        previousPosition = transform.position;
        previousRotation = transform.rotation;

        SetHighlight(GlassHighlightMode.None);
        liquidView.RefreshGeometry();
    }

    public void SetUnits(LiquidColorId[] units)
    {
        state.SetUnits(units);
        liquidView.ClearPreview();
        liquidView.RefreshGeometry();
    }

    public void BeginHold()
    {
        isHeld = true;
        isPouring = false;
        currentEngagement = 0f;
        restPosition = transform.position;
        restRotation = Quaternion.identity;
    }

    public void UpdateHoldPose(Vector3 cursorWorld, HexSortGlassController target, float engagement)
    {
        engagement = Mathf.Clamp01(engagement);
        currentEngagement = engagement;

        Quaternion rotation = Quaternion.identity;
        if (target != null && engagement > 0.001f)
        {
            Vector3 toTarget = target.transform.position - cursorWorld;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                Vector3 toTargetDirection = toTarget.normalized;
                Vector3 tiltAxis = Vector3.Cross(Vector3.up, toTargetDirection);
                if (tiltAxis.sqrMagnitude > 0.0001f)
                {
                    tiltAxis.Normalize();
                    float smoothEngagement = engagement * engagement * (3f - (2f * engagement));
                    float tiltAngle = smoothEngagement * MaxTiltAngle;
                    rotation = Quaternion.AngleAxis(tiltAngle, tiltAxis);
                }
            }
        }

        float tiltLift = engagement * MaxTiltLift;
        Vector3 holdCenterWorld = new Vector3(
            cursorWorld.x,
            HoldHeight + tiltLift,
            cursorWorld.z);

        desiredPosition = holdCenterWorld - (rotation * (Vector3.up * MidGlassHeight));
        desiredRotation = rotation;
    }

    public void EndHold()
    {
        isHeld = false;
        isPouring = false;
        currentEngagement = 0f;
        desiredPosition = restPosition;
        desiredRotation = restRotation;
        liquidView.ClearPreview();
    }

    public void SetHighlight(GlassHighlightMode mode)
    {
        if (highlightRenderer == null || highlightRenderer.sharedMaterial == null)
        {
            return;
        }

        Color color = new Color(0f, 0f, 0f, 0f);
        switch (mode)
        {
            case GlassHighlightMode.Held:
                color = new Color(0.99f, 0.67f, 0.32f, 0.88f);
                break;
            case GlassHighlightMode.ValidTarget:
                color = new Color(0.34f, 0.74f, 1f, 0.50f);
                break;
            case GlassHighlightMode.CandidateTarget:
                color = new Color(0.34f, 0.94f, 0.62f, 0.78f);
                break;
            case GlassHighlightMode.Solved:
                color = new Color(0.47f, 0.92f, 0.66f, 0.58f);
                break;
        }

        RuntimeViewUtility.SetMaterialColor(highlightRenderer.sharedMaterial, color);
    }

    public void SetTransferPreview(LiquidColorId color, float units)
    {
        liquidView.SetPreview(color, units);
    }

    public void ClearTransferPreview()
    {
        liquidView.ClearPreview();
    }

    public bool TryCreateMoveTo(HexSortGlassController target, int maxUnits, out PourMove move)
    {
        if (target == null)
        {
            move = PourMove.Invalid;
            return false;
        }

        return state.TryCreateMoveTo(target.state, Index, target.Index, maxUnits, out move);
    }

    public void ApplyMoveTo(HexSortGlassController target, PourMove move)
    {
        state.ApplyMoveTo(target.state, move);
        liquidView.RefreshGeometry();
        target.liquidView.RefreshGeometry();
    }

    public Vector3 GetReceivePoint(Vector3 sourcePosition)
    {
        Vector3 toSource = sourcePosition - transform.position;
        toSource.y = 0f;
        if (toSource.sqrMagnitude < 0.0001f)
        {
            toSource = Vector3.left;
        }

        toSource.Normalize();
        return transform.position + (Vector3.up * RimHeight) + (toSource * RimRadius * 0.55f);
    }

    public GlassPourIntent GetPourIntent()
    {
        Vector3 openingNormal = transform.up;
        float tiltAngle = Vector3.Angle(openingNormal, Vector3.up);

        Vector3 rimDownhill = Vector3.ProjectOnPlane(Vector3.down, openingNormal);
        if (rimDownhill.sqrMagnitude < 0.0001f)
        {
            rimDownhill = transform.right;
        }

        rimDownhill.Normalize();

        Vector3 horizontalLean = new Vector3(openingNormal.x, 0f, openingNormal.z);
        if (horizontalLean.sqrMagnitude > 0.0001f)
        {
            horizontalLean.Normalize();
        }
        else
        {
            horizontalLean = Vector3.zero;
        }

        Vector3 pourOrigin = transform.position + (openingNormal * RimHeight) + (rimDownhill * RimRadius);

        return new GlassPourIntent(pourOrigin, rimDownhill, horizontalLean, openingNormal, currentEngagement, tiltAngle);
    }

    public void SetPouringState(bool value)
    {
        isPouring = value;
    }

    private void LateUpdate()
    {
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        float positionLerp = 1f - Mathf.Exp(-12f * deltaTime);
        float rotationLerp = 1f - Mathf.Exp(-14f * deltaTime);
        transform.position = Vector3.Lerp(transform.position, desiredPosition, positionLerp);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationLerp);

        Vector3 linearVelocity = (transform.position - previousPosition) / deltaTime;
        Quaternion deltaRotation = transform.rotation * Quaternion.Inverse(previousRotation);
        deltaRotation.ToAngleAxis(out float angle, out _);
        if (angle > 180f)
        {
            angle -= 360f;
        }

        float angularVelocity = Mathf.Abs(angle) / deltaTime;
        GlassPourIntent intent = GetPourIntent();
        float agitation = Mathf.Clamp01((linearVelocity.magnitude * 0.11f) + (angularVelocity * 0.012f));
        liquidView.SetDynamics(new LiquidDynamicsSample(transform.up, intent.DownhillDirection, intent.FlowReadiness, agitation, isHeld, isPouring));

        previousPosition = transform.position;
        previousRotation = transform.rotation;
    }
}
