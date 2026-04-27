using System;

namespace Ape.Profile
{
    [Serializable]
    public struct SaveData
    {
        public int Level;
        public int Cash;

        /// <summary>
        /// Best star rating earned per level, indexed by level index. Grows on demand. Null /
        /// short arrays from older saves are handled by the writer.
        /// </summary>
        public int[] LevelStars;
    }
}
