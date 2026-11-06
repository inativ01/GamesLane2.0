using System.Text.RegularExpressions;
using GamesLane.Server.Games.RichManPoorMan;

namespace GamesLane.Server.Tests;

// Checks the translation files in wwwroot/lang against each other, the languages in i18n.js, the pages and the
// translated privacy policies.
public partial class TranslationsTests
{
    private static readonly string Wwwroot = FindWwwroot();

    // The languages in i18n.js other than English, whose texts are the keys.
    private static readonly string[] Translated = LanguageCodes().Where(code => code != "en").ToArray();

    public static TheoryData<string> TranslatedLanguages => new(Translated.Where(code => code != "he"));

    [GeneratedRegex(@"""((?:[^""\\]|\\.)*)""\s*:\s*""((?:[^""\\]|\\.)*)""")]
    private static partial Regex Entry();

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"const (SPOKEN_SINGLES|SPOKEN_GROUPS|SPOKEN_VALUES) = \{(.*?)\};", RegexOptions.Singleline)]
    private static partial Regex SpokenTable();

    [GeneratedRegex(@"""([^""]+)""")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"code: ""(\w+)""")]
    private static partial Regex LanguageCode();

    [GeneratedRegex(@"flag: ""([^""]+)""")]
    private static partial Regex LanguageFlag();

    [Theory]
    [MemberData(nameof(TranslatedLanguages))]
    public void Each_language_translates_the_same_texts_as_hebrew(string code)
    {
        var hebrew = Translations("he").Keys.ToHashSet();
        var other = Translations(code).Keys.ToHashSet();

        Assert.Empty(hebrew.Except(other));
        Assert.Empty(other.Except(hebrew));
    }

    [Fact]
    public void Translations_use_only_the_varying_parts_of_their_text()
    {
        foreach (var code in Translated)
        {
            var table = Translations(code);
            foreach (var (key, value) in table)
            {
                Assert.True(Names(value).IsSubsetOf(Names(key)), $"{code}: \"{value}\" has a varying part that \"{key}\" does not have.");
            }
        }
    }

    [Fact]
    public void Rich_man_poor_man_reads_every_throw_size_and_card_value_aloud_in_every_language()
    {
        var tables = SpokenTable().Matches(File.ReadAllText(Path.Combine(Wwwroot, "rich-man-poor-man.js")))
            .ToDictionary(match => match.Groups[1].Value, match => Quoted().Matches(match.Groups[2].Value).Select(m => m.Groups[1].Value).ToList());
        var singles = tables["SPOKEN_SINGLES"];
        var groups = tables["SPOKEN_GROUPS"];
        var values = tables["SPOKEN_VALUES"];

        Assert.Equal(RichManPoorManRules.CardsPerPlayer, singles.Count);
        Assert.All(singles, single => Assert.Empty(Names(single)));
        Assert.Equal(RichManPoorManRules.MaxPlayers - 1, groups.Count);
        Assert.All(groups, group => Assert.Equal(["card"], Names(group)));
        Assert.Equal(RichManPoorManRules.CardsPerPlayer, values.Count);
        foreach (var code in Translated)
        {
            var table = Translations(code);
            Assert.All(singles.Concat(groups).Concat(values), key => Assert.True(table.ContainsKey(key), $"{code}: \"{key}\" is not translated."));
            Assert.All(groups, group => Assert.Contains("{card}", table[group]));
        }
    }

    [Fact]
    public void Every_language_has_its_flag_and_every_translated_page_loads_all_languages()
    {
        var i18n = File.ReadAllText(Path.Combine(Wwwroot, "i18n.js"));
        Assert.All(LanguageFlag().Matches(i18n), match => Assert.True(File.Exists(Path.Combine(Wwwroot, match.Groups[1].Value))));

        var pages = Directory.GetFiles(Wwwroot, "*.html").Where(page => File.ReadAllText(page).Contains("lang/")).ToList();
        Assert.NotEmpty(pages);
        foreach (var page in pages)
        {
            var html = File.ReadAllText(page);
            Assert.All(Translated, code => Assert.Contains($"<script src=\"lang/{code}.js\"></script>", html));
        }
    }

    [Fact]
    public void Every_language_has_the_privacy_policy_with_the_same_sections_and_links()
    {
        var english = File.ReadAllText(Path.Combine(Wwwroot, "privacy.html"));
        foreach (var code in Translated)
        {
            var path = Path.Combine(Wwwroot, $"privacy.{code}.html");
            Assert.True(File.Exists(path), $"privacy.{code}.html is missing.");
            var policy = File.ReadAllText(path);
            Assert.Contains($"<html lang=\"{code}\"", policy);
            foreach (var tag in new[] { "<h3>", "<li>", "<strong>", "<a href=" })
            {
                Assert.True(Count(english, tag) == Count(policy, tag), $"privacy.{code}.html has a different number of {tag} than privacy.html.");
            }
        }
    }

    private static int Count(string text, string tag) => text.Split(tag).Length - 1;

    private static HashSet<string> Names(string text) => Placeholder().Matches(text).Select(match => match.Groups[1].Value).ToHashSet();

    private static Dictionary<string, string> Translations(string code)
    {
        var table = new Dictionary<string, string>();
        foreach (Match match in Entry().Matches(File.ReadAllText(Path.Combine(Wwwroot, "lang", $"{code}.js"))))
        {
            Assert.True(table.TryAdd(match.Groups[1].Value, match.Groups[2].Value), $"{code}: \"{match.Groups[1].Value}\" is translated twice.");
        }
        return table;
    }

    private static string[] LanguageCodes() =>
        LanguageCode().Matches(File.ReadAllText(Path.Combine(Wwwroot, "i18n.js"))).Select(match => match.Groups[1].Value).ToArray();

    private static string FindWwwroot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var wwwroot = Path.Combine(dir.FullName, "GamesLane.Server", "wwwroot");
            if (Directory.Exists(wwwroot))
            {
                return wwwroot;
            }
        }
        throw new DirectoryNotFoundException("GamesLane.Server/wwwroot was not found.");
    }
}
