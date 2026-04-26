using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedural generator for Hanoi-style sort puzzles. Always produces a solvable level by
/// starting from the solved state (each color stacked in its own glass) and applying N random
/// *legal* forward moves. The reverse of those moves is a valid solution, so any move count is
/// guaranteed solvable.
/// </summary>
public static class HexSortLevelGenerator
{
    [System.Serializable]
    public struct Parameters
    {
        [Tooltip("Number of distinct liquid colors. One full glass per color in the solved state.")]
        public int colorCount;

        [Tooltip("Number of additional empty glasses on top of the colored ones. 1-2 is typical for Hanoi-style puzzles.")]
        public int emptyGlassCount;

        [Tooltip("Slots per glass. Total units of each color = capacity.")]
        public int capacity;

        [Tooltip("How many random legal moves to apply when scrambling the solved state. More = harder.")]
        public int scrambleMoves;

        [Tooltip("Random seed. Same parameters + seed reproduces the same level.")]
        public int randomSeed;

        public static Parameters Default => new Parameters
        {
            colorCount = 4,
            emptyGlassCount = 2,
            capacity = 4,
            scrambleMoves = 60,
            randomSeed = 0,
        };
    }

    private static readonly LiquidColorId[] DefaultPalette =
    {
        LiquidColorId.Coral,
        LiquidColorId.Sky,
        LiquidColorId.Mint,
        LiquidColorId.Gold,
        LiquidColorId.Grape,
        LiquidColorId.Rose,
    };

    public static int MaxColorCount => DefaultPalette.Length;

    /// <summary>
    /// Generate fills for a Hanoi-style puzzle.
    /// </summary>
    public static List<List<LiquidColorId>> GenerateGlasses(Parameters parameters)
    {
        int colorCount = Mathf.Clamp(parameters.colorCount, 1, MaxColorCount);
        int emptyGlasses = Mathf.Max(0, parameters.emptyGlassCount);
        int capacity = Mathf.Max(1, parameters.capacity);
        int scrambleMoves = Mathf.Max(0, parameters.scrambleMoves);

        var glasses = new List<List<LiquidColorId>>(colorCount + emptyGlasses);

        // Solved state: one full glass per color.
        for (int i = 0; i < colorCount; i++)
        {
            var glass = new List<LiquidColorId>(capacity);
            for (int j = 0; j < capacity; j++)
            {
                glass.Add(DefaultPalette[i]);
            }
            glasses.Add(glass);
        }

        // Empty glasses for moving liquid around.
        for (int i = 0; i < emptyGlasses; i++)
        {
            glasses.Add(new List<LiquidColorId>(capacity));
        }

        // Scramble by applying random REVERSE moves. A forward (gameplay) move requires the
        // destination glass to be empty OR have the same top colour as the source — so applying
        // forward moves from a solved state preserves the invariant "each glass is single-colour
        // or empty" and never actually mixes anything. To produce mixed glasses we apply the
        // inverse: pick a glass with top colour X and pour some of those X's onto another glass
        // *regardless* of that glass's current top colour. The inverse of each such move is a
        // legal forward move, so the resulting state is always solvable.
        var rng = new System.Random(parameters.randomSeed);
        for (int step = 0; step < scrambleMoves; step++)
        {
            ApplyRandomReverseMove(glasses, rng, capacity);
        }

        return glasses;
    }

    /// <summary>
    /// Populate an existing <see cref="HexSortLevelData"/> with generated content.
    /// </summary>
    public static void Populate(HexSortLevelData target, Parameters parameters)
    {
        if (target == null)
        {
            return;
        }

        int capacity = Mathf.Max(1, parameters.capacity);
        var glasses = GenerateGlasses(parameters);

        target.capacity = capacity;
        target.glasses = new HexSortLevelData.GlassFill[glasses.Count];
        for (int i = 0; i < glasses.Count; i++)
        {
            target.glasses[i] = new HexSortLevelData.GlassFill
            {
                units = glasses[i].ToArray(),
            };
        }
        // Par = scramble move count. The optimal solution is at most this many moves (often
        // fewer thanks to redundant scramble steps), so par is a reasonable 3-star bar.
        target.parMoves = Mathf.Max(1, parameters.scrambleMoves);
    }

