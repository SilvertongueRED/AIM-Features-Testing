using System.Text.RegularExpressions;
using Garethp.ModsOfMistriaInstallerLib.Crash;
using Garethp.ModsOfMistriaInstallerLib.Nexus;

namespace Garethp.ModsOfMistriaInstallerLib.Research;

/// <summary>What the crash window asks of the suspects' Nexus pages.</summary>
/// <param name="Suspects">The mods worth reading about, most likely first.</param>
/// <param name="OtherMods">
/// Every mod switched on in the game, by name. A comment on a suspect's page that names one of
/// these - "does this work with March Expanded?" - is about the user's own setup, whichever mod
/// AIM happens to suspect.
/// </param>
/// <param name="CrashTerms">
/// Identifiers out of the crash itself: the missing field, the script it died in. A post quoting
/// the same error carries them verbatim. See <see cref="CrashResearch.TermsFor"/>.
/// </param>
public sealed record CrashQuestion(
    IReadOnlyList<ResearchSubject> Suspects,
    IReadOnlyList<string> OtherMods,
    IReadOnlyList<string> CrashTerms);

/// <summary>
/// Reads the suspects' Nexus pages for one crash: has anybody else had it, did the author fix it,
/// what did they say to do - and what do the comments say about the other mods the user runs.
///
/// The crash window used to borrow <see cref="ConflictResearch"/>, which answers a different
/// question - "are these two mods fine together" - and so searched each suspect's comments for the
/// *other suspects'* names and kept only sentences about compatibility. Nothing about the crash
/// itself was ever looked for: not the error, not the word "crash", not the bug tracker's crash
/// reports. A page full of "fixed the crash on entering the inn in 2.0.11" came back as "nothing
/// found", and a failed or blocked request did too, so the user could not tell AIM had looked at
/// all. This asks the crash's own questions and says, mod by mod, what it managed to read.
///
/// Three things set it apart from the conflict researcher:
///
///   • It searches for the crash. The thread is searched for the crash's own identifiers, for
///     "crash", and for compatibility talk; the bug tracker's crash reports are opened and read
///     with the author's replies.
///   • It pairs questions with answers. A search returns matching comments flat, so the author's
///     "yes, just grab the portrait patch" arrives without the question it answers. Asking the
///     thread for that comment by id returns it nested under its question, and the pair is the
///     finding - see <see cref="NexusPageReader.ReadCommentContextAsync"/>.
///   • It looks for the cure. A version that fixed it, a folder to delete, a patch to install -
///     <see cref="CrashLanguage.Remedy"/> turns those into an instruction, and a fix in a version
///     newer than the user's is an instruction to update.
///
/// Comments and bug reports have no API - Nexus's own API, and the sign-in AIM holds for it, cover
/// the description, the release notes and the files only - so they are read from the public pages
/// as a guest. Nexus sometimes answers a non-browser with its bot check instead of the page; when
/// it does, that is recorded and said, rather than reported as an empty page.
/// </summary>
public static class CrashResearch
{
    private const string Game = "fieldsofmistria";

    /// <summary>Nexus's numeric id for Fields of Mistria, which the page widgets are keyed by.</summary>
    private const int GameId = 6685;

    /// <summary>
    /// What to search every suspect's thread for besides the crash's own words. The thread search
    /// is stemmed, so "crash" finds "crashes" and "compatible" finds "compatibility" - but it does
    /// not match word fragments, so "compatib" finds nothing.
    /// </summary>
    private static readonly string[] GeneralSearches = ["crash", "compatible", "work with"];

    /// <summary>Pages of results per search. Twenty comments a page.</summary>
    private const int SearchPages = 1;

    /// <summary>Pages of results for a search on the crash's own identifiers, which matter most.</summary>
    private const int CrashTermPages = 2;

    /// <summary>
    /// Questions or answers whose other half is fetched, per mod. Each is a request, and the pairs
    /// that matter are almost always in the first few.
    /// </summary>
    private const int MaxContextFetches = 4;

    private const int BugListPages = 2;
    private const int MaxBugsOpened = 5;

    /// <summary>Findings kept from one mod's comments, and from its bug tracker.</summary>
    private const int MaxCommentFindings = 8;

    private const int MaxBugFindings = 6;

    /// <summary>Changelog versions read, newest first.</summary>
    private const int ChangelogVersions = 10;

    /// <summary>A pause between page requests. Nothing here is urgent.</summary>
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(200);

    // ── What to look for ──────────────────────────────────────────────────────────

    private static readonly Regex GmlName = new(
        @"\bgml_(?:Script|Object|GlobalScript|RoomCC)_([A-Za-z0-9_]+)", RegexOptions.Compiled);

