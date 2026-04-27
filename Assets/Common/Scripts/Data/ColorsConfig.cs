using System.Collections.Generic;
using UnityEngine;

namespace Ape.Data
{
    /// <summary>
    /// Maps each <see cref="LiquidColorId"/> to its renderable colour. Authored once and shared
    /// by every system that needs to draw liquid (material library, pour stream, debug UI).
    /// </summary>
    [CreateAssetMenu(fileName = "ColorsConfig", menuName = "HexSort/Configs/ColorsConfig")]
    public class ColorsConfig : ScriptableObject
    {
        [System.Serializable]
        public struct LiquidColorEntry
        {
            public LiquidColorId Id;
            public Color Color;
        }

        [Tooltip("One entry per LiquidColorId. Missing entries fall back to white.")]
        public LiquidColorEntry[] Entries =
        {
            new LiquidColorEntry { Id = LiquidColorId.Coral, Color = new Color(0.96f, 0.39f, 0.35f) },
            new LiquidColorEntry { Id = LiquidColorId.Sky,   Color = new Color(0.25f, 0.67f, 0.98f) },
            new LiquidColorEntry { Id = LiquidColorId.Mint,  Color = new Color(0.34f, 0.88f, 0.69f) },
            new LiquidColorEntry { Id = LiquidColorId.Gold,  Color = new Color(0.98f, 0.79f, 0.28f) },
            new LiquidColorEntry { Id = LiquidColorId.Grape, Color = new Color(0.58f, 0.41f, 0.89f) },
            new LiquidColorEntry { Id = LiquidColorId.Rose,  Color = new Color(0.97f, 0.50f, 0.71f) },
        };

        private Dictionary<LiquidColorId, Color> lookup;

        public Color GetColor(LiquidColorId id)
        {
            if (id == LiquidColorId.None)
            {
                return Color.white;
            }

            EnsureLookup();
            return lookup.TryGetValue(id, out Color c) ? c : Color.white;
        }

        public bool TryGetColor(LiquidColorId id, out Color color)
        {
            EnsureLookup();
            return lookup.TryGetValue(id, out color);
        }

        private void EnsureLookup()
        {
            if (lookup != null && lookup.Count == (Entries != null ? Entries.Length : 0))
            {
                return;
            }

            lookup = new Dictionary<LiquidColorId, Color>();
            if (Entries == null)
            {
                return;
            }
            for (int i = 0; i < Entries.Length; i++)
            {
                lookup[Entries[i].Id] = Entries[i].Color;
            }
        }

        private void OnValidate()
        {
            lookup = null;
        }
    }
}
