using System.Collections.Generic;
using Ape.Core;
using UnityEngine;

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

    [Header("Level")]
    [Tooltip("Optional ScriptableObject describing per-glass starting fill and capacity. If null, the hardcoded fallback layouts below are used.")]
    [SerializeField] private HexSortLevelData levelData;

    [Header("Board Settings")]
    [Tooltip("Default glass capacity when no LevelData is assigned.")]
    [SerializeField] private int glassCapacity = 4;
    [SerializeField] private float glassSpacing = 1.9f;
    [SerializeField] private float glassY = 0.54f;
    [SerializeField] private Vector2 boardExtents = new Vector2(5.5f, 1.85f);
    [SerializeField] private Vector3 boardPivot = new Vector3(0f, 0.95f, 0f);
    [SerializeField] private float initialZoom = 9.4f;

    [Header("Runtime Options")]
    [SerializeField] private bool initializeOnStart = true;

    public bool IsInitialized { get; private set; }

    private HexSortMaterialLibrary materialLibrary;

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

        if (!ResolveReferences())
        {
            return;
        }

        materialLibrary = new HexSortMaterialLibrary();
        pourStream.Initialize(materialLibrary);
        EnsureGlassInitialization();

        cameraController.Initialize(inputManager, mainCamera, boardPivot, initialZoom);
        LiquidColorId[][] layouts = MatchLayoutsToGlasses(CreateStartingLayouts());
        boardController.Initialize(inputManager, mainCamera, pourStream, glasses, layouts, boardExtents, boardPivot);

        IsInitialized = true;
        Debug.Log("HexSortManager initialized the authored Hex Sort scene.");
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
        if (levelData != null)
        {
            return Mathf.Max(1, levelData.capacity);
        }
        return Mathf.Max(1, glassCapacity);
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
        if (levelData != null && levelData.GlassCount > 0)
        {
            int count = levelData.GlassCount;
            LiquidColorId[][] result = new LiquidColorId[count][];
            for (int i = 0; i < count; i++)
            {
                result[i] = levelData.GetGlassUnits(i);
            }
            return result;
        }

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
