using System.Collections.Generic;
using Ape.Core;
using Ape.Data;
using Ape.Profile;
using UnityEngine;

[System.Serializable]
public struct LevelResult
{
    public int LevelIndex;
    public int Moves;
    public int ParMoves;
    public int Stars;
}

public sealed class HexSortManager : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private HexSortCameraController cameraController;
    [SerializeField] private HexSortInputManager inputManager;
    [SerializeField] private HexSortBoardController boardController;
    [SerializeField] private PourStreamView pourStream;
    [SerializeField] private Transform glassesRoot;
    [SerializeField] private List<HexSortGlassController> glasses = new List<HexSortGlassController>();
    [SerializeField] private HexSortGlassController glassPrefab;

    [Header("Level Selection")]
    [Tooltip("Starting level index (used only if SaveData.Level is 0, i.e. first run). After that, level progression is driven by SaveData.")]
    [Min(0)]
    [SerializeField] private int levelIndex = 0;

    [Tooltip("If true, ignores SaveData.Level and always loads the level at `levelIndex` (useful for debugging a specific level).")]
    [SerializeField] private bool forceLevelIndex = false;

    [Tooltip("Optional inline level — overrides the App.Game.Config selection if assigned. Useful for one-off scenes / debug levels.")]
    [SerializeField] private HexSortLevelData levelDataOverride;

    [Header("Runtime Options")]
    [SerializeField] private bool initializeOnStart = true;

    public bool IsInitialized { get; private set; }

    /// <summary>
    /// Fires once when the level is solved (every glass empty or fully one colour). Carries the
    /// move count, par, and resulting star rating. Listeners (e.g. <see cref="GameUI"/>) drive
    /// the level-complete popup.
    /// </summary>
    public event System.Action<LevelResult> LevelCompleted;

    private HexSortMaterialLibrary materialLibrary;
    private HexSortLevelData resolvedLevel;
    private bool levelCompleted;
    private int moveCount;
    private int currentLevelIndex;
    private BoardConfig BoardCfg => App.Game?.Config != null ? App.Game.Config.Board : null;
    private LevelsConfig LevelsCfg => App.Game?.Config != null ? App.Game.Config.Levels : null;

    public int CurrentLevelIndex => currentLevelIndex;
    public int CurrentMoveCount => moveCount;

    private void Start()
    {
        if (initializeOnStart)
        {
            InitializeScene();
        }
    }

    public void InitializeScene()
    {
        if (IsInitialized)
        {
            return;
        }

        if (App.Game == null)
        {
            Debug.LogError("HexSortManager requires App.Game to be initialized before the scene can be configured.");
            return;
        }

        currentLevelIndex = ResolveLevelIndex();
        moveCount = 0;

        // Resolve the level first so InstantiateGlassPrefabs (called from ResolveReferences when
        // there are no pre-placed glasses) can read the layout count from the chosen level.
        resolvedLevel = ResolveLevel();

        if (!ResolveReferences())
        {
            return;
        }

        ColorsConfig colorsConfig = App.Game?.Config != null ? App.Game.Config.Colors : null;
        materialLibrary = new HexSortMaterialLibrary(colorsConfig);
        pourStream.Initialize(materialLibrary);
        EnsureGlassInitialization();

        BoardConfig board = BoardCfg;
        Vector3 configPivot = board != null ? board.BoardPivot : new Vector3(0f, 0.95f, 0f);
        Vector3 boardPivot = ComputeBoardPivot(configPivot);
        Vector2 boardExtents = ComputeBoardExtents(boardPivot);
        CameraConfig cameraConfig = App.Game?.Config != null ? App.Game.Config.Camera : null;

        cameraController.Initialize(inputManager, mainCamera, boardPivot, boardExtents, cameraConfig);
        LiquidColorId[][] layouts = MatchLayoutsToGlasses(CreateStartingLayouts());
        boardController.Initialize(inputManager, mainCamera, pourStream, glasses, layouts, boardExtents, boardPivot, materialLibrary);

        boardController.MoveApplied += HandleMoveApplied;
        levelCompleted = false;

        IsInitialized = true;
        Debug.Log($"HexSortManager initialized scene with level '{(resolvedLevel != null ? resolvedLevel.name : "<fallback>")}'.");
    }

    private void OnDestroy()
    {
        if (boardController != null)
        {
            boardController.MoveApplied -= HandleMoveApplied;
        }
    }

    private void HandleMoveApplied()
    {
        moveCount++;

        if (levelCompleted)
        {
            return;
        }

        if (!IsLevelComplete())
        {
            return;
        }

        levelCompleted = true;
        int par = ResolveParMoves();
        int stars = ComputeStars(moveCount, par);
        SaveBestStars(currentLevelIndex, stars);

        LevelResult result = new LevelResult
        {
            LevelIndex = currentLevelIndex,
            Moves = moveCount,
            ParMoves = par,
            Stars = stars,
        };
        Debug.Log($"Level {currentLevelIndex} completed in {moveCount} moves (par {par}) → {stars} stars.");
        LevelCompleted?.Invoke(result);
    }

    /// <summary>
    /// Replay the camera intro animation. Called when the player presses Play in the main menu
    /// so the camera tweens from a pulled-back pose into the gameplay framing.
    /// </summary>
    public void PlayIntro()
    {
        if (cameraController != null)
        {
            cameraController.PlayIntro();
        }
    }

    /// <summary>
    /// Restart the current level in place: cancel any in-flight pour, snap glasses to rest,
    /// re-apply the starting layouts, and glide the camera back to the fitted framing.
    /// </summary>
    public void RestartLevel()
    {
        if (!IsInitialized)
        {
            return;
        }

        moveCount = 0;
        levelCompleted = false;
        ReframeCamera();
        boardController.ResetBoard();
    }

    private void ReframeCamera()
    {
        BoardConfig boardCfg = BoardCfg;
        Vector3 configPivot = boardCfg != null ? boardCfg.BoardPivot : new Vector3(0f, 0.95f, 0f);
        Vector3 newPivot = ComputeBoardPivot(configPivot);
        Vector2 newExtents = ComputeBoardExtents(newPivot);
        cameraController.Reframe(newPivot, newExtents);
        boardController.UpdateBoardBounds(newPivot, newExtents);
    }

    /// <summary>
    /// Advance to the next level in place. Persists the new index and re-applies the level.
    /// </summary>
    public void LoadNextLevel()
    {
        GoToLevel(currentLevelIndex + 1);
    }

    /// <summary>
    /// Transition to a specific level index in place. Persists the index to SaveData and either
    /// re-applies the new starting layouts to the existing glasses (fast path), or destroys and
    /// re-spawns the glasses if the level's glass count or capacity changed (slow path). No
    /// scene reload. Wraps around when past the last entry of <see cref="LevelsConfig"/>.
    /// </summary>
    public void GoToLevel(int index)
    {
        if (!IsInitialized)
        {
            return;
        }

        currentLevelIndex = Mathf.Max(0, index);
        if (App.Profile != null)
        {
            SaveData data = App.Profile.CurrentData;
            data.Level = currentLevelIndex;
            App.Profile.SetData(data);
        }

        moveCount = 0;
        levelCompleted = false;
        resolvedLevel = ResolveLevel();

        int desiredCapacity = ResolveCapacity();
        int desiredGlassCount = resolvedLevel != null && resolvedLevel.GlassCount > 0
            ? resolvedLevel.GlassCount
            : glasses.Count;
        int currentCapacity = glasses.Count > 0 && glasses[0] != null && glasses[0].State != null
            ? glasses[0].State.Capacity
            : 0;

        bool needRebuild = desiredGlassCount != glasses.Count || desiredCapacity != currentCapacity;

        if (needRebuild)
        {
            DestroyExistingGlasses();
            InstantiateGlassPrefabs();
            EnsureGlassInitialization();
            ReframeCamera();

            LiquidColorId[][] layouts = MatchLayoutsToGlasses(CreateStartingLayouts());
            boardController.RebindGlasses(glasses, layouts);
        }
        else
        {
            // Same glass set, but tween the camera back to the fitted pose in case the user
            // panned / zoomed / orbited during the previous level.
            ReframeCamera();

            LiquidColorId[][] layouts = MatchLayoutsToGlasses(CreateStartingLayouts());
            boardController.ApplyLayouts(layouts);
        }
    }

    /// <summary>
    /// Pivot to center the camera on. Y comes from the BoardConfig; X/Z are the centroid of the
    /// actual glass positions so a row of glasses always sits in the middle of the screen.
    /// </summary>
    private Vector3 ComputeBoardPivot(Vector3 fallback)
    {
        if (glasses == null || glasses.Count == 0)
        {
            return fallback;
        }

        float sumX = 0f;
        float sumZ = 0f;
        int count = 0;
        for (int i = 0; i < glasses.Count; i++)
        {
            if (glasses[i] == null)
            {
                continue;
            }
            Vector3 p = glasses[i].transform.position;
            sumX += p.x;
            sumZ += p.z;
            count++;
        }

        if (count == 0)
        {
            return fallback;
        }
        return new Vector3(sumX / count, fallback.y, sumZ / count);
    }

    /// <summary>
    /// Half-extents from glass span + footprint padding, lower-bounded by BoardConfig.BoardExtents
    /// so a small board never gets framed too tightly.
    /// </summary>
    private Vector2 ComputeBoardExtents(Vector3 pivot)
    {
        BoardConfig board = BoardCfg;
        Vector2 fallback = board != null ? board.BoardExtents : new Vector2(5.5f, 1.85f);
        if (glasses == null || glasses.Count == 0)
        {
            return fallback;
        }

        float footprint = board != null ? board.GlassFootprintRadius : 0.6f;
        float maxDx = 0f;
        float maxDz = 0f;
        for (int i = 0; i < glasses.Count; i++)
        {
            if (glasses[i] == null)
            {
                continue;
            }
            Vector3 p = glasses[i].transform.position;
            float dx = Mathf.Abs(p.x - pivot.x);
            float dz = Mathf.Abs(p.z - pivot.z);
            if (dx > maxDx) maxDx = dx;
            if (dz > maxDz) maxDz = dz;
        }

        return new Vector2(
            Mathf.Max(fallback.x, maxDx + footprint),
            Mathf.Max(fallback.y, maxDz + footprint));
    }

    private void DestroyExistingGlasses()
    {
        for (int i = 0; i < glasses.Count; i++)
        {
            if (glasses[i] != null)
            {
                Destroy(glasses[i].gameObject);
            }
        }
        glasses.Clear();
    }

    private static int ComputeStars(int moves, int par)
    {
        if (par <= 0)
        {
            return 3;
        }
        if (moves <= par)
        {
            return 3;
        }
        if (moves <= Mathf.CeilToInt(par * 1.5f))
        {
            return 2;
        }
        return 1;
    }

    private int ResolveParMoves()
    {
        if (resolvedLevel != null && resolvedLevel.parMoves > 0)
        {
            return resolvedLevel.parMoves;
        }
        // Fallback heuristic when an authored level has no par set: 3 moves per glass is a
        // reasonable rough bar for a manual / hardcoded layout.
        return Mathf.Max(1, glasses.Count * 3);
    }

    private void SaveBestStars(int idx, int stars)
    {
        if (App.Profile == null || idx < 0)
        {
            return;
        }

        SaveData data = App.Profile.CurrentData;
        int[] arr = data.LevelStars;
        if (arr == null || arr.Length <= idx)
        {
            int[] grown = new int[idx + 1];
            if (arr != null)
            {
                System.Array.Copy(arr, grown, arr.Length);
            }
            arr = grown;
        }
        if (stars > arr[idx])
        {
            arr[idx] = stars;
        }
        data.LevelStars = arr;
        App.Profile.SetData(data);
    }

    private int ResolveLevelIndex()
    {
        if (forceLevelIndex)
        {
            return Mathf.Max(0, levelIndex);
        }

        if (App.Profile != null && App.Profile.CurrentData.Level > 0)
        {
            return App.Profile.CurrentData.Level;
        }

        return Mathf.Max(0, levelIndex);
    }

    /// <summary>
    /// Level is complete when every glass is either empty or fully filled with a single colour.
    /// </summary>
    private bool IsLevelComplete()
    {
        if (glasses == null || glasses.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < glasses.Count; i++)
        {
            HexSortGlassController glass = glasses[i];
            if (glass == null || glass.State == null)
            {
                continue;
            }

            if (!glass.State.IsEmpty && !glass.State.IsSolvedComplete)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Inline override > GameConfig.Levels[currentLevelIndex] (looped) > null (use hardcoded fallback layouts).
    /// </summary>
    private HexSortLevelData ResolveLevel()
    {
        if (levelDataOverride != null)
        {
            return levelDataOverride;
        }

        LevelsConfig levelsConfig = LevelsCfg;
        if (levelsConfig == null || levelsConfig.LevelCount == 0)
        {
            Debug.LogWarning("HexSortManager: App.Game.Config.Levels is unassigned or empty; falling back to hardcoded layouts.");
            return null;
        }

        return levelsConfig.GetLevelLooped(currentLevelIndex);
    }

    private void EnsureGlassInitialization()
    {
        int capacity = ResolveCapacity();
        for (int i = 0; i < glasses.Count; i++)
        {
            HexSortGlassController glass = glasses[i];
            if (glass == null)
            {
                continue;
            }

            if (!glass.IsInitialized)
            {
                glass.Initialize(i, capacity, materialLibrary);
            }
        }
    }

    private int ResolveCapacity()
    {
        if (resolvedLevel != null)
        {
            return Mathf.Max(1, resolvedLevel.capacity);
        }
        BoardConfig board = BoardCfg;
        return Mathf.Max(1, board != null ? board.DefaultGlassCapacity : 4);
    }

    private bool ResolveReferences()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (mainCamera == null)
        {
            Debug.LogError("HexSortManager requires a Main Camera reference.");
            return false;
        }

        if (cameraController == null)
        {
            cameraController = mainCamera.GetComponent<HexSortCameraController>();
        }

        if (cameraController == null)
        {
            Debug.LogError("HexSortManager requires a HexSortCameraController on the scene camera.");
            return false;
        }

        if (inputManager == null)
        {
            inputManager = GetComponent<HexSortInputManager>();
            if (inputManager == null)
            {
                inputManager = FindFirstObjectByType<HexSortInputManager>();
            }
        }

        if (inputManager == null)
        {
            Debug.LogError("HexSortManager requires a HexSortInputManager in the scene.");
            return false;
        }

        if (boardController == null)
        {
            boardController = GetComponent<HexSortBoardController>();
            if (boardController == null)
            {
                boardController = FindFirstObjectByType<HexSortBoardController>();
            }
        }

        if (boardController == null)
        {
            Debug.LogError("HexSortManager requires a HexSortBoardController in the scene.");
            return false;
        }

        if (pourStream == null)
        {
            pourStream = FindFirstObjectByType<PourStreamView>();
        }

        if (pourStream == null)
        {
            Debug.LogError("HexSortManager requires a PourStreamView in the scene.");
            return false;
        }

        ResolveGlasses();
        if (glasses.Count == 0 && glassPrefab != null)
        {
            InstantiateGlassPrefabs();
        }

        if (glasses.Count == 0)
        {
            Debug.LogError("HexSortManager requires at least one HexSortGlassController in the scene or a glass prefab reference.");
            return false;
        }

        return true;
    }

    private void ResolveGlasses()
    {
        if (glasses == null)
        {
            glasses = new List<HexSortGlassController>();
        }

        glasses.RemoveAll(item => item == null);
        if (glasses.Count > 0)
        {
            return;
        }

        if (glassesRoot != null)
        {
            glasses.AddRange(glassesRoot.GetComponentsInChildren<HexSortGlassController>(true));
        }

        if (glasses.Count == 0)
        {
            glasses.AddRange(FindObjectsByType<HexSortGlassController>(FindObjectsSortMode.None));
        }
    }

    private void InstantiateGlassPrefabs()
    {
        if (glassPrefab == null)
        {
            return;
        }

        if (glasses == null)
        {
            glasses = new List<HexSortGlassController>();
        }

        if (glassesRoot == null)
        {
            GameObject root = new GameObject("Glasses");
            root.transform.SetParent(transform, false);
            glassesRoot = root.transform;
        }

        BoardConfig board = BoardCfg;
        float glassSpacing = board != null ? board.GlassSpacing : 1.9f;
        float glassY = board != null ? board.GlassY : 0.54f;

        LiquidColorId[][] layouts = CreateStartingLayouts();
        float startX = -((layouts.Length - 1) * glassSpacing) * 0.5f;

        for (int i = 0; i < layouts.Length; i++)
        {
            HexSortGlassController instance = Object.Instantiate(glassPrefab, glassesRoot);
            instance.name = $"Glass_{i}";
            instance.transform.localPosition = new Vector3(startX + (i * glassSpacing), glassY, 0f);
            instance.transform.localRotation = Quaternion.identity;
            instance.gameObject.SetActive(true);
            glasses.Add(instance);
        }
    }

    private LiquidColorId[][] MatchLayoutsToGlasses(LiquidColorId[][] layouts)
    {
        if (layouts.Length == glasses.Count)
        {
            return layouts;
        }

        Debug.LogWarning($"HexSortManager: starting layouts ({layouts.Length}) don't match glass count ({glasses.Count}). Padding/trimming.");
        LiquidColorId[][] adjusted = new LiquidColorId[glasses.Count][];
        for (int i = 0; i < glasses.Count; i++)
        {
            adjusted[i] = i < layouts.Length ? layouts[i] : new LiquidColorId[0];
        }
        return adjusted;
    }

    private LiquidColorId[][] CreateStartingLayouts()
    {
        if (resolvedLevel != null && resolvedLevel.GlassCount > 0)
        {
            int count = resolvedLevel.GlassCount;
            LiquidColorId[][] result = new LiquidColorId[count][];
            for (int i = 0; i < count; i++)
            {
                result[i] = resolvedLevel.GetGlassUnits(i);
            }
            return result;
        }

        // Hardcoded fallback used only when no level has been authored / wired up yet.
        return new[]
        {
            new[] { LiquidColorId.Gold, LiquidColorId.Coral, LiquidColorId.Coral, LiquidColorId.Sky },
            new[] { LiquidColorId.Mint, LiquidColorId.Sky, LiquidColorId.Gold, LiquidColorId.Gold },
            new[] { LiquidColorId.Coral, LiquidColorId.Mint, LiquidColorId.Sky, LiquidColorId.Rose },
            new[] { LiquidColorId.Rose, LiquidColorId.Mint, LiquidColorId.Grape, LiquidColorId.Grape },
            new LiquidColorId[0],
            new LiquidColorId[0],
        };
    }
}
