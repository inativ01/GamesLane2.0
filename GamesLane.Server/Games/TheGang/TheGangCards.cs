namespace GamesLane.Server.Games.TheGang;

/// <summary>
/// Challenge cards make a heist harder; specialist cards help.
/// </summary>
public enum TheGangCardKind
{
    Challenge,
    Specialist,
}

/// <summary>
/// What the group agrees on for a card: just to use it now (<see cref="Use"/>), a player, a player and a card value, a
/// card value, or a hand ranking.
/// </summary>
public enum TheGangChoice
{
    None,
    Use,
    Player,
    PlayerAndRank,
    Rank,
    Ranking,
}

/// <summary>
/// A challenge or specialist card of the advanced game: its key (used in options), name, what it does, and what the
/// group must agree on for it (<see cref="TheGangChoice.None"/> if nothing). The full texts are in <c>docs/the-gang/</c>.
/// </summary>
public record TheGangCard(string Key, string Name, TheGangCardKind Kind, string Text, TheGangChoice Choice = TheGangChoice.None);

/// <summary>
/// The challenge and specialist cards that are available so far, and which cards are never used in the same heist.
/// </summary>
public static class TheGangCards
{
    public const string QuickAccess = "quick-access";
    public const string NoiseSensors = "noise-sensors";
    public const string MotionDetector = "motion-detector";
    public const string RetinaScan = "retina-scan";
    public const string HastyGetaway = "hasty-getaway";
    public const string VentilationShaft = "ventilation-shaft";
    public const string LaserTripwires = "laser-tripwires";
    public const string Blackout = "blackout";
    public const string FingerprintScan = "fingerprint-scan";
    public const string SecurityCameras = "security-cameras";
    public const string Informant = "informant";
    public const string GetawayDriver = "getaway-driver";
    public const string Investor = "investor";
    public const string Mastermind = "mastermind";
    public const string Hacker = "hacker";
    public const string Coordinator = "coordinator";
    public const string Jack = "jack";
    public const string MathWhiz = "math-whiz";
    public const string ConArtist = "con-artist";
    public const string Muscle = "muscle";

    /// <summary>
    /// The challenge cards, in the order shown in the options.
    /// </summary>
    public static IReadOnlyList<TheGangCard> Challenges { get; } =
    [
        new(QuickAccess, "Quick Access", TheGangCardKind.Challenge,
            "No white chips: the heist starts with 3 shared cards and the yellow chips."),
        new(NoiseSensors, "Noise Sensors", TheGangCardKind.Challenge,
            "The 1-star white, yellow and orange chips are locked: whoever takes one first keeps it."),
        new(MotionDetector, "Motion Detector", TheGangCardKind.Challenge,
            "If the first 3 shared cards include a J, Q or K, whoever has the 1-star white chip gets new private cards."),
        new(RetinaScan, "Retina Scan", TheGangCardKind.Challenge,
            "Before the strongest hand is revealed, the others agree on a card value its player holds. If wrong, the heist fails.",
            TheGangChoice.Rank),
        new(HastyGetaway, "Hasty Getaway", TheGangCardKind.Challenge,
            "No orange chips: after the yellow stage, the red stage starts with all 5 shared cards."),
        new(VentilationShaft, "Ventilation Shaft", TheGangCardKind.Challenge,
            "The highest white, yellow and orange chips are locked: whoever takes one first keeps it."),
        new(LaserTripwires, "Laser Tripwires", TheGangCardKind.Challenge,
            "If none of the first 3 shared cards is a J, Q or K, whoever has the highest white chip gets new private cards."),
        new(Blackout, "Blackout", TheGangCardKind.Challenge,
            "Each stage's chips are removed when the next stage starts, so remember who had which chips."),
        new(FingerprintScan, "Fingerprint Scan", TheGangCardKind.Challenge,
            "Before the strongest hand is revealed, the others agree on its hand ranking. If wrong, the heist fails.",
            TheGangChoice.Ranking),
        new(SecurityCameras, "Security Cameras", TheGangCardKind.Challenge,
            "Everyone gets 3 private cards; the best 5 of your 3 cards and the 5 shared cards count."),
    ];

    /// <summary>
    /// The specialist cards, in the order shown in the options.
    /// </summary>
    public static IReadOnlyList<TheGangCard> Specialists { get; } =
    [
        new(Informant, "Informant", TheGangCardKind.Specialist,
            "The group chooses a player, who secretly shows one of their cards to one other player.", TheGangChoice.Player),
        new(GetawayDriver, "Getaway Driver", TheGangCardKind.Specialist,
            "The group chooses a player; everyone sees the ranking of that player's current hand (e.g. a pair).", TheGangChoice.Player),
        new(Investor, "Investor", TheGangCardKind.Specialist,
            "Everyone's number of face cards (J, Q, K) is shown to all.", TheGangChoice.Use),
        new(Mastermind, "Mastermind", TheGangCardKind.Specialist,
            "The group chooses a player and a card value; everyone sees how many of that value the player has.",
            TheGangChoice.PlayerAndRank),
        new(Hacker, "Hacker", TheGangCardKind.Specialist,
            "The group chooses a player, who draws a card from the deck, then discards one of their cards.", TheGangChoice.Player),
        new(Coordinator, "Coordinator", TheGangCardKind.Specialist,
            "Everyone picks one of their cards and passes it to the next player, all at once.", TheGangChoice.Use),
        new(Jack, "Jack", TheGangCardKind.Specialist,
            "The group chooses a player, who gets the \"Jack\" card (a J without a suit: never part of a flush) and discards one card.",
            TheGangChoice.Player),
        new(MathWhiz, "Math Whiz", TheGangCardKind.Specialist,
            "Everyone's total card value is shown to all (2–10 as numbered, J, Q and K 10, A 11).", TheGangChoice.Use),
        new(ConArtist, "Con Artist", TheGangCardKind.Specialist,
            "Look at your cards: once everyone has, all private cards are shuffled and dealt again.", TheGangChoice.Use),
        new(Muscle, "Muscle", TheGangCardKind.Specialist,
            "The group chooses a player, whose hand beats every other hand of the same ranking at the reveal.", TheGangChoice.Player),
    ];

    /// <summary>
    /// Pairs of cards where one cancels the other: Quick Access removes the white chips, so Motion Detector and Laser
    /// Tripwires (which depend on who holds a white chip) have no effect. They are drawn together unless the "avoid
    /// clashing cards" option is on.
    /// </summary>
    public static IReadOnlyList<(string, string)> Clashes { get; } =
    [
        (QuickAccess, MotionDetector),
        (QuickAccess, LaserTripwires),
    ];

    /// <summary>
    /// The card with this key, or null.
    /// </summary>
    public static TheGangCard? Find(string key) =>
        Challenges.Concat(Specialists).FirstOrDefault(c => c.Key == key);

    /// <summary>
    /// Whether one of two cards cancels the other.
    /// </summary>
    public static bool Clash(string a, string b) => Clashes.Contains((a, b)) || Clashes.Contains((b, a));
}
