namespace GamesLane.Server.Games.Dominoes;

public partial class DominoesGame
{
    /// <inheritdoc />
    public bool TryDraw(int player, out string? error)
    {
        if (!TryCheckTurn(player, out error))
        {
            return false;
        }

        error = !Rules.Draws ? "In Block, a player who cannot play passes."
            : Moves(player).Count > 0 ? "You can play a tile."
            : _boneyard.Count == 0 ? "The boneyard is empty."
            : !CanDraw(player) ? "You have already drawn. Play the tile you drew, or pass."
            : null;
        if (error is not null)
        {
            return false;
        }

        var tile = _boneyard[0];
        _boneyard.RemoveAt(0);
        AddToHand(player, tile);
        HasDrawn = true;
        Actions++;
        LastAction = new DominoesAction(player, DominoesActionKind.Drew);
        CheckBlocked();
        return true;
    }

    /// <inheritdoc />
    public bool TryPass(int player, out string? error)
    {
        if (!TryCheckTurn(player, out error))
        {
            return false;
        }

        error = Moves(player).Count > 0 ? "You can play a tile."
            : CanDraw(player) ? "Draw a tile first."
            : null;
        if (error is not null)
        {
            return false;
        }

        EndTurnWithout(player, DominoesActionKind.Passed);
        return true;
    }

    /// <inheritdoc />
    public bool TrySkip(int player, out string? error)
    {
        if (!TryCheckTurn(player, out error))
        {
            return false;
        }

        EndTurnWithout(player, DominoesActionKind.Skipped);
        return true;
    }

    /// <inheritdoc />
    public bool TryNextRound(int round, out string? error)
    {
        error = round < RoundNumber ? null
            : round > RoundNumber ? "There is no such round."
            : Phase == DominoesPhase.GameOver ? "The game is over."
            : Phase != DominoesPhase.RoundOver ? "This round is not finished yet."
            : null;
        if (error is not null || round < RoundNumber)
        {
            return error is null;
        }

        Deal();
        return true;
    }

    /// <summary>
    /// The plays <paramref name="player"/> could make: in the line styles, at either end of the line (any tile on an empty
    /// line, at the left); in Mexican Train, on their own train, the Mexican train and the trains with a marker (or, with
    /// <paramref name="openTrains"/>, every train), but only on a double left uncovered at the end of a train if there is one.
    /// </summary>
    private List<DominoesMove> MovesOf(int player, bool openTrains)
    {
        var hand = _hands[player];
        if (Rules.IsLine)
        {
            return _line.Count == 0
                ? [.. hand.Select(t => new DominoesMove(t, 0))]
                : [.. hand.Where(t => t.Has(_line[0].Left)).Select(t => new DominoesMove(t, 0)),
                    .. hand.Where(t => t.Has(_line[^1].Right)).Select(t => new DominoesMove(t, 1))];
        }

        var all = Enumerable.Range(0, Players).Append(MexicanTarget).ToList();
        var uncovered = all.FirstOrDefault(t => TrainOf(t) is [.., { IsDouble: true }], int.MinValue);
        List<int> targets = uncovered != int.MinValue ? [uncovered]
            : all.Where(t => t == player || t == MexicanTarget || openTrains || _markers[t]).ToList();
        return [.. targets.SelectMany(t => hand.Where(tile => tile.Has(EndOf(t))).Select(tile => new DominoesMove(tile, t)))];
    }

    private List<LaidTile> TrainOf(int target) => target == MexicanTarget ? _mexican : _trains[target];

    private int EndOf(int target) => TrainOf(target) is [.., var last] ? last.Right : Engine!.Value;

    /// <summary>Lays <paramref name="tile"/> at <paramref name="target"/> (see <see cref="TryPlay"/>), and returns it as laid.</summary>
    private LaidTile Lay(Tile tile, int target)
    {
        if (!Rules.IsLine)
        {
            var end = EndOf(target);
            var laid = new LaidTile(end, tile.Other(end));
            TrainOf(target).Add(laid);
            return laid;
        }

        if (_line.Count == 0)
        {
            _line.Add(new LaidTile(tile.Low, tile.High));
        }
        else if (target == 0)
        {
            _line.Insert(0, new LaidTile(tile.Other(_line[0].Left), _line[0].Left));
        }
        else
        {
            _line.Add(new LaidTile(_line[^1].Right, tile.Other(_line[^1].Right)));
        }

        return target == 0 ? _line[0] : _line[^1];
    }

    /// <summary>
    /// All Fives: the open ends of the line added up (a double at an end counts both halves; a single tile, its pips), if
    /// they make a multiple of 5; otherwise 0.
    /// </summary>
    private int FivesPoints()
    {
        var sum = _line.Count == 1 ? _line[0].Left + _line[0].Right
            : (_line[0].IsDouble ? 2 : 1) * _line[0].Left + (_line[^1].IsDouble ? 2 : 1) * _line[^1].Right;
        return sum > 0 && sum % 5 == 0 ? sum : 0;
    }
}
