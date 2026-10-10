using System.Net;
using System.Text;
using Garethp.ModsOfMistriaInstallerLib;
using Garethp.ModsOfMistriaInstallerLib.Crash;
using Garethp.ModsOfMistriaInstallerLib.Nexus;
using Garethp.ModsOfMistriaInstallerLib.Research;

namespace ModsOfMistriaInstallerLibTests.Research;

// Reading the suspects' Nexus pages for one crash.
//
// These run the whole researcher against a fake Nexus that serves what the real one does - the
// comment widget, the bug tracker, the API - so what is under test is the reading and the judging,
// not the network. The March Expanded lines are verbatim from its page: they are the ones that came
// back wrong in the crash window and started this.
[TestFixture]
public class CrashResearchTest
{
    // The real page's description, as the API serves it: BBCode list items run together, an image
    // link, and the one sentence that matters at the end of it all.
    private const string MarchDescription =
        "What was overhauled so far: Individual dialogue lines: Banked, general, gift, festival, museum, market [/*] " +
        "Friday night at the inn conversations [/*] All heart events (each heart level from 1-7) [/*] How to trigger " +
        "the modded events: 1h: have the mines unlocked, walk into the Narrows 3h: have at least lvl 10 blacksmithing " +
        "https://i.postimg.cc/QCJYBLXr/compatibility.png Dialogue I can only guarantee compatibility with dialogue " +
        "mods that either I or the other mod's author maintain a compatibility patch for.";

    private const string ContextPage = """
        <div class="container comments condensed"><ol><li class="comment " id="comment-173549301"> <a href="#"></a> <div class="comment-head clearfix"> <a class="comment-user" href="#"> <img></img> </a> <div class="comment-details"> <span class="comment-name"> <a href="#"> Questioner </a> </span> <ul class="clearfix"> <li> <span class="status-user"> member </span> </li> <li>0 kudos</li> </ul> </div> <a href="#"></a> <div class="comment-actions"> <ul class="actions clearfix"> <li> <a class="popup-btn-ajax btn inline-flex" href="#"> <svg></svg> <span class="flex-label">Report</span> </a> </li> <li> <a class="btn inline-flex" id="button-reply-to-173549301" href="#"> <svg></svg> <span class="flex-label">Reply</span> </a> </li> </ul> </div> </div> <div class="comment-content"> <div class="locked" id="locked-comment-label-173549301"> Locked </div> <div class="sticky" id="sticky-comment-label-173549301"> Sticky </div> <time class="dst-date-adjust">09 Aug 2026, 5:58AM</time> <div class="comment-content-text" id="comment-content-173549301"> Does this work with the March Sylus mod? </div> <ul class="comment-inline clearfix"> </ul> <div class="comment-reply clearfix" id="add-comment-form-173549301"> <div> <h3>Add a reply</h3> <textarea></textarea> <div class="comment-char-limit" id="char-limit-add-comment-173549301">0 / 5000</div> <ul class="choice-list"> <li> <input></input> <label><span class="choice-check"></span><span class="check-label">Use emoticons</span></label> </li> </ul> <ul class="actions clearfix"> <li> <a class="btn  submit-comment" id="submit-add-comment-173549301" href="#"> Submit </a> </li> <li> <a class="btn" href="#"> Cancel </a> </li> </ul> <div class="info warning notice clearfix" id="add-comment-error-box-173549301"> <svg></svg> <div class="info-content" id="add-comment-error-message-173549301"></div> </div> </div> </div> </div> <ol class="comment-kids"> <li class="comment comment-author" id="comment-173551326"> <a href="#"></a> <div class="comment-head clearfix"> <a class="comment-user" href="#"> <img></img> </a> <div class="comment-details"> <span class="comment-name"> <a href="#"> ModAuthor </a> </span> <ul class="clearfix"> <li> <span class="status-premium"> premium </span> </li> <li>248 kudos</li> </ul> </div> <a href="#"></a> <div class="comment-actions"> <ul class="actions clearfix"> <li> <a class="popup-btn-ajax btn inline-flex" href="#"> <svg></svg> <span class="flex-label">Report</span> </a> </li> <li> <a class="btn inline-flex" id="button-reply-to-173551326" href="#"> <svg></svg> <span class="flex-label">Reply</span> </a> </li> </ul> </div> </div> <div class="comment-content"> <div class="locked" id="locked-comment-label-173551326"> Locked </div> <div class="sticky" id="sticky-comment-label-173551326"> Sticky </div> <time class="dst-date-adjust">09 Aug 2026, 7:10AM</time> <div class="comment-content-text" id="comment-content-173551326"> Hey! If it only changes the portraits then yeah it should be compatible! Just make sure to download the portrait compatibility path as well </div> <ul class="comment-inline clearfix"> </ul> <div class="comment-reply clearfix" id="add-comment-form-173551326"> <div> <h3>Add a reply</h3> <textarea></textarea> <div class="comment-char-limit" id="char-limit-add-comment-173551326">0 / 5000</div> <ul class="choice-list"> <li> <input></input> <label><span class="choice-check"></span><span class="check-label">Use emoticons</span></label> </li> </ul> <ul class="actions clearfix"> <li> <a class="btn  submit-comment" id="submit-add-comment-173551326" href="#"> Submit </a> </li> <li> <a class="btn" href="#"> Cancel </a> </li> </ul> <div class="info warning notice clearfix" id="add-comment-error-box-173551326"> <svg></svg> <div class="info-content" id="add-comment-error-message-173551326"></div> </div> </div> </div> </div> </li> </ol> </li></ol></div>
        """;

