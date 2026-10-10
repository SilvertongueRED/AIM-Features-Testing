using System.Text.RegularExpressions;

namespace Garethp.ModsOfMistriaInstallerLib.Research;

/// <summary>What a piece of evidence says about whether the mods can live together.</summary>
public enum Polarity
{
    /// <summary>The author or a user says there is no problem. Evidence *against* the conflict.</summary>
    Clearance,

    /// <summary>Something is stated to be broken or unusable together.</summary>
    Blocker,

    /// <summary>It works, but only under a condition - a patch, a load order, a requirement.</summary>
    Caution,

    /// <summary>Compatibility is discussed without a verdict either way.</summary>
    Context
}

/// <summary>One reason a sentence was kept, and what it means.</summary>
public sealed record CompatibilitySignal(Polarity Polarity, string Reason)
{
    /// <summary>
    /// True when the sentence only means anything if it is about *this* pairing.
    ///
    /// Half the vocabulary here is used by mod authors constantly for reasons that have nothing to
    /// do with the two mods in front of the user. "This mod replaces the bakery dessert case" is a
    /// mod describing itself; "you must reinstall them to play with them" is advice about Steam
    /// updates. Both used to be reported as evidence, and both are noise unless the sentence also
    /// names the other mod in the conflict - which is a judgement <see cref="ModNameMatcher"/>
    /// makes, not a regex, so the flag is carried out to the caller rather than settled here.
    /// </summary>
    public bool NeedsPairing { get; init; }
}

