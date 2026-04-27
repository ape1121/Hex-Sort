using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Ape.Data
{
    [MovedFrom(false, sourceNamespace: "")]
    [CreateAssetMenu(fileName = "GameConfig", menuName = "HexSort/Configs/GameConfig")]
    public class GameConfig : ScriptableObject
    {
        [Tooltip("All puzzle levels in play order.")]
        public LevelsConfig Levels;

        [Tooltip("Default board layout & glass-spawning settings.")]
        public BoardConfig Board;

        [Tooltip("Camera framing, pan, zoom, and orbit feel.")]
        public CameraConfig Camera;

        [Tooltip("Liquid-colour palette mapping LiquidColorId → renderable Color.")]
        public ColorsConfig Colors;
    }
}
