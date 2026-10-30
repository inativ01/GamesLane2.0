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
/// A challenge or specialist card of the advanced game: its key (used in options), name and what it does.
/// The full texts are in <c>docs/the-gang/</c>.
/// </summary>
public record TheGangCard(string Key, string Name, TheGangCardKind Kind, string Text);

/// <summary>
/// The challenge and specialist cards that are available so far, and which cards are never used in the same heist.
/// </summary>
public static class TheGangCards
{
    public const string QuickAccess = "quick-access";
    public const string NoiseSensors = "noise-sensors";
    public const string MotionDetector = "motion-detector";
    public const string HastyGetaway = "hasty-getaway";
    public const string VentilationShaft = "ventilation-shaft";
    public const string LaserTripwires = "laser-tripwires";
    public const string Blackout = "blackout";
    public const string SecurityCameras = "security-cameras";
    public const string Investor = "investor";
    public const string MathWhiz = "math-whiz";

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
        new(HastyGetaway, "Hasty Getaway", TheGangCardKind.Challenge,
            "No orange chips: after the yellow stage, the red stage starts with all 5 shared cards."),
        new(VentilationShaft, "Ventilation Shaft", TheGangCardKind.Challenge,
            "The highest white, yellow and orange chips are locked: whoever takes one first keeps it."),
        new(LaserTripwires, "Laser Tripwires", TheGangCardKind.Challenge,
            "If none of the first 3 shared cards is a J, Q or K, whoever has the highest white chip gets new private cards."),
        new(Blackout, "Blackout", TheGangCardKind.Challenge,
            "Each stage's chips are removed when the next stage starts, so remember who had which chips."),
        new(SecurityCameras, "Security Cameras", TheGangCardKind.Challenge,
            "Everyone gets 3 private cards; the best 5 of your 3 cards and the 5 shared cards count."),
    ];

    /// <summary>
    /// The specialist cards, in the order shown in the options.
    /// </summary>
    public static IReadOnlyList<TheGangCard> Specialists { get; } =
    [
        new(Investor, "Investor", TheGangCardKind.Specialist,
            "Everyone's number of face cards (J, Q, K) is shown to all."),
        new(MathWhiz, "Math Whiz", TheGangCardKind.Specialist,
            "Everyone's total card value is shown to all (2–10 as numbered, J, Q and K 10, A 11)."),
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
