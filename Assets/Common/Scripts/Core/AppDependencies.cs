using Ape.Scenes;
using Ape.Sounds;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ape.Core
{
    [System.Serializable]
    [MovedFrom(false, sourceNamespace: "")]
    public struct AppDependencies
    {
        public LoadingScreenView LoadingScreen;
        public SoundManager SoundManager;
    }
}
