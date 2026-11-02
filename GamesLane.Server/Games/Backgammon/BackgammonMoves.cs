namespace GamesLane.Server.Games.Backgammon;

/// <summary>
/// One checker moved by one die: from and to the mover's own point numbers (<see cref="BackgammonPosition.Bar"/> is 25,
/// <see cref="BackgammonPosition.Off"/> is 0).
/// </summary>
public record BackgammonStep(int From, int To, int Die);

/// <summary>
/// The moves the rules allow. A turn must use as many of the dice as possible (all four with doubles); if only one die
/// can be used, it must be the larger one when that is possible.
/// </summary>
public static class BackgammonMoves
{
    /// <summary>
    /// Every way <paramref name="player"/> can move one checker by <paramref name="die"/>: a checker on the bar must
    /// enter first; a checker may land on a point with at most one opponent checker (hitting it); and once all the
    /// player's checkers are home, a checker may bear off with the exact number, or with a higher number when no checker
    /// is on a higher point.
    /// </summary>
    public static IEnumerable<BackgammonStep> Steps(BackgammonPosition position, int player, int die)
    {
        if (position.Count(player, BackgammonPosition.Bar) > 0)
        {
            var entry = BackgammonPosition.Bar - die;
            if (position.OpponentCount(player, entry) <= 1)
            {
                yield return new BackgammonStep(BackgammonPosition.Bar, entry, die);
            }

            yield break;
        }

        var home = position.AllHome(player);
        for (var point = 24; point >= 1; point--)
        {
            if (position.Count(player, point) == 0)
            {
                continue;
            }

            var to = point - die;
            if (to >= 1)
            {
                if (position.OpponentCount(player, to) <= 1)
                {
                    yield return new BackgammonStep(point, to, die);
                }
            }
            else if (home && (to == 0 || Enumerable.Range(point + 1, 6 - point).All(p => position.Count(player, p) == 0)))
            {
                yield return new BackgammonStep(point, BackgammonPosition.Off, die);
            }
        }
    }

    /// <summary>
    /// The positions a complete, legal turn can end in, with the number of dice it uses (the same for all of them).
    /// </summary>
    public static (IReadOnlySet<string> Finals, int DiceUsed) Turn(BackgammonPosition position, int player, IReadOnlyList<int> dice)
    {
        var leaves = new Dictionary<string, (int Used, HashSet<int> FirstDice)>();
        Explore(position, player, [.. dice], 0, null, dice.Count, leaves, []);
        var used = leaves.Values.Max(l => l.Used);
        var finals = leaves.Where(l => l.Value.Used == used).ToList();
        var larger = dice.Max();
        if (used == 1 && dice.Distinct().Count() == 2 && finals.Any(f => f.Value.FirstDice.Contains(larger)))
        {
            finals = [.. finals.Where(f => f.Value.FirstDice.Contains(larger))];
        }

        return (finals.Select(f => f.Key).ToHashSet(), used);
    }

    /// <summary>
    /// Whether, from <paramref name="position"/>, using <paramref name="steps"/> more of <paramref name="dice"/> can end
    /// the turn in one of <paramref name="finals"/>.
    /// </summary>
    public static bool CanReach(
        BackgammonPosition position, int player, IReadOnlyList<int> dice, int steps, IReadOnlySet<string> finals)
    {
        if (steps == 0 || position.Count(player, BackgammonPosition.Off) == BackgammonPosition.Checkers)
        {
            return finals.Contains(position.Key);
        }

        foreach (var die in dice.Distinct())
        {
            var rest = Without(dice, die);
            foreach (var step in Steps(position, player, die))
            {
                if (CanReach(position.Apply(player, step.From, step.To), player, rest, steps - 1, finals))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// <paramref name="dice"/> with one <paramref name="die"/> removed.
    /// </summary>
    public static int[] Without(IReadOnlyList<int> dice, int die)
    {
        var list = dice.ToList();
        list.Remove(die);
        return [.. list];
    }

    /// <summary>
    /// Records every position where the turn can stop (no further die can be used), with how many dice it took and, for
    /// one-die turns, which die. A turn that bears off the last checker counts as using every die.
    /// </summary>
    private static void Explore(
        BackgammonPosition position,
        int player,
        int[] dice,
        int used,
        int? firstDie,
        int total,
        Dictionary<string, (int Used, HashSet<int> FirstDice)> leaves,
        HashSet<string> seen)
    {
        if (!seen.Add($"{position.Key}#{string.Join(",", dice)}#{firstDie}"))
        {
            return;
        }

        var moved = false;
        if (position.Count(player, BackgammonPosition.Off) < BackgammonPosition.Checkers)
        {
            foreach (var die in dice.Distinct())
            {
                var rest = Without(dice, die);
                foreach (var step in Steps(position, player, die))
                {
                    moved = true;
                    Explore(position.Apply(player, step.From, step.To), player, rest, used + 1, firstDie ?? die, total, leaves, seen);
                }
            }
        }

        if (moved)
        {
            return;
        }

        var count = position.Count(player, BackgammonPosition.Off) == BackgammonPosition.Checkers ? total : used;
        var key = position.Key;
        if (!leaves.TryGetValue(key, out var leaf) || leaf.Used < count)
        {
            leaf = (count, []);
        }

        if (leaf.Used == count && firstDie is { } first)
        {
            leaf.FirstDice.Add(first);
        }

        leaves[key] = leaf;
    }
}
