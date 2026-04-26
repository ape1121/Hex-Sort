using UnityEngine;

public sealed class HexSortGlassController : MonoBehaviour
{
    [Header("Anchors")]
    [Tooltip("Where the procedural liquid column is parented. Leave null to auto-create a child named 'Liquid'.")]
    [SerializeField] private Transform liquidAnchor;
    [Tooltip("Optional renderer used for the selection highlight tint. Leave null to disable highlighting.")]
    [SerializeField] private Renderer highlightRenderer;
    [Tooltip("Collider used for held-glass collision against other glasses, and for picking. Leave null to auto-resolve via GetComponent<Collider>() on this GameObject (and add a fallback CapsuleCollider if nothing is found).")]
    [SerializeField] private Collider glassColliderOverride;

    [Header("Interior Geometry (glass-local units)")]
    [Tooltip("Local Y of the interior floor — where the liquid sits.")]
    [SerializeField] private float interiorBottomLocalY = 0.18f;
    [Tooltip("Vertical height each colour unit occupies inside the glass.")]
    [SerializeField] private float unitHeight = 0.44f;
    [Tooltip("Inner radius at the cavity floor (interiorBottomLocalY). Lower this for tapered glasses.")]
    [SerializeField] private float interiorBottomRadius = 0.42f;
    [Tooltip("Inner radius at the top of the procedural liquid column (meshTopLocalY). Make larger than the bottom for a flared glass.")]
    [SerializeField] private float interiorTopRadius = 0.42f;
    [Tooltip("Top of the procedural liquid mesh in glass-local space. Set well above the rim so leaning never clips.")]
    [SerializeField] private float meshTopLocalY = 4.5f;
    [Tooltip("Local Y of the rim (the pour edge).")]
    [SerializeField] private float rimLocalY = 2.18f;
    [Tooltip("Horizontal distance from the glass centre to the rim edge — used to position pour streams.")]
    [SerializeField] private float rimRadius = 0.34f;

    private GlassLiquidView liquidView;
    private GlassPourAnimator pourAnimator;
    private GlassState state;
    private Collider glassCollider;
    private Vector3 restPosition;
    private Vector3 previousPosition;
    private Quaternion previousRotation;
    private HexSortGlassController currentEngagedTarget;
    private float currentEngagement;
    private float currentPreviewUnits;
    private bool isHeld;
    private bool isPouring;
    private bool isInitialized;

    public int Index { get; private set; }

    public GlassState State => state;

    public bool AnimatorIsPouring => pourAnimator != null && pourAnimator.IsPouring;

    public bool AnimatorIsTransitioning => pourAnimator != null && pourAnimator.IsTransitioning;

    public HexSortGlassController EngagedTarget => currentEngagedTarget;

    public bool IsInitialized => isInitialized;

    public float InteriorBottomLocalY => interiorBottomLocalY;
    public float RimLocalY => rimLocalY;
    public float RimRadius => rimRadius;
    public float BodyMidLocalY => (interiorBottomLocalY + rimLocalY) * 0.5f;

    /// <summary>
    /// Collider used by the pour animator's collision-resolution pass to keep the held glass
    /// outside other glasses. Resolved in <see cref="Initialize"/> from any user-authored
    /// Collider on the same GameObject; if there is none, a fallback `CapsuleCollider` is added.
    /// </summary>
    public Collider Collider => glassCollider;

    /// <summary>
    /// Tell this glass which other glasses it must stay outside of while being dragged or
    /// animated. The list is forwarded to the internal pour animator.
    /// </summary>
    public void SetCollisionPeers(System.Collections.Generic.IList<Collider> peers)
    {
        if (pourAnimator != null)
        {
            pourAnimator.SetCollisionPeers(peers);
        }
    }

    /// <summary>
    /// Continuously-varying displayed fill amount (state count plus the in-progress pour
    /// preview). Use this for visual reactions like dynamic tilt — it varies smoothly as the
    /// preview drains, instead of jumping by 1 at each unit-commit boundary.
    /// </summary>
    public float DisplayedFillUnits
    {
        get
        {
            if (state == null)
            {
                return 0f;
            }
            return Mathf.Max(0f, state.Count + currentPreviewUnits);
        }
    }

