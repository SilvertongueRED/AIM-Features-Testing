using Garethp.ModsOfMistriaInstallerLib;
using Garethp.ModsOfMistriaInstallerLib.Research;

namespace ModsOfMistriaInstallerLibTests;

// The part of "find a fix" that needs no network: which pages the user is pointed at, and how the
// search that covers everything outside Nexus is phrased.
[TestFixture]
public class ConflictResearchTest
{
    private static readonly ResearchSubject Remind =
        new("Remind Me", 812, "https://www.nexusmods.com/fieldsofmistria/mods/812");

    private static readonly ResearchSubject Gift =
        new("The Perfect Gift", 640, null);

    [Test]
    public async Task ShouldPointAtEachModsBugsPostsAndFiles()
    {
        var result = await ConflictResearch.InvestigateAsync([Remind, Gift], null);

        var urls = result.Links.Select(link => link.Url).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(urls, Does.Contain("https://www.nexusmods.com/fieldsofmistria/mods/812?tab=bugs"));
            Assert.That(urls, Does.Contain("https://www.nexusmods.com/fieldsofmistria/mods/812?tab=posts"));
            Assert.That(urls, Does.Contain("https://www.nexusmods.com/fieldsofmistria/mods/812?tab=files"));
            // A mod with an id but no recorded page URL still gets a page built from the id.
            Assert.That(urls, Does.Contain("https://www.nexusmods.com/fieldsofmistria/mods/640?tab=bugs"));
        });
    }

    // A mod AIM has no Nexus identity for has no page to open, but it must still reach the search.
    [Test]
    public async Task ShouldStillOfferASearchForAModWithNoNexusIdentity()
    {
        var handmade = new ResearchSubject("Hand Made Mod", null, null);

        var result = await ConflictResearch.InvestigateAsync([Remind, handmade], null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Links.Where(link => link.Url.Contains("nexusmods.com/fieldsofmistria/mods")),
                Has.Exactly(3).Items);
            Assert.That(result.Links.Select(link => link.Url), Has.One.Contains("duckduckgo.com"));
        });
    }

    [Test]
    public void ShouldNameEveryModAndTheGameInTheWebSearch()
    {
        var url = ConflictResearch.WebSearchUrl([Remind, Gift]);
        var query = Uri.UnescapeDataString(url["https://duckduckgo.com/?q=".Length..]);

        Assert.Multiple(() =>
        {
            Assert.That(query, Does.Contain("Fields of Mistria"));
            Assert.That(query, Does.Contain("\"Remind Me\""));
            Assert.That(query, Does.Contain("\"The Perfect Gift\""));
            Assert.That(query, Does.Contain("compatibility"));
        });
    }

    // With no API key there is no client, and the window says so rather than pretending it looked.
    [Test]
    public async Task ShouldFindNothingWithoutAnApiClient()
    {
        var result = await ConflictResearch.InvestigateAsync([Remind, Gift], null);

        Assert.That(result.FoundNothing, Is.True);
    }

    // ── Keeping the whole sentence ───────────────────────────────────────────────

    [Test]
    public void ShouldTreatTheQuoteAsTheWholeSentenceWhenNothingWasCutOff()
    {
        var finding = new ResearchFinding("Remind Me", "It works fine with everything.", "why", "url");

        Assert.Multiple(() =>
        {
            Assert.That(finding.FullQuote, Is.EqualTo("It works fine with everything."));
            Assert.That(finding.IsShortened, Is.False);
        });
    }

    [Test]
    public void ShouldKnowWhenTheDisplayedQuoteIsOnlyPartOfWhatWasSaid()
    {
        var finding = new ResearchFinding("Remind Me", "Load March…", "why", "url")
        {
            FullQuote = "Load March Expanded after this one and both work."
        };

        Assert.Multiple(() =>
        {
            Assert.That(finding.IsShortened, Is.True);
            Assert.That(finding.FullQuote, Does.Contain("both work"));
        });
    }

    // A `with` expression must not quietly drop the full text, or re-ranking findings would throw
    // away the very thing the window needs to expand them.
    [Test]
    public void ShouldCarryTheFullQuoteThroughACopy()
    {
        var finding = new ResearchFinding("Remind Me", "Load March…", "why", "url")
        {
            FullQuote = "Load March Expanded after this one and both work."
        };

        var copy = finding with { Polarity = Polarity.Caution };

        Assert.That(copy.FullQuote, Is.EqualTo(finding.FullQuote));
    }

    // ── The phrase to paste into a find bar ──────────────────────────────────────

    [Test]
    public void ShouldOfferAShortEnoughPhraseToFindOnThePage()
    {
        var finding = new ResearchFinding("Remind Me", "short", "why", "url")
        {
            FullQuote = "Anything that changes existing dialogue, most notably when the farmer " +
                        "is involved, should be considered incompatible."
        };

        Assert.Multiple(() =>
        {
            Assert.That(finding.SearchPhrase, Does.StartWith("Anything that changes existing"));
            Assert.That(finding.SearchPhrase.Length, Is.LessThanOrEqualTo(60));

            // Cut at a word, never mid-word: half a word finds nothing.
            Assert.That(finding.SearchPhrase, Does.Not.EndWith(" "));
            Assert.That(finding.FullQuote, Does.StartWith(finding.SearchPhrase));
        });
    }

    // A changelog bullet's own marker is decoration the page draws, not text the user can search
    // for, and pasting it into a find bar matches nothing.
    [Test]
    public void ShouldDropListMarkersFromTheSearchPhrase()
    {
        var finding = new ResearchFinding("March Expanded", "•  Updates the mod", "why", "url")
        {
            FullQuote = "•  Updates the mod so it no longer breaks with game patches"
        };

        Assert.That(finding.SearchPhrase, Does.StartWith("Updates the mod"));
    }

    [Test]
    public void ShouldUseTheWholeQuoteWhenItIsAlreadyShort()
    {
        var finding = new ResearchFinding("Remind Me", "They are incompatible.", "why", "url");

        Assert.That(finding.SearchPhrase, Is.EqualTo("They are incompatible."));
    }

    // ── BBCode ───────────────────────────────────────────────────────────────────

    // A link tag carrying a full Nexus URL is far longer than the old forty-character cap, so it
    // survived into the quote and was shown to the user as if the author had typed it.
    [Test]
    public void ShouldStripLinkTagsHoweverLongTheUrlInThemIs()
    {
        var plain = ConflictResearch.PlainText(
            "Including: [url=https://www.nexusmods.com/fieldsofmistria/mods/669]March Expanded[/url] " +
            "(load March last)");

        Assert.Multiple(() =>
        {
            Assert.That(plain, Does.Not.Contain("[url"));
            Assert.That(plain, Does.Not.Contain("nexusmods.com"));
            Assert.That(plain, Does.Contain("March Expanded"));
            Assert.That(plain, Does.Contain("load March last"));
        });
    }

    [Test]
    public void ShouldStripTheBbCodeTagsAnAuthorActuallyUses()
    {
        var plain = ConflictResearch.PlainText(
            "[b]Warning[/b]: [size=3]this [i]replaces[/i] March's lines[/size][*]and Ryis's");

        Assert.Multiple(() =>
        {
            Assert.That(plain, Does.Not.Contain("["));
            Assert.That(plain, Does.Contain("Warning"));
            Assert.That(plain, Does.Contain("replaces"));
        });
    }

    // The other half of the old pattern's failure: prose in brackets is not markup, and eating it
    // changed what the author said.
    [Test]
    public void ShouldLeaveBracketedProseAlone()
    {
        var plain = ConflictResearch.PlainText("Incompatible [see the FAQ] with dialogue mods");

        Assert.That(plain, Does.Contain("[see the FAQ]"));
    }

    // Keeping line breaks stops a bulleted list running together into one quote - and separates a
    // list's verdict from the names it is about. They have to be put back together.
    [Test]
    public void ShouldReadAListOfIncompatibleModsUnderItsLeadIn()
    {
        var mod = new ResearchSubject("Portrait Pack", 1, null);

        var findings = ConflictResearch.Scan(mod,
                "Incompatible with:<br />[list][*]March Expanded[*]Effe's Ears[/list]Enjoy!",
                ["March Expanded", "Seasonal Grass"], "https://example.invalid", "description")
            .ToList();

        Assert.That(findings, Has.Count.EqualTo(1));
        Assert.That(findings[0].FullQuote, Is.EqualTo("Incompatible with: March Expanded"));
        Assert.That(findings[0].Polarity, Is.EqualTo(Polarity.Blocker));
        Assert.That(findings[0].NamesTheOtherMod, Is.True);
    }

    [Test]
    public void ShouldReadAHeadingAsTheVerdictForItsList()
    {
        var mod = new ResearchSubject("Portrait Pack", 1, null);

        var findings = ConflictResearch.Scan(mod,
                "Known conflicts:\nSeasonal Grass\n\nCompatible with:\nMarch Expanded",
                ["March Expanded", "Seasonal Grass"], "https://example.invalid", "description")
            .ToList();

        Assert.That(findings.Select(finding => (finding.FullQuote, finding.Polarity)), Is.EquivalentTo(new[]
        {
            ("Known conflicts: Seasonal Grass", Polarity.Blocker),
            ("Compatible with: March Expanded", Polarity.Clearance)
        }));
    }

    // The screenshot this started from: a description's bullet list, an image link and raw [/*]
    // tags quoted as one block, when only its last sentence said anything.
    [Test]
    public void ShouldNotRunABulletedListIntoOneQuote()
    {
        var plain = ConflictResearch.PlainTextKeepingBreaks(
            "Lines: Banked, general [/*] Friday night at the inn [/*] https://i.postimg.cc/x/compatibility.png Dialogue I can only guarantee compatibility with patched mods.");

        Assert.That(plain, Does.Not.Contain("[/*]").And.Not.Contain("postimg"));
        Assert.That(ConflictResearch.Sentences(plain), Does.Contain("Dialogue I can only guarantee compatibility with patched mods."));
    }

    [Test]
    public void ShouldEndAListWhereALineGivesAVerdictOfItsOwn()
    {
        var mod = new ResearchSubject("Portrait Pack", 1, null);

        var findings = ConflictResearch.Scan(mod,
                "Compatible with:<br />- Better Dialogue<br />Not compatible with March Expanded (it overwrites the inn).",
                ["March Expanded", "Better Dialogue"], "https://example.invalid", "description")
            .ToList();

        Assert.That(findings.Select(finding => (finding.FullQuote, finding.Polarity)), Is.EquivalentTo(new[]
        {
            ("Compatible with: Better Dialogue", Polarity.Clearance),
            ("Not compatible with March Expanded (it overwrites the inn).", Polarity.Blocker)
        }), "March Expanded is not one more item under \"Compatible with\"");
    }

    [Test]
    public void ShouldStillReadALeadInWhoseListNamesNoOtherMod()
    {
        var mod = new ResearchSubject("Bakery Overhaul", 1, null);

        var findings = ConflictResearch.Scan(mod,
                "This mod is not compatible with any other mod that edits the bakery, for this reason:\nIt replaces the whole shop.",
                ["March Expanded"], "https://example.invalid", "description")
            .ToList();

        Assert.That(findings, Has.Count.EqualTo(1));
        Assert.That(findings[0].Polarity, Is.EqualTo(Polarity.Blocker));
    }
}
