using System;
using Ape.Core;
using Ape.Data;
using UnityEngine;

namespace Ape.Game
{
    public sealed class GameManager : IManager
    {
        public GameConfig Config => App.Config.GameConfig;
        public GameSceneDependencies SceneDependencies { get; private set; }
        public bool IsInitialized { get; private set; }
        public bool IsSceneBound { get; private set; }
        public bool IsGameStarted { get; private set; }

        public void Initialize()
        {
            SceneDependencies = default;
            IsInitialized = true;
            IsSceneBound = false;
            IsGameStarted = false;
        }

        public void PrepareForSceneLoad()
        {
            if (!IsInitialized)
                throw new InvalidOperationException("GameManager must be initialized before preparing for scene load.");

            SceneDependencies = default;
            IsSceneBound = false;
            IsGameStarted = false;
        }

        public void BindScene(GameSceneDependencies sceneDependencies)
        {
            if (!IsInitialized)
                throw new InvalidOperationException("GameManager must be initialized before scene dependencies are bound.");

            if (sceneDependencies.MainCamera == null)
                throw new MissingReferenceException("Game scene dependencies require a main camera reference.");

            SceneDependencies = sceneDependencies;
            IsSceneBound = true;
        }

        public void StartGame()
        {
            if (!IsInitialized)
                throw new InvalidOperationException("GameManager must be initialized before the game can start.");

            if (!IsSceneBound)
                throw new InvalidOperationException("GameManager requires scene dependencies before the game can start.");

            IsGameStarted = true;
        }
    }
}