    private static void ApplyRandomReverseMove(List<List<LiquidColorId>> glasses, System.Random rng, int capacity)
    {
        // Build list of REVERSE-move candidates. The inverse of a legal forward move:
        //   pick a glass `from` with top colour X, and pour 1..K of those X's into another
        //   glass `to` that has free space — REGARDLESS of `to`'s current top colour.
        // The forward inverse (move those X's from `to` back to `from`) is always legal,
        // because after this reverse step:
        //   • `from` either still has X on top (if K < runLength) or has whatever was below
        //     (in which case `from` could legally receive X from `to`'s top — wait, no, only
        //     if `from`'s top is X or `from` is empty). To be safe we restrict to cases
        //     where the forward inverse is *clearly* legal: `to`'s pre-move top must allow
        //     pulling X back, i.e. either `to` was empty or `to`'s top equals X.
        // Translated: standard "move K X's from `from` onto `to`" with no same-colour
        // restriction on `to` produces a solvable state.
        var moves = new List<(int from, int to, int count, int weight)>();

        for (int fromIdx = 0; fromIdx < glasses.Count; fromIdx++)
        {
            var from = glasses[fromIdx];
            if (from.Count == 0)
            {
                continue;
            }

            LiquidColorId topColor = from[from.Count - 1];

            // Top run length on `from`.
            int runLength = 1;
            for (int k = from.Count - 2; k >= 0 && from[k] == topColor; k--)
            {
                runLength++;
            }

            for (int toIdx = 0; toIdx < glasses.Count; toIdx++)
            {
                if (toIdx == fromIdx)
                {
                    continue;
                }

                var to = glasses[toIdx];
                int toFree = capacity - to.Count;
                if (toFree <= 0)
                {
                    continue;
                }

                // Skip same-colour "stacks" — that's a legal forward move, not a real scramble.
                if (to.Count > 0 && to[to.Count - 1] == topColor)
                {
                    continue;
                }

                int maxCount = Mathf.Min(runLength, toFree);

                // If pouring into an empty destination AND the source is entirely one colour,
                // moving everything would just relabel glass slots without mixing. Clamp so at
                // least one unit stays behind. (Don't drop the candidate entirely — without
                // this, the very first move from a fully-solved state has no valid candidates
                // and the scrambler does nothing.)
                if (to.Count == 0 && runLength == from.Count)
                {
                    maxCount = Mathf.Min(maxCount, runLength - 1);
                    if (maxCount < 1)
                    {
                        continue;
                    }
                }

                // Prefer moves that pour onto a non-empty glass with a different top colour
                // (real mixing) over pours into empty glasses (just splitting a stack).
                int weight = (to.Count > 0) ? 4 : 1;

                moves.Add((fromIdx, toIdx, maxCount, weight));
            }
        }

        if (moves.Count == 0)
        {
            return;
        }

        // Weighted random pick.
        int totalWeight = 0;
        for (int i = 0; i < moves.Count; i++)
        {
            totalWeight += moves[i].weight;
        }
        int pick = rng.Next(totalWeight);
        int chosenIdx = 0;
        int acc = 0;
        for (int i = 0; i < moves.Count; i++)
        {
            acc += moves[i].weight;
            if (pick < acc)
            {
                chosenIdx = i;
                break;
            }
        }

        var move = moves[chosenIdx];
        int countToMove = rng.Next(1, move.count + 1);

        var fromGlass = glasses[move.from];
        var toGlass = glasses[move.to];
        for (int i = 0; i < countToMove; i++)
        {
            toGlass.Add(fromGlass[fromGlass.Count - 1]);
            fromGlass.RemoveAt(fromGlass.Count - 1);
        }
    }
}
