using System.Collections.Generic;
using UnityEngine;

public sealed class GlassState
{
    private readonly List<LiquidColorId> units = new List<LiquidColorId>();

    public GlassState(int capacity)
    {
        Capacity = capacity;
    }

    public int Capacity { get; }

    public IReadOnlyList<LiquidColorId> Units => units;

    public int Count => units.Count;

    public bool IsEmpty => units.Count == 0;

    public bool IsFull => units.Count >= Capacity;

    public int FreeSpace => Capacity - units.Count;

    public LiquidColorId TopColor => units.Count == 0 ? LiquidColorId.None : units[units.Count - 1];

    public bool IsSolvedComplete
    {
        get
        {
            if (units.Count != Capacity || units.Count == 0)
            {
                return false;
            }

            LiquidColorId first = units[0];
            for (int i = 1; i < units.Count; i++)
            {
                if (units[i] != first)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public void SetUnits(IReadOnlyList<LiquidColorId> values)
    {
        units.Clear();
        for (int i = 0; i < values.Count; i++)
        {
            units.Add(values[i]);
        }
    }

    public bool CanReceive(LiquidColorId color)
    {
        if (color == LiquidColorId.None || FreeSpace <= 0)
        {
            return false;
        }

        return IsEmpty || TopColor == color;
    }

    public bool CanPourInto(GlassState target)
    {
        if (target == null || ReferenceEquals(this, target) || IsEmpty)
        {
            return false;
        }

        return target.CanReceive(TopColor);
    }

    public int GetTopRunLength()
    {
        if (IsEmpty)
        {
            return 0;
        }

        LiquidColorId color = TopColor;
        int runLength = 0;
        for (int i = units.Count - 1; i >= 0; i--)
        {
            if (units[i] != color)
            {
                break;
            }

            runLength++;
        }

        return runLength;
    }

    public int GetTransferCountInto(GlassState target, int maxUnits = int.MaxValue)
    {
        if (!CanPourInto(target))
        {
            return 0;
        }

        int runLength = GetTopRunLength();
        return Mathf.Min(runLength, Mathf.Min(target.FreeSpace, maxUnits));
    }

    public bool TryCreateMoveTo(GlassState target, int sourceIndex, int targetIndex, int maxUnits, out PourMove move)
    {
        int transferCount = GetTransferCountInto(target, maxUnits);
        if (transferCount <= 0)
        {
            move = PourMove.Invalid;
            return false;
        }

        move = new PourMove(sourceIndex, targetIndex, TopColor, transferCount);
        return true;
    }

    public void ApplyMoveTo(GlassState target, PourMove move)
    {
        if (target == null || !move.IsValid)
        {
            return;
        }

        for (int i = 0; i < move.UnitCount; i++)
        {
            target.Push(PopTop());
        }
    }

    private void Push(LiquidColorId color)
    {
        units.Add(color);
    }

    private LiquidColorId PopTop()
    {
        int lastIndex = units.Count - 1;
        LiquidColorId color = units[lastIndex];
        units.RemoveAt(lastIndex);
        return color;
    }
}
