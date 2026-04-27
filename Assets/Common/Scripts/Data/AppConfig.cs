using Ape.Sounds;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ape.Data
{
    [MovedFrom(false, sourceNamespace: "")]
    [CreateAssetMenu(fileName = "AppConfig", menuName = "HexSort/Configs/AppConfig")]
    public class AppConfig : ScriptableObject
    {
        public GameConfig GameConfig;
        public AllSounds SoundsConfig;
        public string GameSceneName;
        public float SceneTransitionDuration = 1f;
    }
}
