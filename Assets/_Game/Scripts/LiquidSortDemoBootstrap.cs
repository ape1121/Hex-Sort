using System.Collections.Generic;
using UnityEngine;

public sealed class LiquidSortDemoBootstrap : MonoBehaviour
{
    private bool isBuilt;

    private void Awake()
    {
        BuildDemo();
    }

    private void BuildDemo()
    {
        if (isBuilt)
        {
            return;
        }

        HexSortMaterialLibrary materials = new HexSortMaterialLibrary();

        HexSortInputManager inputManager = GetComponent<HexSortInputManager>();
        if (inputManager == null)
        {
            inputManager = gameObject.AddComponent<HexSortInputManager>();
        }

        HexSortBoardController boardController = GetComponent<HexSortBoardController>();
        if (boardController == null)
        {
            boardController = gameObject.AddComponent<HexSortBoardController>();
        }

        Camera demoCamera = SetupCamera();
        HexSortCameraController cameraController = demoCamera.GetComponent<HexSortCameraController>();
        if (cameraController == null)
        {
            cameraController = demoCamera.gameObject.AddComponent<HexSortCameraController>();
        }

        Transform runtimeRoot = RebuildRuntimeRoot();
        Transform environmentRoot = new GameObject("Environment").transform;
        environmentRoot.SetParent(runtimeRoot, false);

        Transform glassRoot = new GameObject("Glasses").transform;
        glassRoot.SetParent(runtimeRoot, false);

        Transform streamRoot = new GameObject("Stream").transform;
        streamRoot.SetParent(runtimeRoot, false);

        SetupLighting(runtimeRoot);
        BuildEnvironment(environmentRoot, materials);

        List<HexSortGlassController> glasses = BuildGlasses(glassRoot, materials);
        PourStreamView pourStream = BuildPourStream(streamRoot, materials);

        Vector3 boardPivot = new Vector3(0f, 0.95f, 0f);
        cameraController.Initialize(inputManager, demoCamera, boardPivot, 9.4f);
        boardController.Initialize(inputManager, demoCamera, pourStream, glasses, CreateStartingLayouts(), new Vector2(5.5f, 1.85f), boardPivot);

        isBuilt = true;
    }

    private Transform RebuildRuntimeRoot()
    {
        Transform existing = transform.Find("RuntimeDemo");
        if (existing != null)
        {
            DestroyImmediate(existing.gameObject);
        }

        Transform runtimeRoot = new GameObject("RuntimeDemo").transform;
        runtimeRoot.SetParent(transform, false);
        return runtimeRoot;
    }

    private Camera SetupCamera()
    {
        Camera demoCamera = Camera.main;
        if (demoCamera == null)
        {
            demoCamera = FindFirstObjectByType<Camera>();
        }

        if (demoCamera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            demoCamera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

        demoCamera.clearFlags = CameraClearFlags.SolidColor;
        demoCamera.backgroundColor = new Color(0.93f, 0.97f, 1f);
        demoCamera.fieldOfView = 36f;
        demoCamera.nearClipPlane = 0.1f;
        demoCamera.farClipPlane = 60f;
        demoCamera.allowHDR = true;
        return demoCamera;
    }

    private void SetupLighting(Transform runtimeRoot)
    {
        Light existingDirectional = FindDirectionalLight();
        if (existingDirectional == null)
        {
            GameObject lightObject = new GameObject("Directional Light");
            existingDirectional = lightObject.AddComponent<Light>();
            existingDirectional.type = LightType.Directional;
        }

        existingDirectional.transform.position = new Vector3(0f, 8f, -2f);
        existingDirectional.transform.rotation = Quaternion.Euler(42f, -28f, 0f);
        existingDirectional.color = new Color(1f, 0.97f, 0.93f);
        existingDirectional.intensity = 1.4f;
        existingDirectional.shadows = LightShadows.Soft;

        GameObject fillLightObject = new GameObject("Demo Fill Light");
        fillLightObject.transform.SetParent(runtimeRoot, false);
        fillLightObject.transform.position = new Vector3(0f, 3.5f, -4f);
        fillLightObject.transform.rotation = Quaternion.Euler(28f, 180f, 0f);
        Light fillLight = fillLightObject.AddComponent<Light>();
        fillLight.type = LightType.Directional;
        fillLight.color = new Color(0.73f, 0.83f, 1f);
        fillLight.intensity = 0.52f;
    }

    private void BuildEnvironment(Transform environmentRoot, HexSortMaterialLibrary materials)
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.SetParent(environmentRoot, false);
        floor.transform.localPosition = new Vector3(0f, -0.02f, 0f);
        floor.transform.localScale = new Vector3(1.4f, 1f, 0.9f);
        floor.GetComponent<Renderer>().sharedMaterial = materials.TableMaterial;

        GameObject stage = GameObject.CreatePrimitive(PrimitiveType.Cube);
        stage.name = "Stage";
        stage.transform.SetParent(environmentRoot, false);
        stage.transform.localPosition = new Vector3(0f, 0.27f, 0f);
        stage.transform.localScale = new Vector3(12f, 0.54f, 4.4f);
        stage.GetComponent<Renderer>().sharedMaterial = materials.BoardMaterial;

        GameObject backWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        backWall.name = "Backdrop";
        backWall.transform.SetParent(environmentRoot, false);
        backWall.transform.localPosition = new Vector3(0f, 4.4f, 5.25f);
        backWall.transform.localScale = new Vector3(22f, 9f, 0.35f);
        backWall.GetComponent<Renderer>().sharedMaterial = materials.BackdropMaterial;

        GameObject accent = GameObject.CreatePrimitive(PrimitiveType.Cube);
        accent.name = "AccentBand";
        accent.transform.SetParent(environmentRoot, false);
        accent.transform.localPosition = new Vector3(0f, 2.65f, 5.02f);
        accent.transform.localScale = new Vector3(8.6f, 0.15f, 0.08f);
        accent.GetComponent<Renderer>().sharedMaterial = materials.AccentMaterial;
    }

    private List<HexSortGlassController> BuildGlasses(Transform glassRoot, HexSortMaterialLibrary materials)
    {
        LiquidColorId[][] startingLayouts = CreateStartingLayouts();
        List<HexSortGlassController> glasses = new List<HexSortGlassController>(startingLayouts.Length);

        const float glassSpacing = 1.9f;
        float startX = -((startingLayouts.Length - 1) * glassSpacing) * 0.5f;

        for (int i = 0; i < startingLayouts.Length; i++)
        {
            GameObject glassObject = new GameObject("Glass_" + i);
            glassObject.transform.SetParent(glassRoot, false);
            glassObject.transform.localPosition = new Vector3(startX + (i * glassSpacing), 0.54f, 0f);

            HexSortGlassController glass = glassObject.AddComponent<HexSortGlassController>();
            glass.Initialize(i, 4, materials);
            glasses.Add(glass);
        }

        return glasses;
    }

    private PourStreamView BuildPourStream(Transform streamRoot, HexSortMaterialLibrary materials)
    {
        GameObject streamObject = new GameObject("PourStream");
        streamObject.transform.SetParent(streamRoot, false);

        PourStreamView streamView = streamObject.AddComponent<PourStreamView>();
        streamView.Initialize(materials);
        return streamView;
    }

    private static LiquidColorId[][] CreateStartingLayouts()
    {
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

    private static Light FindDirectionalLight()
    {
        Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i].type == LightType.Directional)
            {
                return lights[i];
            }
        }

        return null;
    }
}
