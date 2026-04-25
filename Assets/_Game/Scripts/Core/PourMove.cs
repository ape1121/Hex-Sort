public readonly struct PourMove
{
    public static readonly PourMove Invalid = new PourMove(-1, -1, LiquidColorId.None, 0);

    public PourMove(int sourceIndex, int targetIndex, LiquidColorId color, int unitCount)
    {
        SourceIndex = sourceIndex;
        TargetIndex = targetIndex;
        Color = color;
        UnitCount = unitCount;
    }

    public int SourceIndex { get; }

    public int TargetIndex { get; }

    public LiquidColorId Color { get; }

    public int UnitCount { get; }

    public bool IsValid => UnitCount > 0;
}
