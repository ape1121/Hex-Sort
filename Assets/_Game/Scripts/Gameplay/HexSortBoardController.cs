using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class HexSortBoardController : MonoBehaviour
{
    [Header("Engagement Detection")]
    [SerializeField] private float engageEnterDistance = 1.05f;
    [SerializeField] private float engageExitDistance = 1.55f;
    [Tooltip("Screen-space radius (in pixels) within which a tap can grab a glass even if the raycast misses its collider. Helps small/distant glasses on touch devices.")]
    [SerializeField] private float pickScreenRadius = 60f;
    [Tooltip("Radius (world units, on the drag plane) used as a fallback to grab a glass when the cursor is close to it but not directly over it.")]
    [SerializeField] private float pickWorldRadius = 0.85f;

    [Header("Pour Timing")]
    [SerializeField] private float perUnitPourSeconds = 0.85f;
    [SerializeField] private float streamWarmupSeconds = 0.05f;
    [Tooltip("If a pour is cancelled (release / target lost) past this fraction of a unit, the in-progress unit is committed instead of reverting.")]
    [Range(0f, 1f)]
    [SerializeField] private float partialCommitThreshold = 0.5f;

    private readonly List<HexSortGlassController> glasses = new List<HexSortGlassController>();
    private readonly List<Vector3> glassRestPositions = new List<Vector3>();
    private readonly List<Quaternion> glassRestRotations = new List<Quaternion>();

    private HexSortInputManager inputManager;
    private Camera worldCamera;
    private PourStreamView pourStream;
    private HexSortMaterialLibrary materialLibrary;
    private Vector2 boardExtents;
    private Vector3 boardPivot;
    private LiquidColorId[][] startingLayouts;
    private HexSortGlassController heldGlass;
    private HexSortGlassController candidateTarget;
    private HexSortGlassController activeEngagedTarget;
    private HexSortGlassController pourTarget;
    private float activePourProgress;
    private LiquidColorId activePourColor;
    private bool initialized;
    private int heldGlassOriginalLayer;
    private const int IgnoreRaycastLayer = 2;

    /// <summary>
    /// Fires after a pour move (full or partial-commit) has been applied to the underlying
    /// <see cref="GlassState"/>s. Subscribers can use this to detect win conditions, score, etc.
    /// </summary>
    public event System.Action MoveApplied;

    public void Initialize(
        HexSortInputManager input,
        Camera demoCamera,
        PourStreamView streamView,
        IList<HexSortGlassController> boardGlasses,
        LiquidColorId[][] initialLayouts,
        Vector2 bounds,
        Vector3 pivot,
        HexSortMaterialLibrary materials = null)
    {
        inputManager = input;
        worldCamera = demoCamera;
        pourStream = streamView;
        materialLibrary = materials;
        boardExtents = bounds;
        boardPivot = pivot;
        startingLayouts = initialLayouts;

        BindGlasses(boardGlasses, initialLayouts);

        initialized = true;
        RefreshHighlights(null);
    }

    /// <summary>
    /// Replace the controller's glass set with the supplied list and apply the new starting
    /// layouts. Captures each glass's current transform as its rest pose so subsequent
    /// <see cref="ResetBoard"/> calls return cleanly to spawn positions. Cleans up any in-flight
    /// pour / hold state and republishes collision peers. Use this to transition to a new level
    /// without reloading the scene.
    /// </summary>
    public void RebindGlasses(IList<HexSortGlassController> boardGlasses, LiquidColorId[][] initialLayouts)
    {
        ForceCancelInteraction();
        BindGlasses(boardGlasses, initialLayouts);
        RefreshHighlights(null);
    }

    /// <summary>
    /// Apply a fresh set of starting layouts to the existing glasses (e.g. for restart). Cancels
    /// any in-flight pour and snaps glasses back to their rest positions.
    /// </summary>
    public void ApplyLayouts(LiquidColorId[][] layouts)
    {
        if (!initialized || layouts == null)
        {
            return;
        }

        ForceCancelInteraction();
        startingLayouts = layouts;

        for (int i = 0; i < glasses.Count; i++)
        {
            HexSortGlassController glass = glasses[i];
            if (glass == null)
            {
                continue;
            }
            if (i < glassRestPositions.Count)
            {
                glass.transform.position = glassRestPositions[i];
                glass.transform.rotation = glassRestRotations[i];
            }
            LiquidColorId[] units = (i < layouts.Length) ? layouts[i] : new LiquidColorId[0];
            glass.SetUnits(units);
        }

        RefreshHighlights(null);
    }

    private void BindGlasses(IList<HexSortGlassController> boardGlasses, LiquidColorId[][] initialLayouts)
    {
        startingLayouts = initialLayouts;

        glasses.Clear();
        glassRestPositions.Clear();
        glassRestRotations.Clear();

        for (int i = 0; i < boardGlasses.Count; i++)
        {
            HexSortGlassController glass = boardGlasses[i];
            glasses.Add(glass);
            glassRestPositions.Add(glass.transform.position);
            glassRestRotations.Add(glass.transform.rotation);
            LiquidColorId[] units = (initialLayouts != null && i < initialLayouts.Length) ? initialLayouts[i] : new LiquidColorId[0];
            glass.SetUnits(units);
        }

        RepublishCollisionPeers();
    }

    /// <summary>
    /// Update the board pivot and extents used for cursor clamping. Call when the glass layout
    /// (and therefore the implied board footprint) changes between levels.
    /// </summary>
    public void UpdateBoardBounds(Vector3 pivot, Vector2 extents)
    {
        boardPivot = pivot;
        boardExtents = extents;
    }

    /// <summary>
    /// Re-broadcast the current glass set's colliders as collision peers. Call after the glass
    /// set changes (e.g. level transition that destroys/creates glasses).
    /// </summary>
    public void RepublishCollisionPeers()
    {
        List<Collider> peerColliders = new List<Collider>(glasses.Count);
        for (int i = 0; i < glasses.Count; i++)
        {
            Collider c = glasses[i] != null ? glasses[i].Collider : null;
            if (c != null)
            {
                peerColliders.Add(c);
            }
        }
        for (int i = 0; i < glasses.Count; i++)
        {
            if (glasses[i] != null)
            {
                glasses[i].SetCollisionPeers(peerColliders);
            }
        }
    }

    private void ForceCancelInteraction()
    {
        if (heldGlass != null)
        {
            if (heldGlass.Collider != null)
            {
                heldGlass.Collider.gameObject.layer = heldGlassOriginalLayer;
            }
            heldGlass.EndHold();
            heldGlass.SetPouringState(false);
        }
        ClearPourPreview();
        if (inputManager != null)
        {
            inputManager.ReleasePrimary(this);
        }
        heldGlass = null;
        candidateTarget = null;
        activeEngagedTarget = null;
    }

    private void Update()
    {
        if (!initialized || inputManager == null || worldCamera == null)
        {
            return;
        }

        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            ResetBoard();
            return;
        }

        if (inputManager.MultiTouchStartedThisFrame && heldGlass != null)
        {
            ReleaseHeldGlass();
            return;
        }

        if (heldGlass == null)
        {
            TryStartHold();
            return;
        }

        UpdateHeldGlass();
    }

    private void TryStartHold()
    {
        if (!inputManager.PrimaryPressedThisFrame || inputManager.MultiTouchGestureActive)
        {
            RefreshHighlights(null);
            return;
        }

        if (!TryRaycastGlass(inputManager.PrimaryScreenPosition, out HexSortGlassController glass) || glass.State.IsEmpty)
        {
            RefreshHighlights(null);
            return;
        }

        if (!TryProjectPointerToDragPlane(inputManager.PrimaryScreenPosition, out Vector3 startWorldPoint))
        {
            return;
        }

        if (!inputManager.TryCapturePrimary(this))
        {
            return;
        }

        Vector3 startCursor = ClampToBoard(startWorldPoint);

        heldGlass = glass;
        heldGlass.BeginHold(startCursor);
        // Keep the held glass's collider out of raycasts so it never blocks target picking when
        // the cursor passes over its body. ComputePenetration (held-glass-vs-peers) ignores
        // layers, so collision resolution is unaffected.
        if (heldGlass.Collider != null)
        {
            heldGlassOriginalLayer = heldGlass.Collider.gameObject.layer;
            heldGlass.Collider.gameObject.layer = IgnoreRaycastLayer;
        }
        activeEngagedTarget = null;
        pourTarget = null;
        activePourProgress = 0f;
        activePourColor = LiquidColorId.None;
        RefreshHighlights(null);
    }

    private void UpdateHeldGlass()
    {
        if (inputManager.PrimaryReleasedThisFrame || !inputManager.OwnsPrimary(this))
        {
            ReleaseHeldGlass();
            return;
        }

        if (!TryProjectPointerToDragPlane(inputManager.PrimaryScreenPosition, out Vector3 worldPoint))
        {
            return;
        }

        Vector3 cursorWorld = ClampToBoard(worldPoint);

        HexSortGlassController desiredTarget = ResolveEngagementTarget(cursorWorld);
        candidateTarget = desiredTarget;

        float engagementSignal = desiredTarget != null ? 1f : 0f;
        heldGlass.DriveHold(cursorWorld, desiredTarget, engagementSignal);
        RefreshHighlights(candidateTarget);

        TickPourFlow(desiredTarget);
    }

    private HexSortGlassController ResolveEngagementTarget(Vector3 cursorWorld)
    {
        Vector2 cursorXZ = new Vector2(cursorWorld.x, cursorWorld.z);

        HexSortGlassController nearest = null;
        float nearestDistance = float.PositiveInfinity;

        for (int i = 0; i < glasses.Count; i++)
        {
            HexSortGlassController candidate = glasses[i];
            if (candidate == heldGlass || !heldGlass.TryCreateMoveTo(candidate, 1, out _))
            {
                continue;
            }

            Vector3 candidatePos = candidate.transform.position;
            float distance = Vector2.Distance(cursorXZ, new Vector2(candidatePos.x, candidatePos.z));

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = candidate;
            }
        }

        if (nearest == null)
        {
            activeEngagedTarget = null;
            return null;
        }

        if (activeEngagedTarget == nearest)
        {
            if (nearestDistance <= engageExitDistance)
            {
                return nearest;
            }

            activeEngagedTarget = null;
            return null;
        }

        if (nearestDistance <= engageEnterDistance)
        {
            activeEngagedTarget = nearest;
            return nearest;
        }

        activeEngagedTarget = null;
        return null;
    }

    private void TickPourFlow(HexSortGlassController target)
    {
        if (target == null || !heldGlass.AnimatorIsPouring)
        {
            CommitPartialPourIfNeeded();
            ClearPourPreview();
            return;
        }

        if (!heldGlass.TryCreateMoveTo(target, 1, out PourMove move))
        {
            CommitPartialPourIfNeeded();
            ClearPourPreview();
            return;
        }

        if (activePourColor != move.Color || pourTarget != target)
        {
            activePourProgress = 0f;
            activePourColor = move.Color;
        }

        pourTarget = target;

        float pourRate = 1f / Mathf.Max(0.05f, perUnitPourSeconds);
        activePourProgress = Mathf.Min(1f, activePourProgress + (Time.deltaTime * pourRate));

        heldGlass.SetTransferPreview(move.Color, -activePourProgress);
        target.SetTransferPreview(move.Color, activePourProgress);
        heldGlass.SetPouringState(true);

        if (activePourProgress >= streamWarmupSeconds * pourRate)
        {
            GlassPourIntent intent = heldGlass.GetPourIntent();
            Vector3 receivePoint = target.GetReceivePoint(intent.PourOrigin);

            // Stream intensity grows with: (a) the per-unit progress ramp and
            // (b) how full the source still is. A nearly-full glass should produce
            // a thicker, faster stream than a glass with one unit left.
            int sourceCapacity = Mathf.Max(1, heldGlass.State.Capacity);
            float fillRatio = Mathf.Clamp01((float)heldGlass.State.Count / sourceCapacity);
            float fillBoost = Mathf.Lerp(0.4f, 1.1f, fillRatio);
            float streamIntensity = Mathf.Clamp01(activePourProgress * 1.5f * fillBoost);
            pourStream.Show(intent.PourOrigin, receivePoint, GetLiquidColor(move.Color), streamIntensity);
        }

        if (activePourProgress >= 1f)
        {
            heldGlass.ClearTransferPreview();
            target.ClearTransferPreview();
            heldGlass.ApplyMoveTo(target, move);
            activePourProgress = 0f;
            activePourColor = LiquidColorId.None;
            pourTarget = null;
            MoveApplied?.Invoke();
        }
    }

    private void ReleaseHeldGlass()
    {
        CommitPartialPourIfNeeded();

        if (heldGlass != null)
        {
            if (heldGlass.Collider != null)
            {
                heldGlass.Collider.gameObject.layer = heldGlassOriginalLayer;
            }
            heldGlass.EndHold();
            heldGlass.SetPouringState(false);
        }

        inputManager.ReleasePrimary(this);
        heldGlass = null;
        candidateTarget = null;
        activeEngagedTarget = null;
        ClearPourPreview();
        RefreshHighlights(null);
    }

    private void ClearPourPreview()
    {
        if (heldGlass != null)
        {
            heldGlass.ClearTransferPreview();
            heldGlass.SetPouringState(false);
        }

        if (candidateTarget != null)
        {
            candidateTarget.ClearTransferPreview();
        }

        activePourProgress = 0f;
        activePourColor = LiquidColorId.None;
        pourTarget = null;
        pourStream.Hide();
    }

    private void CommitPartialPourIfNeeded()
    {
        if (heldGlass == null || pourTarget == null)
        {
            return;
        }

        if (activePourProgress < partialCommitThreshold)
        {
            return;
        }

        if (!heldGlass.TryCreateMoveTo(pourTarget, 1, out PourMove move))
        {
            return;
        }

        if (move.Color != activePourColor)
        {
            return;
        }

        heldGlass.ClearTransferPreview();
        pourTarget.ClearTransferPreview();
        heldGlass.ApplyMoveTo(pourTarget, move);
        activePourProgress = 0f;
        activePourColor = LiquidColorId.None;
        pourTarget = null;
        MoveApplied?.Invoke();
    }

    /// <summary>
    /// Cancel any in-flight pour, snap every glass to its captured rest position, and re-apply
    /// the cached starting layouts. Bound to the `R` key for debugging and called from the UI
    /// reset button via <see cref="HexSortManager.RestartLevel"/>.
    /// </summary>
    public void ResetBoard()
    {
        ApplyLayouts(startingLayouts);
    }

    private void RefreshHighlights(HexSortGlassController activeCandidate)
    {
        for (int i = 0; i < glasses.Count; i++)
        {
            HexSortGlassController glass = glasses[i];
            GlassHighlightMode mode = GlassHighlightMode.None;

            if (glass.State.IsSolvedComplete)
            {
                mode = GlassHighlightMode.Solved;
            }

            if (heldGlass != null)
            {
                if (glass == heldGlass)
                {
                    mode = GlassHighlightMode.Held;
                }
                else if (heldGlass.TryCreateMoveTo(glass, 1, out _))
                {
                    mode = GlassHighlightMode.ValidTarget;
                }

                if (glass == activeCandidate)
                {
                    mode = GlassHighlightMode.CandidateTarget;
                }
            }

            glass.SetHighlight(mode);
        }
    }

    private bool TryProjectPointerToDragPlane(Vector2 screenPosition, out Vector3 worldPoint)
    {
        Ray ray = worldCamera.ScreenPointToRay(screenPosition);
        Plane plane = new Plane(Vector3.up, new Vector3(0f, 1.85f, 0f));
        if (plane.Raycast(ray, out float enter))
        {
            worldPoint = ray.GetPoint(enter);
            return true;
        }

        worldPoint = Vector3.zero;
        return false;
    }

    private Vector3 ClampToBoard(Vector3 worldPoint)
    {
        worldPoint.x = Mathf.Clamp(worldPoint.x, boardPivot.x - boardExtents.x, boardPivot.x + boardExtents.x);
        worldPoint.z = Mathf.Clamp(worldPoint.z, boardPivot.z - boardExtents.y, boardPivot.z + boardExtents.y);
        return worldPoint;
    }

    private bool TryRaycastGlass(Vector2 screenPosition, out HexSortGlassController glass)
    {
        // Primary path: direct collider raycast. RaycastAll + nearest-glass-hit so we never
        // pick a non-glass collider sitting between the camera and a glass body.
        Ray ray = worldCamera.ScreenPointToRay(screenPosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f);
        HexSortGlassController nearestGlassHit = null;
        float nearestHitDistance = float.PositiveInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            HexSortGlassController hitGlass = hits[i].collider.GetComponentInParent<HexSortGlassController>();
            if (hitGlass != null && hits[i].distance < nearestHitDistance)
            {
                nearestHitDistance = hits[i].distance;
                nearestGlassHit = hitGlass;
            }
        }
        if (nearestGlassHit != null)
        {
            glass = nearestGlassHit;
            return true;
        }

        // Fallback 1: pick the closest glass within `pickWorldRadius` of the projected drag-plane
        // point. Helps when a glass is small or partially behind another and the collider hit
        // misses by a hair.
        if (TryProjectPointerToDragPlane(screenPosition, out Vector3 worldPoint))
        {
            HexSortGlassController nearestWorld = FindNearestGlassToPoint(worldPoint, pickWorldRadius);
            if (nearestWorld != null)
            {
                glass = nearestWorld;
                return true;
            }
        }

        // Fallback 2: pick the closest glass whose world centre projects nearest to the pointer
        // in screen space (within `pickScreenRadius`). Lets users grab a glass by tapping near
        // it on the screen even if neither the collider nor the drag plane resolved.
        HexSortGlassController nearestScreen = FindNearestGlassInScreen(screenPosition, pickScreenRadius);
        if (nearestScreen != null)
        {
            glass = nearestScreen;
            return true;
        }

        glass = null;
        return false;
    }

    private HexSortGlassController FindNearestGlassToPoint(Vector3 worldPoint, float maxDistance)
    {
        HexSortGlassController nearest = null;
        float nearestSqr = maxDistance * maxDistance;
        for (int i = 0; i < glasses.Count; i++)
        {
            HexSortGlassController g = glasses[i];
            if (g == null)
            {
                continue;
            }
            Vector3 d = g.transform.position - worldPoint;
            d.y = 0f;
            float sqr = d.sqrMagnitude;
            if (sqr < nearestSqr)
            {
                nearestSqr = sqr;
                nearest = g;
            }
        }
        return nearest;
    }

    private HexSortGlassController FindNearestGlassInScreen(Vector2 screenPosition, float maxPixels)
    {
        HexSortGlassController nearest = null;
        float nearestSqr = maxPixels * maxPixels;
        for (int i = 0; i < glasses.Count; i++)
        {
            HexSortGlassController g = glasses[i];
            if (g == null)
            {
                continue;
            }
            // Aim at the body midpoint so glasses with off-pivot meshes still pick reliably.
            Vector3 worldAim = g.transform.position + (Vector3.up * g.BodyMidLocalY);
            Vector3 screenPos = worldCamera.WorldToScreenPoint(worldAim);
            if (screenPos.z <= 0f)
            {
                continue;
            }
            float sqr = ((Vector2)screenPos - screenPosition).sqrMagnitude;
            if (sqr < nearestSqr)
            {
                nearestSqr = sqr;
                nearest = g;
            }
        }
        return nearest;
    }

    private Color GetLiquidColor(LiquidColorId color)
    {
        return materialLibrary != null ? materialLibrary.GetLiquidColor(color) : Color.white;
    }
}
