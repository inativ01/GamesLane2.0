using System.Text.RegularExpressions;

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

    [GeneratedRegex(@"^\d+ \((masculine|feminine)\)$")]
    private static partial Regex NumberWord();

    [GeneratedRegex(@"code: ""(\w+)""")]
    private static partial Regex LanguageCode();

    [GeneratedRegex(@"flag: ""([^""]+)""")]
    private static partial Regex LanguageFlag();

    [Theory]
    [MemberData(nameof(TranslatedLanguages))]
    public void Each_language_translates_the_same_texts_as_hebrew(string code)
    {
        var hebrew = Translations("he").Keys.Where(key => !NumberWord().IsMatch(key)).ToHashSet();
        var other = Translations(code).Keys.Where(key => !NumberWord().IsMatch(key)).ToHashSet();

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
                var allowed = Names(key);
                if (allowed.Contains("n"))
                {
                    allowed.UnionWith(["m", "f"]);
                }
                Assert.True(Names(value).IsSubsetOf(allowed), $"{code}: \"{value}\" has a varying part that \"{key}\" does not have.");
                foreach (var gender in Names(value).Intersect(["m", "f"]))
                {
                    var suffix = gender == "m" ? "masculine" : "feminine";
                    Assert.All(Enumerable.Range(2, 11), n => Assert.Contains($"{n} ({suffix})", table.Keys));
                }
            }
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