/// <summary>
/// Decides whether a sentence from a mod page bears on a conflict, and which way it points.
///
/// This exists because the old keyword list got the most common phrasing in the game's modding
/// scene wrong. It looked for the words "compatible", "compatibility" and "incompatible", and
/// authors overwhelmingly write the plural: "No known compatibilities / incompatibilities at this
/// time. It is a standalone mod." None of the three words is a substring of either plural, so the
/// one sentence on the page that answered the question was invisible. Matching on the stem
/// "compatib" instead catches every inflection there is.
///
/// The second thing it fixes is that the old list had no notion of direction. "Incompatible with
/// Foo" and "no known incompatibilities" both merely "mentioned compatibility", so a page that
/// explicitly cleared the mod read exactly like one that condemned it, and the user had to read
/// every quote to find out which. Here a sentence is classified, and a clearance is worth showing
/// precisely because it lets an issue be closed.
///
/// Order matters and is the whole design: negated phrasings are tested before the words they
/// negate, so "no known incompatibilities" is settled as a clearance before anything can see the
/// bare word "incompatibilities" inside it and call it a blocker.
/// </summary>
public static class CompatibilityLanguage
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Compiled;

    /// <summary>
    /// The rules, in the order they are tried. The first that matches decides the sentence.
    /// </summary>
    private static readonly (Regex Pattern, Polarity Polarity, string Reason, bool NeedsPairing)[] Rules =
    [
        // ── Negated claims, first, so the negation wins over the word it negates ──────────

        // "No known compatibilities / incompatibilities at this time", "no conflicts reported",
        // "not aware of any issues". The optional "known"/"reported" and the slash-separated pair
        // are both taken straight from how authors actually write this line.
        //
        // The trailing lookahead keeps "there is no compatibility patch" out of it: that sentence
        // negates the existence of a fix, not the existence of a problem, and reading it as an
        // all-clear would be exactly backwards.
        (new Regex(
            @"\b(?:no|none|not\s+aware\s+of\s+any|aren'?t\s+any|isn'?t\s+any)\b" +
            @"(?:\s+\w+){0,3}?\s*" +
            @"\b(?:in)?compatibilit(?:y|ies)\b(?!\s*(?:patch|fix|mod|version|update))|" +
            @"\bno\s+(?:known\s+|reported\s+|current\s+)?(?:conflicts?|issues?|problems?)\b",
            Options),
            Polarity.Clearance, "reports no known incompatibilities", false),

        // "This is not a standalone mod" is the opposite claim and has to be settled first, or the
        // rule below reads it as its own reverse. A lookbehind will not do it: the negation and the
        // word are separated by an article, and often by more.
        (new Regex(@"\b(?:not|isn'?t|aren'?t|never)\b(?:\s+\w+){0,2}\s+stand[\s-]?alone\b", Options),
            Polarity.Caution, "says the mod is not standalone", false),

        // "It is a standalone mod" - the author saying it touches nothing else.
        (new Regex(@"\bstand[\s-]?alone\b", Options),
            Polarity.Clearance, "describes the mod as standalone", false),

        // ── Problems that are already gone ───────────────────────────────────────────────

        // "Updates the mod so it no longer breaks with game patches", "fixed a conflict with Foo",
        // "doesn't clash with the bakery mod any more". Every one of these carries a blocker's own
        // verb, and the bare "breaks with" rule below used to file the first of them - a March
        // Expanded changelog line about surviving game updates - under "says this is a known
        // problem" in the crash window. They report the opposite: the problem has been dealt with.
        //
        // Needs pairing, because a fix only clears *this* pairing when it names the other mod. "No
        // longer breaks with game patches" says nothing at all about the mods in front of the user.
        //
        // The words allowed between the negation and the verb are kept few, and a conjunction ends
        // the run: "it doesn't work and crashes" and "fixed the icons but it still crashes" are both
        // complaints, and letting the gap run across "and" or "but" would read them as all-clears.
        //
        // A bare "fix" counts only where a changelog writes it, at the start of a line - "Fix crash
        // on startup". Anywhere else it is the noun: "tried the fix you posted, game crashes" is a
        // crash report.
        (new Regex(
            @"\b(?:(?:no\s+longer|not\s+any\s?more)(?:\s+(?!(?:but|and|yet|still|however|though)\b)\S+){0,2}?" +
            @"|(?:(?<=^\s*(?:[-•*]\s*)?)fix|fixed|fixes|resolv(?:ed|es)|solv(?:ed|es)|prevent(?:s|ed)?)" +
            @"(?:\s+(?!(?:but|and|yet|still|however|though)\b)\S+){0,4}?)" +
            @"\s+(?:break\w*|conflict\w*|clash\w*|crash\w*|incompatib\w*)", Options),
            Polarity.Clearance, "says a problem was fixed", true),

        // "It doesn't conflict with Foo", "they won't clash". The same verbs, denied rather than
        // fixed - and like a fix, an all-clear only for the pairing it names.
        (new Regex(
            @"\b(?:doesn'?t|does\s+not|don'?t|do\s+not|won'?t|will\s+not|shouldn'?t|should\s+not)" +
            @"(?:\s+(?:really|even|actually|ever))?" +
            @"\s+(?:break\w*|conflict\w*|clash\w*|crash\w*|interfere\w*)", Options),
            Polarity.Clearance, "says they do not conflict", true),

        // ── Blockers ─────────────────────────────────────────────────────────────────────

        // Broken by the game itself rather than by another mod. Worth knowing in its own right - it
        // is a real, current problem with the mod - but it is not "a conflict", and calling it one
        // sent the user looking for a second mod that was never involved. Tested before the conflict
        // rule so the reason says which of the two it is.
        //
        // It has to name an update, a patch or a version: "crashes on game start" and "no crashes
        // in the game since I installed it" are about the game, but not about the game changing.
        (new Regex(
            @"(?<!\bno\s+)\b(?:breaks?|broke|broken|crash(?:es|ed|ing)?)\s+(?:with|after|since)\s+" +
            @"(?:the\s+|this\s+)?(?:latest\s+|new(?:est)?\s+|recent\s+|last\s+|current\s+)?" +
            @"(?:(?:game|fom|fields\s+of\s+mistria)(?:'s)?\s+(?:update|patch|version)\w*" +
            @"|(?:update\w*|patch\w*)(?!\s+(?:of|to|for)\b)|v?\d+\.\d+(?:\.\d+)*)\b", Options),
            Polarity.Blocker, "says it breaks with a game update", false),

        (new Regex(
            @"\b(?:not|isn'?t|aren'?t|won'?t|will\s+not|never)\s+(?:be\s+)?" +
            @"(?:fully\s+|currently\s+|entirely\s+)?compatib\w*", Options),
            Polarity.Blocker, "says something is not compatible", false),

        (new Regex(@"\bincompatib\w*", Options),
            Polarity.Blocker, "says something is incompatible", false),

        (new Regex(
            @"\b(?:do\s+not|don'?t|do\s+n'?t|never)\s+(?:use|install|run|combine)\b" +
            @"(?:\s+\w+){0,3}?\s+\b(?:with|alongside|together)\b", Options),
            Polarity.Blocker, "warns against using them together", false),

        // "Breaks with game patches" is not a conflict with another mod, so the object of the verb is
        // checked: game, patch, update and version words are the game's, not a mod's.
        (new Regex(
            @"\b(?:conflicts?\s+with|clashes?\s+with|breaks?\s+(?:with|when\s+used\s+with))\b" +
            @"(?!\s+(?:the\s+|any\s+|some\s+)?(?:game|patch\w*|update\w*|version\w*|gml|steam)\b)", Options),
            Polarity.Blocker, "names a conflict", false),

        // ── Conditional - it works, but only if you do something ─────────────────────────

        // A bare "patch" is worthless and was actively misleading. Two ways it went wrong, both
        // seen on real pages:
        //
        //   • "my old patchless version" - patch\w* happily matched the middle of "patchless",
        //     which says the opposite of what was reported.
        //   • "Updated for the v0.11.7 patch" - a *game* patch. Every mod's version history says
        //     this, so the researcher reported a compatibility finding on essentially every mod
        //     that keeps a changelog in its description.
        //
        // So the word only counts when something in the sentence makes it a patch *between mods*:
        // it is called a compatibility patch, it is a patch for or to something, or it is being
        // referred to as a specific object ("the patch below", "a patch exists"). A version number
        // sitting in front of it does none of those.
        // Named as a compatibility patch outright. That phrase is only ever written about two mods
        // living together, so it stands on its own.
        (new Regex(@"\bcompatibilit\w*\s+patch(?:es)?\b", Options),
            Polarity.Caution, "mentions a compatibility patch", false),

        // The looser forms - "a patch", "the patch below", "patched version". Real often enough to
        // keep, but a mod author writes them about their own files at least as often, so they only
        // count when the sentence is about the pairing.
        (new Regex(
            @"\bpatch(?:es)?\b\s+(?:for|to|that|which)\b" +
            @"|\b(?:a|the|this|my|his|her|their|no)\s+patch(?:es)?\b" +
            @"|\bpatched\s+version\b", Options),
            Polarity.Caution, "mentions a patch", true),

        // "Load this after Foo" and "load it below the other mod" are the same instruction as
        // "load order", and are how people actually write it - so a couple of words are allowed
        // between the verb and the direction.
        (new Regex(@"\bload\s+(?:\w+\s+){0,2}(?:order|after|before|last|first|below|above)\b", Options),
            Polarity.Caution, "mentions load order", true),

        // What a mod replaces is the single most useful thing a description can say about a file
        // conflict - it is the author listing, by name, the things this mod will take over.
        //
        // But only when the list is about the other mod. Nearly every replacer's description opens
        // with "This Mod Replaces:" and then thirty furniture names, and reporting that back as a
        // finding tells the user something they knew before they opened the window: it is what the
        // conflict report already said, at ten times the length.
        (new Regex(@"\b(?:overwrit\w*|overrid\w*|replac(?:e|es|ed|ing|ement|ements)\b|replacer)\b", Options),
            Polarity.Caution, "mentions overwriting files", true),

        // "Requires" used to be kept, and it was pure noise: near enough every mod page says it
        // requires MOMI, which tells the user nothing about the two mods they are looking at. A
        // requirement only matters here when it names one of the other mods in the conflict, and
        // that is decided by ModNameMatcher rather than by a keyword, so nothing is lost by
        // dropping the word.

        // ── Positive statements that are not a blanket all-clear ─────────────────────────

        // "Fully compatible" is a blanket statement about the mod, so it needs no pairing.
        (new Regex(@"\b(?:fully|completely|totally|100%)\s+compatib\w*", Options),
            Polarity.Clearance, "states that it is fully compatible", false),

        // "Works with X" is a clearance only when there is an X.
        //
        // Two things went wrong with the old version of this rule, and one of them put a sentence
        // about *Steam* at the top of the evidence-against list: "you must reinstall them to play
        // with them" matched "play … with", and was reported as the author saying the two mods work
        // together. So "play" is gone - nobody says a mod "plays with" another one - and a pronoun
        // after the preposition is refused, because "works with them" identifies nothing. What is
        // left still has to name the other mod to count.
        (new Regex(
            @"\b(?:works?|working|runs?|installs?)\s+" +
            @"(?:just\s+|really\s+)?(?:fine|well|perfectly|nicely|great|happily)?\s*" +
            @"(?:with|alongside|together\s+with)\s+" +
            @"(?!\b(?:them|it|this|that|these|those|you|me|us|him|her|any|each|other)\b)", Options),
            Polarity.Clearance, "states that they work together", true),

        (new Regex(@"\bworks?\s+together\b", Options),
            Polarity.Clearance, "states that they work together", true),

        // "It should be compatible", "yes, they're compatible" - how an author answers "does this
        // work with X?" in the comments. The negated forms were settled as blockers further up, so
        // anything reaching here is the positive claim. Only about the pairing it answers.
        //
        // A question is not a claim, and neither is a doubt: "will this be compatible with X?" and
        // "I don't think it's compatible" read exactly like the answer without these two guards,
        // and a commenter's question would otherwise count as evidence the conflict is not real.
        // "If it only changes the portraits then it should be compatible" stays - a condition on
        // an answer is still an answer.
        (new Regex(
            @"^(?![^?]*\?)(?!\s*(?:is|are|will|would|does|do|can|could)\b)" +
            @"(?!.*\b(?:not\s+sure|unsure|don'?t\s+think|do\s+not\s+think|doubt|wonder(?:ing)?|no\s+idea)\b)" +
            @".*?\b(?:should\s+be|is|are|it'?s|they'?re)\s+(?:still\s+|totally\s+|perfectly\s+|fully\s+)?compatible\b",
            Options | RegexOptions.Singleline),
            Polarity.Clearance, "says they are compatible", true),

        // ── Anything else that discusses the subject at all ──────────────────────────────

        (new Regex(@"\bcompatib\w*", Options),
            Polarity.Context, "mentions compatibility", true),

        (new Regex(@"\bconflict\w*", Options),
            Polarity.Context, "mentions a conflict", true)
    ];

    /// <summary>
    /// Classifies one sentence, or null when it says nothing about compatibility.
    /// </summary>
    public static CompatibilitySignal? Classify(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence)) return null;

        foreach (var (pattern, polarity, reason, needsPairing) in Rules)
            if (pattern.IsMatch(sentence))
                return new CompatibilitySignal(polarity, reason) { NeedsPairing = needsPairing };

        return null;
    }

    /// <summary>
    /// The stricter bar used for comment threads and bug reports.
    ///
    /// A description is a few paragraphs written by the author; a comment thread is hundreds of
    /// posts by everyone, most of them "love this mod". A post that merely contains the word
    /// "replace" is noise, so only a stated verdict - a blocker, a clearance, or a patch - earns a
    /// place unless the post also names one of the other mods.
    /// </summary>
    public static CompatibilitySignal? ClassifyDiscussion(string post)
    {
        var signal = Classify(post);
        return signal?.Polarity is Polarity.Context ? null : signal;
    }

    /// <summary>
    /// Whether a classified sentence is worth putting in front of the user for *this* conflict.
    ///
    /// The last gate before a finding is shown, and the one that turned the window from a list of
    /// everything the pages say into a list of things that bear on the question. A sentence with no
    /// verdict is not evidence; a sentence whose verdict only means something in context is not
    /// evidence unless the context is there.
    /// </summary>
    public static bool BearsOnThePairing(CompatibilitySignal? signal, bool namesTheOtherMod) =>
        signal is not null && (namesTheOtherMod || !signal.NeedsPairing);
}
