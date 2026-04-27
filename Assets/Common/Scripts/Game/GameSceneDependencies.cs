using UnityEngine;

namespace Ape.Game
{
    public readonly struct GameSceneDependencies
    {
        public Camera MainCamera { get; }

        public GameSceneDependencies(Camera mainCamera)
        {
            MainCamera = mainCamera;
        }
    }
}