    /// <summary>
    /// The identifiers in a crash worth searching a comment thread for.
    ///
    /// Only names that look like code - an underscore, a digit, an inner capital - or are long
    /// enough to be unusual. "Stores" is the script a store crash dies in, and also a word in a
    /// hundred comments about shops; "spr_decor_cottage_garden_arch" is in a comment only when
    /// somebody pasted that error.
    /// </summary>
    public static IReadOnlyList<string> TermsFor(GameCrashLog crash)
    {
        var terms = new List<string>();

        void Add(string? term)
        {
            if (string.IsNullOrWhiteSpace(term)) return;

            term = term.Trim();
            if (term.Length < 4 || !LooksLikeCode(term)) return;
            if (!terms.Contains(term, StringComparer.OrdinalIgnoreCase)) terms.Add(term);
        }

        Add(crash.Subject);

        foreach (Match match in GmlName.Matches(crash.Message)) Add(match.Groups[1].Value);

        if (crash.Innermost is { } frame)
        {
            Add(Path.GetFileNameWithoutExtension(frame.Path));
            Add(frame.Symbol);
        }

        return terms.Take(3).ToList();
    }

    private static bool LooksLikeCode(string term) =>
        term.Contains('_') ||
        term.Any(char.IsDigit) ||
        term.Skip(1).Any(char.IsUpper) ||
        term.Length >= 12;

    // ── The run ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads every suspect's pages, most likely first.
    /// </summary>
    /// <param name="client">
    /// AIM's Nexus API client, signed in through the user's own Nexus account. Null when they are
    /// not signed in, in which case the description and release notes are skipped and said to be.
    /// </param>
    /// <param name="pageReader">Reads the public pages: comments, bug reports, the readme.</param>
    /// <param name="partial">
    /// Called with everything found so far after each mod, so a window can show the first suspect's
    /// answer while the fourth is still being read.
    /// </param>
    public static async Task<ResearchResult> InvestigateAsync(
        CrashQuestion question,
        NexusApiClient? client,
        HttpClient? pageReader,
        NexusSession? session = null,
        Action<ResearchResult>? partial = null,
        CancellationToken ct = default)
    {
        session ??= NexusSession.Anonymous;

        var run = new Run(question, client, pageReader, session, ct);

        foreach (var mod in question.Suspects)
        {
            ct.ThrowIfCancellationRequested();

            if (mod.ModId is null)
            {
                foreach (var source in Enum.GetValues<ResearchSource>())
                    run.Coverage.Add(new ResearchCoverage(mod.Name, source, CoverageStatus.Skipped)
                    {
                        Skipped = SkipReason.NoNexusPage
                    });

                partial?.Invoke(run.Result());
                continue;
            }

            await run.ReadAsync(mod);
            partial?.Invoke(run.Result());
        }

        return run.Result();
    }

    /// <summary>
    /// One research run's state: what has been found, what was read, and whether Nexus has started
    /// refusing pages.
    /// </summary>
    private sealed class Run(
        CrashQuestion question,
        NexusApiClient? client,
        HttpClient? http,
        NexusSession session,
        CancellationToken ct)
    {
        public readonly List<ResearchFinding> Findings = [];
        public readonly List<ResearchCoverage> Coverage = [];

        /// <summary>
        /// Set when Nexus's bot check answers instead of a page. Every page comes from the same
        /// site, so once one is refused the rest will be; asking again only adds a wait.
        /// </summary>
        private bool _pagesBlocked;

        public ResearchResult Result() =>
            new(Rank(Findings), ConflictResearch.BuildLinks(question.Suspects))
            {
                Coverage = Coverage.ToList()
            };

        public async Task ReadAsync(ResearchSubject mod)
        {
            var page = ConflictResearch.PageOf(mod);
            var others = OthersThan(mod);

            var overview = await ReadOverviewAsync(mod, page, others);
            await ReadChangelogAsync(mod, page, others);
            await ReadReadmeAsync(mod, page, others);

            var authors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(overview?.Uploader)) authors.Add(overview.Uploader);

            await ReadCommentsAsync(mod, page, others, authors);
            await ReadBugsAsync(mod, page, others, authors, overview?.Version);
        }

