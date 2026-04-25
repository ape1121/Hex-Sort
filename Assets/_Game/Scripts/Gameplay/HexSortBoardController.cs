using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class HexSortBoardController : MonoBehaviour
{
    private const float PourEngagementMaxDistance = 1.85f;
    private const float PourEngagementMinDistance = 0.55f;
    private const float PourEngagementThreshold = 0.4f;

    private readonly List<HexSortGlassController> glasses = new List<HexSortGlassController>();

    private HexSortInputManager inputManager;
    private Camera worldCamera;
    private PourStreamView pourStream;
    private Vector2 boardExtents;
    private Vector3 boardPivot;
    private LiquidColorId[][] startingLayouts;
    private HexSortGlassController heldGlass;
    private HexSortGlassController candidateTarget;
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

        if (!inputManager.TryCapturePrimary(this))
        {
            return;
        }

        heldGlass = glass;
        heldGlass.BeginHold();
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

        HexSortGlassController nearestPourable = FindNearestPourTarget(cursorWorld, out float engagement);
        candidateTarget = nearestPourable;

        heldGlass.UpdateHoldPose(cursorWorld, nearestPourable, engagement);
        RefreshHighlights(candidateTarget);

        if (candidateTarget == null || !heldGlass.TryCreateMoveTo(candidateTarget, 1, out PourMove move) || engagement < PourEngagementThreshold)
        {
            ClearPourPreview();
            return;
        }

        if (activePourColor != move.Color)
        {
            activePourProgress = 0f;
            activePourColor = move.Color;
        }

        float pourRate = Mathf.Lerp(0.6f, 2.4f, engagement);
        activePourProgress += Time.deltaTime * pourRate;

        heldGlass.SetTransferPreview(move.Color, -Mathf.Clamp01(activePourProgress));
        candidateTarget.SetTransferPreview(move.Color, Mathf.Clamp01(activePourProgress));
        heldGlass.SetPouringState(true);

        GlassPourIntent intent = heldGlass.GetPourIntent();
        Vector3 receivePoint = candidateTarget.GetReceivePoint(intent.PourOrigin);
        pourStream.Show(intent.PourOrigin, receivePoint, GetLiquidColor(move.Color), engagement);

        if (activePourProgress >= 1f)
        {
            heldGlass.ClearTransferPreview();
            candidateTarget.ClearTransferPreview();
            heldGlass.ApplyMoveTo(candidateTarget, move);
            activePourProgress = 0f;
            activePourColor = LiquidColorId.None;
        }
    }

    private HexSortGlassController FindNearestPourTarget(Vector3 cursorWorld, out float engagement)
    {
        engagement = 0f;
        HexSortGlassController bestTarget = null;
        float bestDistance = float.PositiveInfinity;

        Vector2 cursorXZ = new Vector2(cursorWorld.x, cursorWorld.z);

        for (int i = 0; i < glasses.Count; i++)
        {
            HexSortGlassController candidate = glasses[i];
            if (candidate == heldGlass || !heldGlass.TryCreateMoveTo(candidate, 1, out _))
            {
                continue;
            }

            Vector3 candidatePos = candidate.transform.position;
            Vector2 candidateXZ = new Vector2(candidatePos.x, candidatePos.z);
            float distance = Vector2.Distance(cursorXZ, candidateXZ);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestTarget = candidate;
            }
        }

        if (bestTarget == null || bestDistance >= PourEngagementMaxDistance)
        {
            return null;
        }

        engagement = Mathf.InverseLerp(PourEngagementMaxDistance, PourEngagementMinDistance, bestDistance);
        return bestTarget;
    }

    private void ReleaseHeldGlass()
    {
        if (heldGlass != null)
        {
            heldGlass.EndHold();
            heldGlass.SetPouringState(false);
        }

        inputManager.ReleasePrimary(this);
        heldGlass = null;
        candidateTarget = null;
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
        pourStream.Hide();
    }

    private void ResetBoard()
    {
        ClearPourPreview();
        inputManager.ReleasePrimary(this);
        heldGlass = null;
        candidateTarget = null;

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