    private const string TabPage = """
        <html><body><script>var cfg = {"thread_id":16773377};</script>
        <div class="container comments condensed"><ol>
        <li class="comment comment-sticky comment-author" id="comment-100"><div class="comment-head"><span class="comment-name"><a href="#">ModAuthor</a></span></div>
        <div class="comment-content"><div class="comment-content-text">Thanks for downloading! Please read the description before posting.</div></div></li>
        </ol></div></body></html>
        """;

    private const string SearchCompatible = """
        <div class="container comments condensed"><ol>
        <li class="comment comment-author" id="comment-173551326"><div class="comment-head"><span class="comment-name"><a href="#">ModAuthor</a></span></div>
        <div class="comment-content"><div class="comment-content-text">Hey! If it only changes the portraits then yeah it should be compatible! Just make sure to download the portrait compatibility path as well</div></div></li>
        <li class="comment " id="comment-174927939"><div class="comment-head"><span class="comment-name"><a href="#">Someone</a></span></div>
        <div class="comment-content"><div class="comment-content-text">pleeease add a compatability patch for Farmer of Dubious Repute!</div></div></li>
        </ol></div>
        """;

    private const string SearchCrashTerm = """
        <div class="container comments condensed"><ol>
        <li class="comment " id="comment-555"><div class="comment-head"><span class="comment-name"><a href="#">Player</a></span></div>
        <div class="comment-content"><div class="comment-content-text">Game crashes with ERROR: spr_foo_bar not found when I walk into the inn</div></div></li>
        </ol></div>
        """;

    private const string BugList = """
        <table><tbody>
        <tr class="mod-issue-row" data-issue-id="852000"><td class="table-bug-title">Closed Game wont open with march and ryis Version: 2.0.3</td><td class="table-bug-status">Fixed</td><td class="table-bug-replies">1</td></tr>
        <tr class="mod-issue-row" data-issue-id="900"><td class="table-bug-title">Typo in festival dialogue</td><td class="table-bug-status">New issue</td><td class="table-bug-replies">0</td></tr>
        </tbody></table>
        """;

    private const string BugThread = """
        <div class="comments"><ol>
        <li class="comment" id="bug-issue-tile-852000"><div class="comment-head"><span class="comment-name"><a href="#">Reporter</a></span></div><div class="comment-content"><time>21 August 2024</time> The game won't open with March and Ryis installed.</div></li>
        <li class="comment" id="bug-reply-tile-4242861"><div class="comment-head"><span class="comment-name"><a href="#">ModAuthor</a></span></div><div class="comment-content"><time>21 August 2024</time> sorry about that! Fixed in 2.0.4, please update.</div></li>
        </ol></div>
        """;

    private static string ModJson => $$"""
        {"name":"March Expanded - A Tsundere Dialogue Overhaul","summary":"","description":{{System.Text.Json.JsonSerializer.Serialize(MarchDescription)}},"version":"2.0.12","uploaded_by":"ModAuthor"}
        """;

    private const string ChangelogJson = """
        {"2.0.10":["Updates the mod so it no longer breaks with game patches/GML updates"],
         "2.0.11":["Fixed a crash when entering the inn with spr_foo_bar"],
         "2.0.2":["Fixed crash on startup"]}
        """;

    /// <summary>A Nexus that answers the requests the researcher makes, by what they ask for.</summary>
    private sealed class FakeNexus(bool blockPages = false) : HttpMessageHandler
    {
        public readonly List<string> Asked = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = Uri.UnescapeDataString(request.RequestUri!.ToString());
            Asked.Add(url);

            if (url.StartsWith("https://api.nexusmods.com/v1/"))
            {
                if (url.EndsWith("/changelogs.json")) return Json(ChangelogJson);
                if (url.EndsWith("/mods/669.json")) return Json(ModJson);
                return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            }

            if (url.Contains("file-metadata.nexusmods.com"))
                return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") };

            if (blockPages)
            {
                var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent("<html><head><title>Just a moment...</title></head></html>")
                };
                challenge.Headers.Add("cf-mitigated", "challenge");
                return challenge;
            }

