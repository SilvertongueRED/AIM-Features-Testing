using System.Text.RegularExpressions;

namespace Garethp.ModsOfMistriaInstallerLib.Research;

/// <summary>
/// Reads a comment or a bug report for what it says about a crash - whether it is about one at
/// all, whether it is asking or telling, and what it says to do about it.
///
/// <see cref="CompatibilityLanguage"/> answers "are these two mods fine together". A crash asks
/// different questions of the same pages: has anybody else had this crash, did the author fix it,
/// and what did they say to do. The vocabulary barely overlaps - "breaks", "error", "won't load"
/// are crash words that say nothing about a pairing - so it is kept separate rather than bolted on
/// to the compatibility rules and allowed to change how a file conflict is judged.
/// </summary>
public static class CrashLanguage
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Compiled;

    /// <summary>
    /// Talk of the game failing: crashing, erroring, hanging, refusing to start.
    ///
    /// Deliberately the failure itself rather than any problem - "the sprite is the wrong colour"
    /// is a bug, but not one that tells the user anything about why their game stopped.
    /// </summary>
    private static readonly Regex Failure = new(
        @"\b(?:crash\w*|error(?:s|ed)?|exception|fatal|freez\w*|froze|hang(?:s|ing)?|black\s+screen" +
        @"|soft\s?-?lock\w*|stuck\s+(?:on|at)\s+(?:the\s+)?(?:load|loading)" +
        @"|(?:won'?t|doesn'?t|does\s+not|can'?t|cannot|will\s+not|fails?\s+to|refuses?\s+to)\s+" +
        @"(?:load|start|open|launch|boot|run))\b",
        Options);

    /// <summary>A sentence written as a question - which on Nexus is how compatibility is asked about.</summary>
    private static readonly Regex Question = new(
        @"\?|^\s*(?:does|do|is|are|will|would|can|could|has|have|did|anyone|any\s+chance|any\s+plans?)\b",
        Options);

    /// <summary>
    /// A report that something has been dealt with: "fixed", "resolved", "patched", "works now".
    /// The bare word only counts at the start of a line, where a changelog writes "Fix crash on
    /// startup" - anywhere else it is the noun, and "tried the fix you posted, still crashes"
    /// mentions a fix without saying anything was fixed.
    /// </summary>
    private static readonly Regex Fix = new(
        @"^\s*(?:[-•*]\s*)?fix\b|\b(?:fixed|fixes|resolv(?:ed|es)|patched|hotfix\w*|no\s+longer|should\s+(?:now\s+)?work\s+now|works?\s+now)\b",
        Options);

    /// <summary>True when the text talks about the game crashing, erroring or failing to start.</summary>
    public static bool IsAboutAFailure(string? text) =>
        !string.IsNullOrWhiteSpace(text) && Failure.IsMatch(text);

    /// <summary>True when the text asks rather than tells.</summary>
    public static bool IsAQuestion(string? text) =>
        !string.IsNullOrWhiteSpace(text) && Question.IsMatch(text);

    /// <summary>True when the text says something was fixed.</summary>
    public static bool SaysFixed(string? text) =>
        !string.IsNullOrWhiteSpace(text) && Fix.IsMatch(text);

    /// <summary>
    /// The first of <paramref name="terms"/> the text contains as a whole word, or null.
    ///
    /// These are the names out of the crash itself - the missing field, the script it died in -
    /// and a post that pastes the same error carries them verbatim. Whole words only, so a field
    /// called "item" is not found inside "items" in somebody's shopping list.
    /// </summary>
    public static string? FirstTermIn(string? text, IEnumerable<string> terms)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        foreach (var term in terms)
        {
            if (string.IsNullOrWhiteSpace(term)) continue;

            var pattern = $@"(?<![\p{{L}}\p{{Nd}}_]){Regex.Escape(term.Trim())}(?![\p{{L}}\p{{Nd}}_])";
            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase)) return term;
        }

        return null;
    }

    // ── What to do about it ──────────────────────────────────────────────────────

    private static readonly Regex VersionNumber = new(@"v?\d+(?:\.\d+)+[a-z]?", Options);

    /// <summary>
    /// The remedies authors and commenters actually write, each with the instruction to show for it.
    /// Tried in order; the first that matches decides. "{mod}" in a pattern or a template is the mod
    /// whose page the sentence came from; "{0}", "{1}" are the captured groups.
    /// </summary>
    private static readonly (string Pattern, string Template)[] Remedies =
    [
        // A version that fixes it - the most useful thing a page can say about a crash.
        (@"\b(?:fix(?:ed)?|resolved|patched)\s+(?:it\s+|this\s+|that\s+)?(?:in|with|by|as\s+of)\s+(?:version\s+|update\s+|the\s+)?(v?\d+(?:\.\d+)+[a-z]?)",
            "Update {mod} to {0} or later."),
        (@"\b(?:update|upgrade)\s+(?:the\s+mod\s+|it\s+|{mod}\s+)?to\s+(?:version\s+)?(v?\d+(?:\.\d+)+[a-z]?)",
            "Update {mod} to {0} or later."),
        (@"\b(?:re-?download|update\s+to|get|grab|use|install)\s+(?:the\s+)?(?:latest|newest|new|current|updated)\s+(?:version|update|file|release)",
            "Update {mod} to its latest version."),

        // "It's fixed now", "March is now updated to the current version" - but not "the game was
        // updated", which is the cause of the crash rather than the cure.
        (@"(?<!\b(?:game|steam|fom|mistria|game's)\s+)\b(?:is|was|has\s+been|should\s+be|it'?s)\s+(?:now\s+)?(?:fixed|updated|patched)\b",
            "Update {mod} to its latest version."),

        // Leftovers from an old install, which is how half of all "it crashed after updating"
        // reports end.
        (@"\b(?:delete|remove|clear\s+out)\s+(?:the\s+|your\s+|any\s+)?(?:old|previous|older)\s+(?:mod\s+)?(?:folder|files?|version|install)",
            "Delete the old {mod} folder, then install the current version fresh."),
        (@"\b(?:clean|fresh)\s+(?:re-?)?install|\bre-?install(?:ing)?\b",
            "Reinstall {mod} from a fresh download."),

        // Load order, kept with whatever the sentence says to load it relative to.
        (@"\bload\s+(?:it\s+|this\s+(?:mod\s+)?|{mod}\s+|mine\s+)?(after|before|below|above)\s+" +
         @"([^.,;!?\n]{3,60}?)(?=\s+(?:and|so|but|then|or|if|to|because)\b|[.,;!?\n]|$)",
            "Load {mod} {0} {1}."),

        // A compatibility patch named as the way to make it work. The words in front of "patch"
        // are taken as its name, minus the filler a sentence wraps it in - "download both the
        // regular one as well as the Portrait Compatibility Patch" names the Portrait one.
        (@"\b(?:download|install|use|get|grab|need|add)\b[^.!?\n]{0,80}?\b(?:the\s+|a\s+)?" +
         @"((?:(?!(?:as|the|a|an|and|or|well|both|to|of|with|for|your|this|that|regular|also)\b)[\w'-]+\s+){0,3}compat\w*\s+patch(?:es)?)\b",
            "Install the {0}."),
        (@"\b(?:download|install|use|get|grab)\s+(?:the\s+)?(?:version|file)\s+(?:in|from|under)\s+(?:the\s+)?[""']?optional\s+files",
            "Use the version of {mod} under Optional Files on its page."),

        // The installer or the framework, which is what an engine-level crash after a game patch
        // usually comes down to.
        (@"\b(?:update|updated|latest|newest|requires?|need(?:s)?)\s+(?:the\s+)?(?:latest\s+|newest\s+)?(MOMI|AIM|MMAPI|mod\s+installer|installer)\b",
            "Update {0} to its latest version."),

        // The game's own version.
        (@"\b(?:roll\s*back|downgrade|opt\s+out\s+of\s+the\s+beta|leave\s+the\s+beta|beta\s+branch)\b",
            "Check which game version {mod} supports - the author points at the game's version or beta branch."),

        // Turn the other one off. The name has to start with a capital, which is what keeps "disable
        // it" and "remove the old files" out of this.
        (@"\b(?i:disable|uninstall|remove|turn\s+off)\s+(?:the\s+)?(\p{Lu}[^.,;!?\n]{2,50}?)(?:\s+(?i:mod|first|before|too))?(?=[.,;!?\n]|$)",
            "Turn off {0}.")
    ];

    /// <summary>
    /// What a sentence tells the user to do, written as an instruction, or null when it does not
    /// say. Built from the sentence's own words - a version number, the thing to load after - so
    /// the instruction is the author's, not AIM's guess at one.
    /// </summary>
    /// <param name="modName">The mod whose page the sentence came from.</param>
    public static string? Remedy(string? text, string modName)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        foreach (var (source, template) in Remedies)
        {
            // Case-insensitive except where the rule itself says otherwise: the last one needs a
            // capital to recognise a mod's name. The mod's own name goes in as a literal.
            var pattern = source.Replace("{mod}", Regex.Escape(modName));
            var options = template.StartsWith("Turn off", StringComparison.Ordinal)
                ? RegexOptions.None
                : RegexOptions.IgnoreCase;

            var match = Regex.Match(text, pattern, options);
            if (!match.Success) continue;

            var instruction = template.Replace("{mod}", modName);
            for (var group = 1; group < match.Groups.Count; group++)
                instruction = instruction.Replace($"{{{group - 1}}}", Tidy(match.Groups[group].Value));

            return instruction;
        }

        return null;
    }

    /// <summary>The newest version number a sentence names, for "fixed in 2.0.11" style claims.</summary>
    public static string? VersionIn(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var match = VersionNumber.Match(text);
        return match.Success ? match.Value : null;
    }

    private static string Tidy(string value) =>
        Regex.Replace(value, @"\s+", " ").Trim().TrimEnd('.', ',', ';', ':');

    // ── Versions ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Compares two version strings the way authors write them: "2.0.10" after "2.0.9", a leading
    /// "v" ignored, and a missing part counted as zero. Null when either is not a version at all -
    /// "Beta", "final" - because guessing which is newer would be worse than not saying.
    /// </summary>
    public static int? CompareVersions(string? left, string? right)
    {
        var a = Parts(left);
        var b = Parts(right);
        if (a is null || b is null) return null;

        for (var index = 0; index < Math.Max(a.Count, b.Count); index++)
        {
            var x = index < a.Count ? a[index] : 0;
            var y = index < b.Count ? b[index] : 0;
            if (x != y) return x.CompareTo(y);
        }

        return 0;
    }

    private static List<int>? Parts(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;

        var match = Regex.Match(version.Trim(), @"^v?(\d+(?:\.\d+)*)", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        return match.Groups[1].Value.Split('.').Select(part => int.TryParse(part, out var n) ? n : 0).ToList();
    }
}