    public void Initialize(int index, int capacity, HexSortMaterialLibrary materials)
    {
        if (isInitialized)
        {
            return;
        }

        Index = index;
        state = new GlassState(capacity);

        if (interiorBottomRadius < 0.01f) interiorBottomRadius = 0.42f;
        if (interiorTopRadius < 0.01f) interiorTopRadius = 0.42f;
        if (unitHeight < 0.01f) unitHeight = 0.44f;
        if (meshTopLocalY < rimLocalY + 0.1f) meshTopLocalY = rimLocalY + 2.0f;

        // Auto-clamp unitHeight so capacity * unitHeight fits within the visible interior.
        // Without this, visual full saturates at the rim before logical full, and the pour rules
        // happily accept more units even though the glass *looks* full.
        float availableHeight = rimLocalY - interiorBottomLocalY - 0.05f;
        if (availableHeight > 0.01f)
        {
            float maxUnitHeight = availableHeight / Mathf.Max(1, capacity);
            if (unitHeight > maxUnitHeight)
            {
                Debug.LogWarning($"HexSortGlassController '{name}': unitHeight {unitHeight:F3} > {maxUnitHeight:F3} (capacity {capacity}, interior height {availableHeight:F3}). Clamping so visual full == logical full.");
                unitHeight = maxUnitHeight;
            }
        }

        Transform liquidRoot = liquidAnchor;
        if (liquidRoot == null)
        {
            liquidRoot = new GameObject("Liquid").transform;
            liquidRoot.SetParent(transform, false);
        }

        if (highlightRenderer != null && highlightRenderer.sharedMaterial != null)
        {
            highlightRenderer.sharedMaterial = materials.CreateHighlightMaterialInstance();
        }

        // Prefer prefab-attached components so the inspector-configured tunables survive — a
        // bare AddComponent would silently spawn a second instance with default values that
        // overwrites the prefab one in LateUpdate (e.g. pourTiltAngleFull/pourHeightOffset
        // edits would appear to have no effect).
        liquidView = gameObject.GetComponent<GlassLiquidView>();
        if (liquidView == null)
        {
            liquidView = gameObject.AddComponent<GlassLiquidView>();
        }
        liquidView.Initialize(liquidRoot, state, materials, capacity, interiorBottomLocalY, unitHeight, interiorBottomRadius, interiorTopRadius, meshTopLocalY, rimLocalY);

        pourAnimator = gameObject.GetComponent<GlassPourAnimator>();
        if (pourAnimator == null)
        {
            pourAnimator = gameObject.AddComponent<GlassPourAnimator>();
        }

        // Prefer an inspector-assigned collider (any Collider on the glass mesh, capsule, etc).
        glassCollider = glassColliderOverride;
        if (glassCollider == null)
        {
            glassCollider = gameObject.GetComponent<Collider>();
        }
        if (glassCollider == null)
        {
            // Fallback so the held-glass collision still works even when the user hasn't
            // configured a collider on the mesh yet.
            CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 1.28f, 0f);
            capsule.radius = 0.52f;
            capsule.height = 2.85f;
            glassCollider = capsule;
        }

        restPosition = transform.position;
        previousPosition = transform.position;
        previousRotation = transform.rotation;

        SetHighlight(GlassHighlightMode.None);
        liquidView.RefreshGeometry();
        isInitialized = true;
    }

    public void SetUnits(LiquidColorId[] units)
    {
        state.SetUnits(units);
        liquidView.ClearPreview();
        liquidView.RefreshGeometry();
    }

    public void BeginHold(Vector3 cursorWorld)
    {
        isHeld = true;
        isPouring = false;
        currentEngagement = 0f;
        currentEngagedTarget = null;
        restPosition = transform.position;
        pourAnimator.BeginHold(cursorWorld);

        if (liquidView != null)
        {
            Vector3 toCursor = cursorWorld - transform.position;
            toCursor.y = 0f;
            if (toCursor.sqrMagnitude > 0.0001f)
            {
                liquidView.AddSloshImpulse(toCursor.normalized * 0.45f);
            }
        }
    }

    public void DriveHold(Vector3 cursorWorld, HexSortGlassController target, float engagement)
    {
        currentEngagement = Mathf.Clamp01(engagement);

        if (target != null)
        {
            if (currentEngagedTarget != target)
            {
                pourAnimator.EngageTarget(target);
                currentEngagedTarget = target;
            }
            return;
        }

        if (currentEngagedTarget != null)
        {
            pourAnimator.DisengageTarget(cursorWorld);
            currentEngagedTarget = null;
            return;
        }

        pourAnimator.UpdateCursor(cursorWorld);
    }

    public void EndHold()
    {
        isHeld = false;
        isPouring = false;
        currentEngagement = 0f;
        currentEngagedTarget = null;
        liquidView.ClearPreview();
        liquidView.AddSloshImpulse(new Vector3(Random.Range(-0.25f, 0.25f), 0f, Random.Range(-0.25f, 0.25f)));
        pourAnimator.ReturnToRest(restPosition);
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
        currentPreviewUnits = units;
        liquidView.SetPreview(color, units);
    }

    public void ClearTransferPreview()
    {
        currentPreviewUnits = 0f;
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
        // Land the stream on the *actual* liquid surface inside the glass — not the rim — so
        // the pour tube extends all the way down and the splash particles fire on the
        // liquid surface instead of in mid-air at the rim. Falls back to a point just above
        // the floor when the glass is empty.
        float surfaceLocalY = interiorBottomLocalY + (DisplayedFillUnits * unitHeight);
        // Stay below the rim by a small margin so the particle position is unambiguously
        // inside the cup, even when the glass is nearly full.
        surfaceLocalY = Mathf.Min(surfaceLocalY, rimLocalY - 0.05f);
        if (state != null && state.IsEmpty)
        {
            surfaceLocalY = interiorBottomLocalY + 0.02f;
        }

        Vector3 toSource = sourcePosition - transform.position;
        toSource.y = 0f;
        if (toSource.sqrMagnitude < 0.0001f)
        {
            toSource = Vector3.left;
        }
        toSource.Normalize();

        // Small lateral bias toward the source side keeps the impact off dead-centre for
        // visual variety, but well inside the interior radius.
        return transform.position + (Vector3.up * surfaceLocalY) + (toSource * rimRadius * 0.15f);
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

        Vector3 pourOrigin = transform.position + (openingNormal * rimLocalY) + (rimDownhill * rimRadius);

        return new GlassPourIntent(pourOrigin, rimDownhill, horizontalLean, openingNormal, currentEngagement, tiltAngle);
    }

    public void SetPouringState(bool value)
    {
        isPouring = value;
    }

    private void OnValidate()
    {
        if (interiorBottomRadius < 0.01f) interiorBottomRadius = 0.01f;
        if (interiorTopRadius < 0.01f) interiorTopRadius = 0.01f;
        if (unitHeight < 0.01f) unitHeight = 0.01f;
        if (meshTopLocalY < rimLocalY + 0.1f) meshTopLocalY = rimLocalY + 0.1f;
        if (meshTopLocalY < interiorBottomLocalY + 0.1f) meshTopLocalY = interiorBottomLocalY + 0.1f;
    }

    private void LateUpdate()
    {
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

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

        if (liquidView == null || !isInitialized)
        {
            previousPosition = transform.position;
            previousRotation = transform.rotation;
            return;
        }

        liquidView.SetDynamics(new LiquidDynamicsSample(
            transform.up,
            intent.DownhillDirection,
            linearVelocity,
            angularVelocity,
            intent.FlowReadiness,
            agitation,
            isHeld,
            isPouring));

        previousPosition = transform.position;
        previousRotation = transform.rotation;
    }

