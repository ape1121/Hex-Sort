using Ape.Core;
using Ape.Game;
using Ape.Scenes;
using UnityEngine;

[RequireComponent(typeof(HexSortManager))]
public sealed class HexSortSceneBootstrapper : SceneBootstrapper
{
    [SerializeField] private Camera mainCamera;
    [SerializeField] private HexSortManager hexSortManager;

    protected override void BootstrapScene()
    {
        Camera sceneCamera = mainCamera != null ? mainCamera : Camera.main;
        if (sceneCamera == null)
        {
            Debug.LogError("HexSortSceneBootstrapper requires a scene camera reference.");
            return;
        }

        if (App.Game == null)
        {
            Debug.LogError("HexSortSceneBootstrapper requires App.Game to be initialized.");
            return;
        }

        App.Game.PrepareForSceneLoad();
        App.Game.BindScene(new GameSceneDependencies(sceneCamera));
        App.Game.StartGame();

        if (hexSortManager == null)
        {
            hexSortManager = GetComponent<HexSortManager>();
        }

        if (hexSortManager == null)
        {
            Debug.LogError("HexSortSceneBootstrapper requires a HexSortManager component on the same GameObject.");
            return;
        }

        hexSortManager.InitializeScene();
    }
}