            if (url.Contains("ModBugReplyList"))
            {
                var form = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
                return Html(form.Contains("852000") ? BugThread : "<div></div>");
            }

            if (url.Contains("ModBugsTab")) return Html(url.Contains("page:1") ? BugList : "<table></table>");

            if (url.Contains("CommentContainer"))
            {
                if (url.Contains("comment_id:173551326")) return Html(ContextPage);
                if (url.Contains("search_text:compatible")) return Html(SearchCompatible);
                if (url.Contains("search_text:spr_foo_bar")) return Html(SearchCrashTerm);
                return Html("<div class=\"container comments\"><ol></ol></div>");
            }

            if (url.Contains("?tab=posts")) return Html(TabPage);

            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") };
        }

        private static HttpResponseMessage Html(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/html") };

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private static CrashQuestion Question(params ResearchSubject[] suspects) => new(
        suspects,
        ["March Sylus", "Crys's Terraforming Tiles", "Seasonal Grass"],
        ["spr_foo_bar"]);

    private static readonly ResearchSubject March = new("March Expanded", 669, null) { Version = "2.0.3" };

    private static async Task<ResearchResult> Investigate(FakeNexus nexus, bool signedIn = true, params ResearchSubject[] suspects)
    {
        var http = new HttpClient(nexus);
        var client = signedIn ? new NexusApiClient("token", http) : null;

        return await CrashResearch.InvestigateAsync(
            Question(suspects.Length > 0 ? suspects : [March]), client, http);
    }

    [Test]
    public async Task ShouldPairTheAuthorsAnswerWithTheQuestionItAnswers()
    {
        var result = await Investigate(new FakeNexus());

        var pair = result.Findings.SingleOrDefault(finding => finding.MentionedMod == "March Sylus");

        Assert.That(pair, Is.Not.Null, "the author's reply is about March Sylus, which only the question names");
        Assert.That(pair!.FullQuote, Is.EqualTo("Does this work with the March Sylus mod?"));
        Assert.That(pair.Answer, Does.StartWith("Hey! If it only changes the portraits"));
        Assert.That(pair.AnsweredBy, Is.EqualTo("ModAuthor"));
        Assert.That(pair.Polarity, Is.EqualTo(Polarity.Clearance), "\"it should be compatible\" is the verdict");
    }

    [Test]
    public async Task ShouldFindACommentQuotingThisCrashsOwnError()
    {
        var result = await Investigate(new FakeNexus());

        var same = result.Findings.SingleOrDefault(finding => finding.FullQuote.Contains("spr_foo_bar not found"));

        Assert.That(same, Is.Not.Null);
        Assert.That(same!.MatchesThisCrash, Is.True);
        Assert.That(same.Polarity, Is.EqualTo(Polarity.Blocker), "somebody else hit this very crash");
    }

    [Test]
    public async Task ShouldOfferTheFixAnAuthorGaveOnTheBugTracker()
    {
        var result = await Investigate(new FakeNexus());

        var bug = result.Findings.SingleOrDefault(finding => finding.Where.Contains("Game wont open"));

        Assert.That(bug, Is.Not.Null);
        Assert.That(bug!.Where, Does.Not.Contain("Closed").And.Not.Contain("Version:"),
            "the status badge and the version cell are not part of the title");
        Assert.That(bug.Answer, Does.Contain("Fixed in 2.0.4"));
        Assert.That(bug.Treatment, Is.EqualTo("Update March Expanded to 2.0.4 or later."));
    }

    [Test]
    public async Task ShouldOfferAnUpdateWhenANewerReleaseFixedThisCrash()
    {
        var result = await Investigate(new FakeNexus());

        var update = result.Findings.SingleOrDefault(finding => finding.Where == "changelog for 2.0.11");

        Assert.That(update, Is.Not.Null);
        Assert.That(update!.Treatment, Is.EqualTo("Update March Expanded to 2.0.11 or later."));
        Assert.That(update.MatchesThisCrash, Is.True);
    }

    // The screenshot behind this one: March Expanded's 2.0.10 changelog line about surviving game
    // patches was shown under "says this is a known problem". It is a mod keeping up with the game,
    // true of every maintained mod, and says nothing about the crash in front of the user.
    [Test]
    public async Task ShouldNotCallAModKeepingUpWithTheGameAKnownProblem()
    {
        var result = await Investigate(new FakeNexus());

        Assert.That(result.Findings.Where(finding => finding.FullQuote.Contains("no longer breaks with game patches")),
            Is.Empty);
        Assert.That(result.Findings.Where(finding => finding.FullQuote.Contains("Fixed crash on startup")), Is.Empty,
            "a fix in 2.0.2 is already in the 2.0.3 the user has");
    }

    // The other screenshot: a whole bulleted list, an image link and raw [/*] tags shown as one quote,
    // when the only sentence that mattered was the last one.
    [Test]
    public async Task ShouldQuoteOnlyTheSentenceThatSaysSomething()
    {
        var result = await Investigate(new FakeNexus());

        var description = result.Findings.Where(finding => finding.Where == "description").ToList();

        Assert.That(description, Has.Count.EqualTo(1));
        Assert.That(description[0].FullQuote, Is.EqualTo(
            "Dialogue I can only guarantee compatibility with dialogue mods that either I or the other mod's author maintain a compatibility patch for."));
        Assert.That(description[0].Polarity, Is.EqualTo(Polarity.Caution));
    }

    [Test]
    public async Task ShouldSayWhatItReadAndHowMuch()
    {
        var result = await Investigate(new FakeNexus());

        var comments = result.Coverage.Single(line => line.Source == ResearchSource.Comments);
        var bugs = result.Coverage.Single(line => line.Source == ResearchSource.BugReports);

        Assert.That(comments.Status, Is.EqualTo(CoverageStatus.Read));
        Assert.That(comments.Searched, Is.GreaterThan(0));
        Assert.That(comments.Read, Is.GreaterThanOrEqualTo(4), "the pinned post, two search hits and the crash report");
        Assert.That(bugs.Status, Is.EqualTo(CoverageStatus.Read));
        Assert.That(bugs.Read, Is.EqualTo(2));
        Assert.That(bugs.Searched, Is.EqualTo(1), "only the crash report was worth opening");
    }

    [Test]
    public async Task ShouldSayWhenNexusBlockedThePagesRatherThanFindNothing()
    {
        var nexus = new FakeNexus(blockPages: true);
        var other = new ResearchSubject("Crys's Terraforming Tiles", 700, null);

        var result = await Investigate(nexus, true, March, other);

        Assert.That(result.Coverage.Single(line => line.ModName == "March Expanded" && line.Source == ResearchSource.Comments).Status,
            Is.EqualTo(CoverageStatus.Blocked));

        // Once the site has said no, it is not asked again for every other page.
        Assert.That(result.Coverage.Where(line => line.ModName == "Crys's Terraforming Tiles" &&
                                                  line.Source is ResearchSource.Comments or ResearchSource.BugReports)
                .Select(line => line.Skipped),
            Is.All.EqualTo(SkipReason.BlockedEarlier));
        Assert.That(nexus.Asked.Count(url => url.Contains("www.nexusmods.com")), Is.EqualTo(1));
    }

    [Test]
    public async Task ShouldReadThePublicPagesWithoutANexusSignIn()
    {
        var result = await Investigate(new FakeNexus(), signedIn: false);

        Assert.That(result.Coverage.Single(line => line.Source == ResearchSource.Description).Skipped,
            Is.EqualTo(SkipReason.NotSignedIn));
        Assert.That(result.Coverage.Single(line => line.Source == ResearchSource.Comments).Status,
            Is.EqualTo(CoverageStatus.Read));
    }

    [Test]
    public void ShouldSearchForTheCrashsOwnIdentifiersButNotForOrdinaryWords()
    {
        var crash = new GameCrashLog(
            "ERROR in action number 1 of gml_Script_spawn_npc_inn: field default_dialogue not set",
            [new CrashFrame(0, "assets/gml/scripts/Stores.gml", 12, null)],
            DateTimeOffset.UtcNow, "error_log.json", "")
        {
            Subject = "default_dialogue"
        };

        Assert.That(CrashResearch.TermsFor(crash), Is.EqualTo(new[] { "default_dialogue", "spawn_npc_inn" }),
            "\"Stores\" is the script it died in, and a word in a hundred comments about shops");
    }

    [TestCase("Closed Game wont open with march and ryis Version: 2.0.3", "Game wont open with march and ryis")]
    [TestCase("Typo in festival dialogue", "Typo in festival dialogue")]
    public void ShouldCleanABugTitle(string cell, string title)
    {
        Assert.That(CrashResearch.CleanTitle(cell), Is.EqualTo(title));
    }

    // An author's answer that opens with a question of its own still answers.
    [Test]
    public void ShouldNotLetAQuestionInAnAnswerSilenceTheRestOfIt()
    {
        var (signal, remedy) = CrashResearch.ReadStatements(
            "Did you update MOMI? It's compatible with March Expanded, update to 2.0.11.", "Portrait Pack");

        Assert.That(signal?.Polarity, Is.EqualTo(Polarity.Clearance));
        Assert.That(remedy, Is.EqualTo("Update Portrait Pack to 2.0.11 or later."));
    }

    [Test]
    public void ShouldNotTakeAnInstructionFromAQuestion()
    {
        Assert.That(CrashResearch.ReadStatements("Is this fixed in 2.0.11?", "Portrait Pack").Remedy, Is.Null);
    }
}
