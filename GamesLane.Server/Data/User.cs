namespace GamesLane.Server.Data;

/// <summary>
/// A saved user: a registered user (username and password) or a user who signs in with an external account.
/// Guests are not saved. Users who are inactive for 30 days are deleted.
/// </summary>
public class User
{
    /// <summary>
    /// The user's ID.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The username a registered user logs in with, as they typed it. Null for external accounts.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// <see cref="Username"/> in upper case, so usernames are unique regardless of case.
    /// </summary>
    public string? NormalizedUsername { get; set; }

    /// <summary>
    /// A salted hash of a registered user's password. Null for external accounts.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// The name shown to other players (1 to 30 characters).
    /// </summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// A built-in icon (an emoji), or the address of the user's uploaded icon (<c>/api/icons/{id}</c>).
    /// </summary>
    public required string Icon { get; set; }

    /// <summary>
    /// When the user was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the user last logged in or opened the site, updated at most once a day.
    /// </summary>
    public DateTimeOffset LastActiveAt { get; set; }

    /// <summary>
    /// The order of the games on the user's main screen: game names separated by commas, e.g. <c>Backgammon,TheGang</c>,
    /// or null for the usual order.
    /// </summary>
    public string? GameOrder { get; set; }

    /// <summary>
    /// Whether the user has the chat in game sessions (see <see cref="UserSettings"/>).
    /// </summary>
    public bool ChatOn { get; set; } = true;

    /// <summary>
    /// Whether the site plays sounds for the user (see <see cref="UserSettings"/>).
    /// </summary>
    public bool SoundOn { get; set; } = true;

    /// <summary>
    /// How playing cards are drawn for the user: <see cref="UserSettings.NewCards"/> or <see cref="UserSettings.ClassicCards"/>.
    /// </summary>
    public string CardStyle { get; set; } = UserSettings.NewCards;

    /// <summary>
    /// The external accounts (e.g. Google) the user signs in with.
    /// </summary>
    public List<ExternalLogin> ExternalLogins { get; set; } = [];
}

/// <summary>
/// A user's settings from their profile: <see cref="Chat"/> shows the chat in game sessions, <see cref="Sound"/> lets
/// the site play sounds (the chat's chime, game sounds and reading aloud), and <see cref="Cards"/> is how playing cards
/// are drawn (<see cref="NewCards"/> or <see cref="ClassicCards"/>). New users have the chat and sounds on, and the new
/// cards; settings saved without a card style get the new cards.
/// </summary>
public record UserSettings(bool Chat, bool Sound, string Cards = UserSettings.NewCards)
{
    /// <summary>The new cards: four suit colors, a large faint suit with the rank as a road in the middle.</summary>
    public const string NewCards = "New";

    /// <summary>The classic cards: red and black, with pips and court figures.</summary>
    public const string ClassicCards = "Classic";

    /// <summary>The card styles a user can choose.</summary>
    public static IReadOnlyList<string> CardStyles { get; } = [NewCards, ClassicCards];
}

/// <summary>
/// Links an external account (a provider such as Google, and the account's ID there) to a user.
/// </summary>
public class ExternalLogin
{
    /// <summary>
    /// The provider, e.g. <c>Google</c>.
    /// </summary>
    public required string Provider { get; set; }

    /// <summary>
    /// The account's ID at the provider.
    /// </summary>
    public required string ProviderKey { get; set; }

    /// <summary>
    /// The user the account belongs to.
    /// </summary>
    public Guid UserId { get; set; }
}

/// <summary>
/// A saved user's preferred options for one game, applied when they create a session of it.
/// </summary>
public class UserGameOptions
{
    /// <summary>
    /// The user the preferences belong to.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The game, e.g. <c>TheGang</c>.
    /// </summary>
    public required string GameType { get; set; }

    /// <summary>
    /// The chosen options as JSON, e.g. <c>{"mode":"Advanced"}</c>.
    /// </summary>
    public required string Options { get; set; }

    /// <summary>
    /// When the preferences were last saved.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// An icon uploaded by a user (or copied from their Google picture). A new upload replaces it with a new ID,
/// so its address never changes content and browsers can keep it.
/// </summary>
public class UserIcon
{
    /// <summary>
    /// The icon's ID, used in its address <c>/api/icons/{id}</c>.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The user the icon belongs to.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// <c>image/png</c>, <c>image/jpeg</c> or <c>image/webp</c>.
    /// </summary>
    public required string ContentType { get; set; }

    /// <summary>
    /// The image file.
    /// </summary>
    public required byte[] Data { get; set; }

    /// <summary>
    /// When the icon was uploaded.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
