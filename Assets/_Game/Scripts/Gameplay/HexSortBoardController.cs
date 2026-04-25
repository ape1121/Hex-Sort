using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class HexSortBoardController : MonoBehaviour
{
    [Header("Engagement Detection")]
    [SerializeField] private float engageEnterDistance = 1.05f;
    [SerializeField] private float engageExitDistance = 1.55f;

    [Header("Pour Timing")]
    [SerializeField] private float perUnitPourSeconds = 0.85f;
    [SerializeField] private float streamWarmupSeconds = 0.05f;
    [Tooltip("If a pour is cancelled (release / target lost) past this fraction of a unit, the in-progress unit is committed instead of reverting.")]
    [Range(0f, 1f)]
    [SerializeField] private float partialCommitThreshold = 0.5f;

    private readonly List<HexSortGlassController> glasses = new List<HexSortGlassController>();

    private HexSortInputManager inputManager;
    private Camera worldCamera;
    private PourStreamView pourStream;
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

    public void Initialize(
        HexSortInputManager input,
        Camera demoCamera,
        PourStreamView streamView,
        IList<HexSortGlassController> boardGlasses,
        LiquidColorId[][] initialLayouts,
        Vector2 bounds,
        Vector3 pivot)
    {
        inputManager = input;
        worldCamera = demoCamera;
        pourStream = streamView;
        boardExtents = bounds;
        boardPivot = pivot;
        startingLayouts = initialLayouts;

        glasses.Clear();
        for (int i = 0; i < boardGlasses.Count; i++)
        {
            glasses.Add(boardGlasses[i]);
            glasses[i].SetUnits(initialLayouts[i]);
        }

        // Publish each glass's collider list to every other glass so the held-glass collision
        // pass can keep dragged glasses outside their neighbours.
        List<Collider> peerColliders = new List<Collider>(glasses.Count);
        for (int i = 0; i < glasses.Count; i++)
        {
            Collider c = glasses[i].Collider;
            if (c != null)
            {
                peerColliders.Add(c);
            }
        }
        for (int i = 0; i < glasses.Count; i++)
        {
            glasses[i].SetCollisionPeers(peerColliders);
        }

        initialized = true;
        RefreshHighlights(null);
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
        }
    }

    private void ReleaseHeldGlass()
    {
        CommitPartialPourIfNeeded();

        if (heldGlass != null)
        {
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
    }

    private void ResetBoard()
    {
        ClearPourPreview();
        inputManager.ReleasePrimary(this);
        heldGlass = null;
        candidateTarget = null;
        activeEngagedTarget = null;

        for (int i = 0; i < glasses.Count; i++)
        {
            glasses[i].transform.position = new Vector3(glasses[i].transform.position.x, 0.54f, 0f);
            glasses[i].transform.rotation = Quaternion.identity;
            glasses[i].SetUnits(startingLayouts[i]);
        }

        RefreshHighlights(null);
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
        Ray ray = worldCamera.ScreenPointToRay(screenPosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            glass = hit.collider.GetComponentInParent<HexSortGlassController>();
            return glass != null;
        }

        glass = null;
        return false;
    }

    private Color GetLiquidColor(LiquidColorId color)
    {
        switch (color)
        {
            case LiquidColorId.Coral:
                return new Color(0.96f, 0.39f, 0.35f);
            case LiquidColorId.Sky:
                return new Color(0.25f, 0.67f, 0.98f);
            case LiquidColorId.Mint:
                return new Color(0.34f, 0.88f, 0.69f);
            case LiquidColorId.Gold:
                return new Color(0.98f, 0.79f, 0.28f);
            case LiquidColorId.Grape:
                return new Color(0.58f, 0.41f, 0.89f);
            case LiquidColorId.Rose:
                return new Color(0.97f, 0.50f, 0.71f);
            default:
                return Color.white;
        }
    }
}