        /// <summary>
        /// Every other mod the user runs. The mod itself is left out - its own page mentions it
        /// constantly - along with any mod whose name it shares, so "March Expanded" is not found
        /// to be talking about "March Expanded - Portrait Patch".
        /// </summary>
        private List<string> OthersThan(ResearchSubject mod) =>
            question.OtherMods
                .Concat(question.Suspects.Select(suspect => suspect.Name))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Where(name => !ModNameMatcher.Mentions(name, mod.Name) && !ModNameMatcher.Mentions(mod.Name, name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        // ── Through the API ─────────────────────────────────────────────────────

        private async Task<NexusModOverview?> ReadOverviewAsync(
            ResearchSubject mod, string page, List<string> others)
        {
            if (client is null)
            {
                Coverage.Add(new ResearchCoverage(mod.Name, ResearchSource.Description, CoverageStatus.Skipped)
                {
                    Skipped = SkipReason.NotSignedIn
                });
                return null;
            }

            NexusModOverview? overview;
            try
            {
                overview = await client.GetModOverviewAsync(Game, mod.ModId!.Value, ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Logger.Log($"Crash research could not read the description of {mod.Name}: {exception.Message}");
                overview = null;
            }

            if (overview is null)
            {
                Coverage.Add(new ResearchCoverage(mod.Name, ResearchSource.Description, CoverageStatus.Failed));
                return null;
            }

            var kept = ConflictResearch
                .Scan(mod, $"{overview.Summary}\n{overview.Description}", others, page, "description")
                .Select(finding => Tag(finding, others))
                .Where(KeepFromDescription)
                .ToList();

            Findings.AddRange(kept);
            Coverage.Add(new ResearchCoverage(mod.Name, ResearchSource.Description,
                kept.Count > 0 ? CoverageStatus.Read : CoverageStatus.Empty, 1) { Kept = kept.Count });

            return overview;
        }

        /// <summary>
        /// What a description says that is worth putting beside a crash.
        ///
        /// A blocker earns its place only when it names one of the user's own mods. "Not
        /// compatible with Better Dialogue" on a page is a known problem for somebody who runs
        /// Better Dialogue; reported to a user who does not, under "says this is a known problem",
        /// it reads as an explanation of a crash it has nothing to do with. A condition or an
        /// all-clear stays either way - "only compatible with dialogue mods that have a patch" is
        /// exactly the sort of rule a crash breaks.
        /// </summary>
        private static bool KeepFromDescription(ResearchFinding finding) =>
            finding.Polarity is not Polarity.Blocker || finding.MentionedMod is not null;

        private async Task ReadChangelogAsync(ResearchSubject mod, string page, List<string> others)
        {
            if (client is null)
            {
                Coverage.Add(new ResearchCoverage(mod.Name, ResearchSource.Changelog, CoverageStatus.Skipped)
                {
                    Skipped = SkipReason.NotSignedIn
                });
                return;
            }

            List<ModChangelogEntry> entries;
            try
            {
                entries = await client.GetChangelogsAsync(Game, mod.ModId!.Value, ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Logger.Log($"Crash research could not read the changelog of {mod.Name}: {exception.Message}");
                Coverage.Add(new ResearchCoverage(mod.Name, ResearchSource.Changelog, CoverageStatus.Failed));
                return;
            }

            var source = $"{page}?tab=logs";
            var kept = 0;

            foreach (var entry in entries.Take(ChangelogVersions))
            {
                // Newer than what the user has, the same, or unknown. Only a newer one can be the
                // cure; one the user already has describes a crash they are not having, and saying
                // otherwise is how a changelog line about surviving game patches ended up under
                // "says this is a known problem".
                var newer = CrashLanguage.CompareVersions(entry.Version, mod.Version) is { } order ? order > 0 : (bool?)null;

                foreach (var line in entry.Lines)
                {
                    var finding = JudgeChangelogLine(mod, line, entry.Version, newer, others, source);
                    if (finding is null) continue;

                    Findings.Add(finding);
                    kept++;
                }
            }

            Coverage.Add(new ResearchCoverage(mod.Name, ResearchSource.Changelog,
                kept > 0 ? CoverageStatus.Read : CoverageStatus.Empty, Math.Min(entries.Count, ChangelogVersions))
            {
                Kept = kept
            });
        }

        /// <summary>
        /// One line of release notes, judged as a possible cure.
        ///
        /// A fix only counts when it is a fix for a crash - "fixed a crash when entering the inn",
        /// a line naming this crash's own identifiers, or one naming another of the user's mods.
        /// "Updates the mod so it no longer breaks with game patches" is a fix for being out of date
        /// with the game, true of every maintained mod, and says nothing about this crash.
        /// </summary>
        private ResearchFinding? JudgeChangelogLine(
            ResearchSubject mod, string line, string version, bool? newer, List<string> others, string source)
        {
            var plain = ConflictResearch.PlainText(line);
            if (plain.Length < 8) return null;

            var term = CrashLanguage.FirstTermIn(plain, question.CrashTerms);
            var mentioned = ModNameMatcher.FirstMentioned(plain, others);
            var fixedSomething = CrashLanguage.SaysFixed(plain) ||
                                 CompatibilityLanguage.Classify(plain)?.Reason == "says a problem was fixed";
            var aboutAFailure = CrashLanguage.IsAboutAFailure(plain);

            var where = $"changelog for {version}";

            if (fixedSomething && (aboutAFailure || term is not null || mentioned is not null))
            {
                // A fix the user already has is not the cure for the crash they are having - unless
                // it names this very crash, in which case it is worth knowing it came back.
                if (newer == false && term is null) return null;

                // With the installed version unknown, every crash fix in the history would read as
                // advice; only one that names this crash or one of the user's mods is worth it.
                if (newer is null && term is null && mentioned is null) return null;

                return new ResearchFinding(mod.Name, ConflictResearch.Trim(plain), newer == true
                    ? $"fixed in {version}, newer than the {mod.Version} you have"
                    : newer == false
                        ? $"fixed in {version}, which you already have - this may be a different crash"
                        : $"fixed in {version}", source)
                {
                    Polarity = newer == false ? Polarity.Context : Polarity.Caution,
                    Where = where,
                    FullQuote = plain,
                    PageMod = mod.Name,
                    MentionedMod = mentioned,
                    NamesTheOtherMod = mentioned is not null,
                    MatchesThisCrash = term is not null,
                    ByModAuthor = true,
                    Treatment = newer == false
                        ? null
                        : newer == true
                            ? $"Update {mod.Name} to {version} or later."
                            : $"Make sure {mod.Name} is at version {version} or later."
                };
            }

            // Otherwise only a line about one of the user's other mods - "added compatibility with
            // Crys's tiles" - and only when it says something about the two of them.
            if (mentioned is null && term is null) return null;

            var signal = CompatibilityLanguage.Classify(plain);
            if (signal is null && term is null) return null;

            return new ResearchFinding(mod.Name, ConflictResearch.Trim(plain),
                signal?.Reason ?? "names something this crash names", source)
            {
                Polarity = signal?.Polarity ?? Polarity.Context,
                Where = where,
                FullQuote = plain,
                PageMod = mod.Name,
                MentionedMod = mentioned,
                NamesTheOtherMod = mentioned is not null,
                MatchesThisCrash = term is not null,
                ByModAuthor = true
            };
        }

        private async Task ReadReadmeAsync(ResearchSubject mod, string page, List<string> others)
        {
            if (http is null) return;

            // Served from Nexus's file host rather than the site, so the site's bot check does not
            // stand in front of it - and a mod without a readme answers "not found", which is the
            // common case rather than a failure. Neither is worth a line in the coverage list.
            var readme = await NexusPageReader.ReadReadmeAsync(http, GameId, mod.ModId!.Value, session, ct);
            if (readme is null) return;

            var kept = ConflictResearch.Scan(mod, readme, others, $"{page}?tab=docs", "readme", strict: true)
                .Select(finding => Tag(finding, others))
                .Where(KeepFromDescription)
                .ToList();

            Findings.AddRange(kept);
            Coverage.Add(new ResearchCoverage(mod.Name, ResearchSource.Readme,
                kept.Count > 0 ? CoverageStatus.Read : CoverageStatus.Empty, 1) { Kept = kept.Count });
        }

        /// <summary>
        /// Fills in which of the user's mods a description sentence names, and whether it names this
        /// crash - and, when it does either, what it says to do. A general instruction ("portrait
        /// mods need the portrait patch") stays a condition rather than a thing to try, because it
        /// is about everybody's setup, not this one.
        /// </summary>
        private ResearchFinding Tag(ResearchFinding finding, List<string> others)
        {
            var mentioned = ModNameMatcher.FirstMentioned(finding.FullQuote, others);
            var matches = CrashLanguage.FirstTermIn(finding.FullQuote, question.CrashTerms) is not null;

            return finding with
            {
                MentionedMod = mentioned,
                MatchesThisCrash = matches,
                ByModAuthor = true,
                Treatment = mentioned is not null || matches
                    ? CrashLanguage.Remedy(finding.FullQuote, finding.PageMod.Length > 0 ? finding.PageMod : finding.ModName)
                    : null
            };
        }

        // ── The comment thread ──────────────────────────────────────────────────

        private async Task ReadCommentsAsync(
            ResearchSubject mod, string page, List<string> others, HashSet<string> authors)
        {
            if (http is null) return;

            if (_pagesBlocked)
            {
                Coverage.Add(Skipped(mod, ResearchSource.Comments, SkipReason.BlockedEarlier));
                return;
            }

            var source = $"{page}?tab=posts";

            // The tab itself: page one of the thread, pinned posts included - which is where an
            // author puts "known issues" - and the thread's id, without which nothing else can be
            // asked for.
            var tab = await NexusPageReader.FetchWithStatusAsync(http, source, session, ct);
            if (!tab.Succeeded)
            {
                Coverage.Add(Refused(mod, ResearchSource.Comments, tab));
                return;
            }

            var comments = new Dictionary<string, NexusComment>(StringComparer.Ordinal);
            void Collect(IEnumerable<NexusComment> batch)
            {
                foreach (var comment in batch)
                {
                    if (comments.Values.Any(seen => seen.Text == comment.Text)) continue;
                    comments.TryAdd(comment.Id, comment);
                    if (comment.ByModAuthor && comment.Author.Length > 0) authors.Add(comment.Author);
                }
            }

            Collect(NexusPageReader.ExtractComments(tab.Html!, "posts"));

            var threadId = NexusPageReader.ExtractCommentThreadId(tab.Html!);
            var searches = 0;

            if (threadId is not null)
            {
                var plan = question.CrashTerms.Select(term => (term, CrashTermPages))
                    .Concat(GeneralSearches.Select(term => (term, SearchPages)))
                    .Concat(ConflictResearch.SearchTermsFor(
                            question.Suspects.Where(other => !ReferenceEquals(other, mod)).Select(other => other.Name).ToList())
                        .Select(term => (term, SearchPages)));

                foreach (var (term, pages) in plan)
                {
                    for (var number = 1; number <= pages; number++)
                    {
                        await Task.Delay(Pause, ct);

                        var (found, fetch) = await NexusPageReader.ReadCommentThreadPageAsync(
                            http, GameId, mod.ModId!.Value, threadId.Value, number, session, ct, term);

                        if (fetch.Blocked) _pagesBlocked = true;
                        if (!fetch.Succeeded) break;

                        searches++;
                        Collect(found);

                        // A short page is the last one.
                        if (found.Count < 20) break;
                    }

                    if (_pagesBlocked) break;
                }
            }

            var findings = await JudgeCommentsAsync(mod, comments.Values.ToList(), others, threadId, source);

            Findings.AddRange(findings);

            // Blocked partway still says blocked: the searches it did not finish are the ones that
            // would have found the old conversations, and "read, nothing found" would claim more
            // than AIM managed.
            Coverage.Add(new ResearchCoverage(mod.Name, ResearchSource.Comments,
                _pagesBlocked ? CoverageStatus.Blocked
                : findings.Count > 0 ? CoverageStatus.Read : CoverageStatus.Empty, comments.Count)
            {
                Searched = searches,
                Kept = findings.Count
            });
        }

        /// <summary>
        /// Works through the comments read, keeps the ones that bear on this crash or on the user's
        /// other mods, and fetches the other half of the most useful conversations.
        /// </summary>
        private async Task<List<ResearchFinding>> JudgeCommentsAsync(
            ResearchSubject mod, List<NexusComment> comments, List<string> others, int? threadId, string source)
        {
            var candidates = comments
                .Select(comment => (Comment: comment, Read: Read(comment.Text, others)))
                .Where(entry => Relevant(entry.Comment, entry.Read))
                // The crash's own words first, then the author, then anything about another mod.
                .OrderByDescending(entry => entry.Read.Term is not null)
                .ThenByDescending(entry => entry.Comment.ByModAuthor)
                .ThenByDescending(entry => entry.Read.Mentioned is not null)
                .ThenByDescending(entry => entry.Read.Failure)
                .Take(MaxCommentFindings)
                .ToList();

            var findings = new List<ResearchFinding>();
            var fetches = 0;

            foreach (var (comment, read) in candidates)
            {
                string? asked = null;
                NexusComment? answer = null;

                // Recover the other half of the conversation: the question an author's reply was
                // answering, or the author's answer to a question.
                var wantsQuestion = comment.ByModAuthor && !read.Question;
                var wantsAnswer = !comment.ByModAuthor && read.Question;

                if ((wantsQuestion || wantsAnswer) && threadId is not null && fetches < MaxContextFetches &&
                    !_pagesBlocked && http is not null && long.TryParse(comment.Id, out _))
                {
                    fetches++;
                    await Task.Delay(Pause, ct);

                    var (context, fetch) = await NexusPageReader.ReadCommentContextAsync(
                        http, GameId, mod.ModId!.Value, threadId.Value, comment.Id, session, ct);
                    if (fetch.Blocked) _pagesBlocked = true;

                    var self = context.FirstOrDefault(entry => entry.Id == comment.Id);
                    if (self is not null)
                    {
                        if (wantsQuestion && NexusPageReader.ParentOf(context, self) is { } parent)
                            asked = parent.Text;

                        if (wantsAnswer)
                            answer = NexusPageReader.RepliesTo(context, self).FirstOrDefault(reply => reply.ByModAuthor);
                    }
                }

                findings.Add(asked is not null
                    ? CommentFinding(mod, source, asked, comment, others, read)
                    : CommentFinding(mod, source, comment.Text, answer, others, read, comment));
            }

            return findings;
        }

        /// <summary>A question and the author's answer to it.</summary>
        private ResearchFinding CommentFinding(
            ResearchSubject mod, string source, string questionText, NexusComment authorAnswer,
            List<string> others, CommentReading answerReading)
        {
            var questionReading = Read(questionText, others);
            return Build(mod, source, questionText, authorAnswer, others,
                Merge(questionReading, answerReading), asker: "");
        }

        /// <summary>A comment on its own, with the author's answer when one was found.</summary>
        private ResearchFinding CommentFinding(
            ResearchSubject mod, string source, string text, NexusComment? answer,
            List<string> others, CommentReading read, NexusComment comment)
        {
            if (answer is not null) read = Merge(read, Read(answer.Text, others));

            return Build(mod, source, text, answer, others, read,
                asker: comment.ByModAuthor ? "" : comment.Author, byAuthor: comment.ByModAuthor, authorName: comment.Author);
        }

        private ResearchFinding Build(
            ResearchSubject mod,
            string source,
            string text,
            NexusComment? answer,
            List<string> others,
            CommentReading read,
            string asker,
            bool byAuthor = false,
            string authorName = "")
        {
            // The answer decides what the conversation means, when there is one: "does this work
            // with X?" is a question, and "yes, with the portrait patch" is the verdict.
            var verdictText = answer?.Text ?? text;
            var (signal, remedy) = ReadStatements(verdictText, mod.Name);

            // A verdict that only means something about a named pairing - "fixed", "works with" -
            // means nothing here when the conversation names none of the user's mods. "Tried the
            // fix you posted, game crashes anyway" is a crash report, not an all-clear.
            if (signal is { NeedsPairing: true } && read.Mentioned is null && read.Term is null) signal = null;

            // A commenter's own fix only counts when they say it worked: "deleting the old folder
            // fixed it for me" is a remedy, "should I delete the old folder" is not.
            if (answer is null && !byAuthor && !CrashLanguage.SaysFixed(text)) remedy = null;

            var polarity = Judge(signal, read, remedy, answered: answer is not null || byAuthor);

            var reasons = new List<string>();
            if (read.Term is not null) reasons.Add($"names \"{read.Term}\" from this crash");
            if (read.Mentioned is not null) reasons.Add($"about {read.Mentioned}");
            if (signal is not null && signal.Polarity != Polarity.Context) reasons.Add(signal.Reason);
            else if (read.Failure) reasons.Add("reports a crash");
            if (answer is not null) reasons.Add("the author answered");
            if (reasons.Count == 0) reasons.Add("discusses it");

            var who = byAuthor
                ? $"{mod.Name} - {(authorName.Length > 0 ? authorName : "author")} (author)"
                : asker.Length > 0 ? $"{mod.Name} - {asker}" : mod.Name;

            return new ResearchFinding(who, ConflictResearch.Trim(text), string.Join(", ", reasons), source)
            {
                Polarity = polarity,
                Where = "comments",
                FullQuote = text,
                PageMod = mod.Name,
                MentionedMod = read.Mentioned,
                NamesTheOtherMod = read.Mentioned is not null,
                MatchesThisCrash = read.Term is not null,
                ByModAuthor = byAuthor,
                Answer = answer?.Text,
                AnsweredBy = answer?.Author ?? "",
                Treatment = remedy
            };
        }

        /// <summary>
        /// Which section a conversation belongs in.
        ///
        /// A stated verdict wins. Without one, a post quoting this crash's own error is the same
        /// problem reported - a known problem - and a crash report naming another of the user's
        /// mods is too. An answer that only says what to do is a condition: it works, once you have
        /// done that.
        /// </summary>
        private static Polarity Judge(CompatibilitySignal? signal, CommentReading read, string? remedy, bool answered)
        {
            if (signal is { Polarity: not Polarity.Context } stated) return stated.Polarity;
            if (remedy is not null) return Polarity.Caution;
            if (read.Failure && (read.Term is not null || read.Mentioned is not null)) return Polarity.Blocker;
            if (read.Term is not null) return Polarity.Blocker;
            return answered && read.Failure ? Polarity.Caution : Polarity.Context;
        }

        private static bool Relevant(NexusComment comment, CommentReading read)
        {
            if (read.Term is not null) return true;

            if (read.Mentioned is not null)
                return read.Compatibility is not null || read.Failure || read.Question;

            // The author's own word on a crash or on compatibility is worth having even when it
            // names nothing - "a fix for the inn crash is coming", "standalone, no known conflicts".
            return comment.ByModAuthor &&
                   (read.Failure || read.Compatibility is { Polarity: not Polarity.Context });
        }

        private CommentReading Read(string text, List<string> others) => new(
            CrashLanguage.FirstTermIn(text, question.CrashTerms),
            ModNameMatcher.FirstMentioned(text, others),
            CrashLanguage.IsAboutAFailure(text),
            CrashLanguage.IsAQuestion(text),
            CompatibilityLanguage.Classify(text));

        private static CommentReading Merge(CommentReading first, CommentReading second) => new(
            first.Term ?? second.Term,
            first.Mentioned ?? second.Mentioned,
            first.Failure || second.Failure,
            first.Question,
            second.Compatibility ?? first.Compatibility);

        // ── The bug tracker ─────────────────────────────────────────────────────

        private async Task ReadBugsAsync(
            ResearchSubject mod, string page, List<string> others, HashSet<string> authors, string? latest)
        {
            if (http is null) return;

            if (_pagesBlocked)
            {
                Coverage.Add(Skipped(mod, ResearchSource.BugReports, SkipReason.BlockedEarlier));
                return;
            }

            var source = $"{page}?tab=bugs";
            var bugs = new List<NexusBugSummary>();

            for (var number = 1; number <= BugListPages; number++)
            {
                await Task.Delay(Pause, ct);

                var (batch, fetch) = await NexusBugReader.ReadBugListWithStatusAsync(
                    http, GameId, mod.ModId!.Value, number, session, ct);

                if (!fetch.Succeeded)
                {
                    if (fetch.Blocked) _pagesBlocked = true;

                    // A later page failing still leaves what the first ones said.
                    if (number == 1)
                    {
                        Coverage.Add(Refused(mod, ResearchSource.BugReports, fetch));
                        return;
                    }

                    break;
                }

                bugs.AddRange(batch);
                if (batch.Count < 10) break;
            }

            var findings = new List<ResearchFinding>();
            var opened = 0;

            foreach (var bug in bugs
                         .Select(bug => (Bug: bug, Title: CleanTitle(bug.Title)))
                         .Select(entry => (entry.Bug, entry.Title, Read: Read(entry.Title, others)))
                         .Where(entry => entry.Read.Term is not null || entry.Read.Failure || entry.Read.Mentioned is not null)
                         .OrderByDescending(entry => entry.Read.Term is not null)
                         .ThenByDescending(entry => entry.Read.Mentioned is not null))
            {
                if (findings.Count >= MaxBugFindings) break;

                var (summary, title, read) = bug;
                string? report = null;
                NexusComment? answer = null;

                if (summary.Replies > 0 && opened < MaxBugsOpened && !_pagesBlocked)
                {
                    opened++;
                    await Task.Delay(Pause, ct);

                    var (posts, fetch) = await NexusBugReader.ReadBugThreadAsync(http, summary.IssueId, session, ct);
                    if (fetch.Blocked) _pagesBlocked = true;

                    report = posts.FirstOrDefault()?.Text;

                    // The tracker does not mark the author, so the author is whoever posts under the
                    // name the mod was uploaded with, or that the comment thread marked as theirs.
                    // Their last word is the one that stands.
                    answer = posts.Skip(1).LastOrDefault(post => authors.Contains(post.Author));

                    read = Merge(read, Read(string.Join("\n", posts.Select(post => post.Text)), others));
                }

                findings.Add(BugFinding(mod, summary, title, report, answer, read, latest, source));
            }

            Findings.AddRange(findings);
            Coverage.Add(new ResearchCoverage(mod.Name, ResearchSource.BugReports,
                findings.Count > 0 ? CoverageStatus.Read : CoverageStatus.Empty, bugs.Count)
            {
                Searched = opened,
                Kept = findings.Count
            });
        }

        /// <summary>
        /// One crash report from the tracker, with the author's ruling folded into what it means.
        ///
        /// The status outranks the words. A report marked fixed is a cure if the user's copy is
        /// older than the fix, and nothing at all if they already have it; one marked "not a bug" is
        /// evidence against; an acknowledged one is a known problem.
        /// </summary>
        private ResearchFinding BugFinding(
            ResearchSubject mod,
            NexusBugSummary bug,
            string title,
            string? report,
            NexusComment? answer,
            CommentReading read,
            string? latest,
            string source)
        {
            // Null when either version is unknown - signed out of Nexus, or a mod whose manifest has
            // no version - and then no update is advised: a fixed report the user may well already
            // have the fix for is not something to put at the top of "what to try".
            var order = CrashLanguage.CompareVersions(mod.Version, latest);
            var upToDate = order is >= 0;
            var behind = order is < 0;

            var remedy = ReadStatements(answer?.Text, mod.Name).Remedy;
            if (remedy is null && bug.State == BugState.Fixed)
            {
                if (behind)
                    remedy = $"Update {mod.Name} to {latest} or later.";
                else if (order is null && read.Term is not null)
                    remedy = $"Make sure {mod.Name} is up to date - the author marked this report fixed.";
            }

            var polarity = bug.State switch
            {
                BugState.NotABug => Polarity.Clearance,
                BugState.Fixed => remedy is not null ? Polarity.Caution : Polarity.Context,
                BugState.Duplicate or BugState.NeedMoreInfo => Polarity.Context,
                _ when remedy is not null => Polarity.Caution,
                BugState.Known or BugState.WontFix or BugState.BeingLookedAt when read.Failure => Polarity.Blocker,
                _ when read.Term is not null => Polarity.Blocker,
                _ when read.Failure && read.Mentioned is not null => Polarity.Blocker,
                _ => Polarity.Context
            };

            var reasons = new List<string> { $"bug report marked \"{ConflictResearch.Describe(bug.State)}\"" };
            if (read.Term is not null) reasons.Insert(0, $"names \"{read.Term}\" from this crash");
            if (read.Mentioned is not null) reasons.Add($"about {read.Mentioned}");
            if (bug.State == BugState.Fixed && upToDate) reasons.Add("and you already have the latest version");
            if (answer is not null) reasons.Add("the author replied");

            // The report's own words when it was read - a title is a dozen words, and the
            // sentence that says what crashed is usually the second one.
            var quote = report is { Length: > 0 } ? $"{title}: {report}" : title;

            return new ResearchFinding(mod.Name, ConflictResearch.Trim(quote), string.Join(", ", reasons), source)
            {
                Polarity = polarity,
                Where = $"bug report \"{ConflictResearch.Trim(title, 60)}\"",
                FullQuote = quote,
                PageMod = mod.Name,
                MentionedMod = read.Mentioned,
                NamesTheOtherMod = read.Mentioned is not null,
                MatchesThisCrash = read.Term is not null,
                Answer = answer?.Text,
                AnsweredBy = answer?.Author ?? "",
                Treatment = remedy
            };
        }

        // ── Coverage ────────────────────────────────────────────────────────────

        private ResearchCoverage Refused(ResearchSubject mod, ResearchSource source, NexusFetch fetch)
        {
            if (fetch.Blocked) _pagesBlocked = true;

            return new ResearchCoverage(mod.Name, source, fetch.Blocked ? CoverageStatus.Blocked : CoverageStatus.Failed)
            {
                StatusCode = fetch.StatusCode
            };
        }

        private static ResearchCoverage Skipped(ResearchSubject mod, ResearchSource source, SkipReason reason) =>
            new(mod.Name, source, CoverageStatus.Skipped) { Skipped = reason };
    }

    /// <summary>
    /// What a post states and what it says to do, read a sentence at a time and skipping the
    /// questions.
    ///
    /// An author's reply often opens with a question of its own - "Did you update MOMI? It's
    /// compatible with March Expanded, update to 2.0.11." - and judging the post whole would let
    /// that question silence both the all-clear and the instruction beside it. A question is never
    /// an instruction, though: "is this fixed in 2.0.11?" is not "update to 2.0.11".
    /// </summary>
    internal static (CompatibilitySignal? Signal, string? Remedy) ReadStatements(string? text, string modName)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);

        CompatibilitySignal? verdict = null;
        CompatibilitySignal? mention = null;
        string? remedy = null;

        foreach (var sentence in ConflictResearch.Pieces(ConflictResearch.PlainTextKeepingBreaks(text)))
        {
            if (CrashLanguage.IsAQuestion(sentence)) continue;

            var signal = CompatibilityLanguage.Classify(sentence);
            if (signal is { Polarity: not Polarity.Context }) verdict ??= signal;
            else if (signal is not null) mention ??= signal;

            remedy ??= CrashLanguage.Remedy(sentence, modName);
        }

        return (verdict ?? mention, remedy);
    }

    /// <summary>What one comment or report says, read once and carried with it.</summary>
    private sealed record CommentReading(
        string? Term,
        string? Mentioned,
        bool Failure,
        bool Question,
        CompatibilitySignal? Compatibility);

    /// <summary>
    /// The bug list's title cell carries more than the title: a "Closed" badge in front and the mod
    /// version the report was filed against behind.
    /// </summary>
    internal static string CleanTitle(string title)
    {
        var text = Regex.Replace(title, @"^\s*Closed\s+", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\s+Version:\s.*$", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return text.Trim();
    }

    /// <summary>
    /// The order findings are shown in: this crash's own words first, then anything that says what
    /// to do, then the user's other mods, then the author - and within that, known problems ahead of
    /// conditions ahead of all-clears.
    /// </summary>
    internal static List<ResearchFinding> Rank(IEnumerable<ResearchFinding> findings) =>
        findings
            .GroupBy(finding => (finding.PageMod, finding.FullQuote))
            .Select(group => group.First())
            .OrderByDescending(finding => finding.MatchesThisCrash)
            .ThenByDescending(finding => finding.Treatment is not null)
            .ThenByDescending(finding => finding.MentionedMod is not null)
            .ThenByDescending(finding => finding.ByModAuthor || finding.Answer is not null)
            .ThenBy(finding => finding.Polarity switch
            {
                Polarity.Blocker => 0,
                Polarity.Caution => 1,
                Polarity.Clearance => 2,
                _ => 3
            })
            .ToList();
}
