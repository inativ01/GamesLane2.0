namespace GamesLane.Server.Games.Dominoes;

public partial class DominoesGame
{
    /// <summary>
    /// Shuffles the set (in Mexican Train, without the round's engine) and deals each player their tiles; the rest is the
    /// boneyard. Then the round's starter plays (see the class).
    /// </summary>
    private void Deal()
    {
        RoundNumber++;
        var set = DominoesTiles.Set(Rules.Highest).ToList();
        Engine = Rules.IsLine ? null : Rules.EngineFor(RoundNumber);
        if (Engine is { } engine)
        {
            set.Remove(new Tile(engine, engine));
        }

        var tiles = set.ToArray();
        _shuffle(tiles);
        var size = Rules.HandSize(Players);
        for (var p = 0; p < Players; p++)
        {
            _hands[p].Clear();
            _hands[p].AddRange(DominoesTiles.Sorted(tiles.Skip(p * size).Take(size)));
            _trains[p].Clear();
        }

        _boneyard.Clear();
        _boneyard.AddRange(tiles.Skip(Players * size));
        _line.Clear();
        _mexican.Clear();
        Array.Fill(_markers, false);
        LastAction = null;
        HasDrawn = false;
        Phase = DominoesPhase.Playing;
        Actions++;
        _starter = RoundNumber == 1 ? FirstStarter()
            : Rules.IsLine && LastResult?.Domino is { } domino ? domino
            : (_starter + 1) % Players;
        Turn = _starter;
    }

    /// <summary>Round 1: in the line styles, the holder of the highest double (player 0 if nobody holds one); otherwise player 0.</summary>
    private int FirstStarter()
    {
        if (!Rules.IsLine)
        {
            return 0;
        }

        for (var value = Rules.Highest; value >= 0; value--)
        {
            var holder = Array.FindIndex(_hands, h => h.Contains(new Tile(value, value)));
            if (holder >= 0)
            {
                return holder;
            }
        }

        return 0;
    }

    private void AddToHand(int player, Tile tile)
    {
        var hand = DominoesTiles.Sorted(_hands[player].Append(tile)).ToList();
        _hands[player].Clear();
        _hands[player].AddRange(hand);
    }

    private void NextTurn()
    {
        Turn = (Turn + 1) % Players;
        HasDrawn = false;
    }

    /// <summary>The player's turn ends without a play; in Mexican Train, their train gets a marker.</summary>
    private void EndTurnWithout(int player, DominoesActionKind kind)
    {
        if (!Rules.IsLine)
        {
            _markers[player] = true;
        }

        Actions++;
        LastAction = new DominoesAction(player, kind);
        NextTurn();
        CheckBlocked();
    }

    /// <summary>
    /// Ends the round once nobody can play any more: the boneyard is empty (or, in Block, not drawn from) and no player
    /// holds a tile that matches (in Mexican Train, on any train).
    /// </summary>
    private void CheckBlocked()
    {
        if (Phase == DominoesPhase.Playing && (!Rules.Draws || _boneyard.Count == 0)
            && Enumerable.Range(0, Players).All(p => MovesOf(p, openTrains: true).Count == 0))
        {
            EndRound(null);
        }
    }

    /// <summary>
    /// The round ends: <paramref name="domino"/> played their last tile, or (null) nobody can play. In the line styles, the
    /// side that played out, or after a block the side with the fewest pips (if only one), scores the pips in the other
    /// sides' hands (in All Fives, to the nearest 5); once a side reaches the target, the highest score wins. In Mexican
    /// Train, each player gains the pips in their hand; after the last round, the lowest total wins.
    /// </summary>
    private void EndRound(int? domino)
    {
        var pips = _hands.Select(DominoesTiles.Pips).ToArray();
        var points = new int[Sides];
        if (Rules.IsLine)
        {
            var sidePips = Enumerable.Range(0, Sides).Select(s => Enumerable.Range(0, Players).Where(p => SideOf(p) == s).Sum(p => pips[p])).ToArray();
            int? winner = domino is { } d ? SideOf(d)
                : sidePips.Count(p => p == sidePips.Min()) == 1 ? Array.IndexOf(sidePips, sidePips.Min()) : null;
            if (winner is { } side)
            {
                var total = sidePips.Where((_, s) => s != side).Sum();
                points[side] = Rules.Style == DominoesStyle.AllFives ? (total + 2) / 5 * 5 : total;
            }
        }
        else
        {
            points = pips;
        }

        for (var s = 0; s < Sides; s++)
        {
            _scores[s] += points[s];
        }

        Actions++;
        LastResult = new DominoesRoundResult(RoundNumber, domino, domino is null, [.. _hands.Select(h => h.ToArray())], pips, points, [.. _scores]);
        if (Rules.IsLine ? _scores.Max() >= Rules.Target : RoundNumber >= Rules.TrainRounds)
        {
            EndGame(highestWins: Rules.IsLine);
            return;
        }

        Phase = DominoesPhase.RoundOver;
    }

    /// <summary>The game ends: the sides with the highest (or lowest) score win.</summary>
    private void EndGame(bool highestWins)
    {
        var best = highestWins ? _scores.Max() : _scores.Min();
        _winners.Clear();
        _winners.AddRange(Enumerable.Range(0, Sides).Where(s => _scores[s] == best));
        Phase = DominoesPhase.GameOver;
    }

    /// <summary>
    /// Checks that the game is waiting for <paramref name="player"/> to play.
    /// </summary>
    private bool TryCheckTurn(int player, out string? error)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(player);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(player, Players);
        error = Phase == DominoesPhase.GameOver ? "The game is over."
            : Phase == DominoesPhase.RoundOver ? "This round is over."
            : player != Turn ? "It is not your turn."
            : null;
        return error is null;
    }
}
