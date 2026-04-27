using UnityEngine;

namespace Ape.Data
{
    /// <summary>
    /// Ordered collection of all puzzle levels available in the game. Stored as a child
    /// ScriptableObject of <see cref="GameConfig"/> so it can be authored independently and
    /// referenced by name elsewhere.
    /// </summary>
    [CreateAssetMenu(fileName = "LevelsConfig", menuName = "HexSort/Configs/LevelsConfig")]
    public class LevelsConfig : ScriptableObject
    {
        [Tooltip("Levels in play order. Index 0 = first level, last index = final level.")]
        public HexSortLevelData[] Levels;

        public int LevelCount => Levels != null ? Levels.Length : 0;

        public HexSortLevelData GetLevel(int index)
        {
            if (Levels == null || index < 0 || index >= Levels.Length)
            {
                return null;
            }
            return Levels[index];
        }

        /// <summary>
        /// Returns the level for the given index, wrapping around if it exceeds the count.
        /// Useful for endless / loop-back behaviour.
        /// </summary>
        public HexSortLevelData GetLevelLooped(int index)
        {
            if (Levels == null || Levels.Length == 0)
            {
                return null;
            }
            int wrapped = ((index % Levels.Length) + Levels.Length) % Levels.Length;
            return Levels[wrapped];
        }
    }
}