#if UNITY_EDITOR
    [Header("Gizmos")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private int gizmoCapacity = 4;
    [SerializeField] private int gizmoCircleSegments = 32;

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
        {
            return;
        }

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;

        Color cavityColor = new Color(0.34f, 0.74f, 1f, 0.85f);
        Color unitColor = new Color(0.34f, 0.94f, 0.62f, 0.55f);
        Color rimColor = new Color(0.99f, 0.67f, 0.32f, 0.95f);
        Color meshTopColor = new Color(1f, 1f, 1f, 0.45f);
        Color pourColor = new Color(0.97f, 0.50f, 0.71f, 0.95f);

        Gizmos.color = cavityColor;
        DrawGizmoCircle(interiorBottomLocalY, RadiusAt(interiorBottomLocalY));
        DrawGizmoTaperVerticals(interiorBottomLocalY, rimLocalY, 8);

        Gizmos.color = unitColor;
        int slices = Mathf.Max(0, gizmoCapacity);
        for (int i = 1; i <= slices; i++)
        {
            float y = interiorBottomLocalY + (i * unitHeight);
            if (y > rimLocalY + 0.001f)
            {
                break;
            }
            DrawGizmoCircle(y, RadiusAt(y));
        }

        Gizmos.color = rimColor;
        DrawGizmoCircle(rimLocalY, RadiusAt(rimLocalY));
        DrawGizmoCircle(rimLocalY, rimRadius);

        Gizmos.color = meshTopColor;
        DrawGizmoCircle(meshTopLocalY, RadiusAt(meshTopLocalY));
        DrawGizmoTaperVerticals(rimLocalY, meshTopLocalY, 4);

        Gizmos.color = pourColor;
        Vector3 pourOriginLocal = new Vector3(rimRadius, rimLocalY, 0f);
        Gizmos.DrawSphere(pourOriginLocal, 0.04f);

        Gizmos.matrix = previousMatrix;
    }

    private void DrawGizmoCircle(float localY, float radius)
    {
        int segments = Mathf.Max(8, gizmoCircleSegments);
        Vector3 previous = new Vector3(radius, localY, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float angle = (Mathf.PI * 2f * i) / segments;
            Vector3 next = new Vector3(Mathf.Cos(angle) * radius, localY, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }

    private void DrawGizmoTaperVerticals(float fromLocalY, float toLocalY, int spokes)
    {
        int count = Mathf.Max(2, spokes);
        float fromRadius = RadiusAt(fromLocalY);
        float toRadius = RadiusAt(toLocalY);
        for (int i = 0; i < count; i++)
        {
            float angle = (Mathf.PI * 2f * i) / count;
            float cosA = Mathf.Cos(angle);
            float sinA = Mathf.Sin(angle);
            Vector3 bottom = new Vector3(cosA * fromRadius, fromLocalY, sinA * fromRadius);
            Vector3 top = new Vector3(cosA * toRadius, toLocalY, sinA * toRadius);
            Gizmos.DrawLine(bottom, top);
        }
    }

    private float RadiusAt(float localY)
    {
        float span = meshTopLocalY - interiorBottomLocalY;
        if (Mathf.Abs(span) < 0.0001f)
        {
            return interiorBottomRadius;
        }

        float t = Mathf.Clamp01((localY - interiorBottomLocalY) / span);
        return Mathf.Lerp(interiorBottomRadius, interiorTopRadius, t);
    }
#endif
}
