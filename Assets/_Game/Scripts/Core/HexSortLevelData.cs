using UnityEngine;

[CreateAssetMenu(menuName = "HexSort/Level Data", fileName = "Level_")]
public sealed class HexSortLevelData : ScriptableObject
{
    [System.Serializable]
    public class GlassFill
    {
        [Tooltip("Liquid units stacked from bottom to top. Empty array = empty glass.")]
        public LiquidColorId[] units = new LiquidColorId[0];
    }

    [Tooltip("Number of unit slots each glass holds. Glass fills with more entries than this will be truncated.")]
    [Min(1)]
    public int capacity = 4;

    [Tooltip("Par (target) move count used for star grading. 3 stars at moves <= par, 2 stars at <= 1.5*par, 1 star otherwise. The generator sets this automatically; 0 disables grading (always 3 stars).")]
    [Min(0)]
    public int parMoves = 0;

    [Tooltip("Per-glass starting fill, in scene order (left-to-right by default). Empty entries become empty glasses.")]
    public GlassFill[] glasses = new GlassFill[0];

    public int GlassCount => glasses != null ? glasses.Length : 0;

    public LiquidColorId[] GetGlassUnits(int index)
    {
        if (glasses == null || index < 0 || index >= glasses.Length)
        {
            return new LiquidColorId[0];
        }

        LiquidColorId[] source = glasses[index].units;
        if (source == null || source.Length == 0)
        {
            return new LiquidColorId[0];
        }

        int length = Mathf.Min(source.Length, capacity);
        LiquidColorId[] result = new LiquidColorId[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = source[i];
        }
        return result;
    }
}
