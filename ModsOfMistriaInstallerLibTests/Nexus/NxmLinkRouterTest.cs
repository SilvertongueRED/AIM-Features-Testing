using Garethp.ModsOfMistriaInstallerLib.Nexus;

namespace ModsOfMistriaInstallerLibTests.Nexus;

// Who receives a clicked "Mod Manager Download" link when link sharing is on. The game in the link
// decides who can use it: Fields of Mistria is AIM's, Stardew Valley belongs to Stardrop and Vortex,
// and Vortex takes everything else. Among those, the manager opened most recently wins, else the one
// that received the last link, else a manager installed for that game, else AIM.
[TestFixture]
public class NxmLinkRouterTest
{
    private const string Mistria = "fieldsofmistria";
    private const string Stardew = "stardewvalley";
    private const string Cyberpunk = "cyberpunk2077";

    private static readonly DateTime Morning = new(2026, 10, 17, 9, 0, 0);

    private static NxmRunningManager Open(string name, int minutesAfterMorning) =>
        new(name, Morning.AddMinutes(minutesAfterMorning));

    private static bool EverythingInstalled(string _) => true;

    // The case that went wrong in testing: AIM was opened, then Stardrop, then a Fields of Mistria
    // mod was downloaded from Nexus and the link was handed to Stardrop.
    [Test]
    public void ShouldKeepAFieldsOfMistriaLinkInAimEvenWhenStardropWasOpenedLater()
    {
        var decision = NxmLinkRouter.Decide(
            Mistria, [Open("AIM", 0), Open("Stardrop", 30)], "Stardrop", EverythingInstalled);

        Assert.That(decision.HandleHere, Is.True);
    }

    [Test]
    public void ShouldKeepAFieldsOfMistriaLinkInAimEvenWhenAimIsNotOpen()
    {
        var decision = NxmLinkRouter.Decide(Mistria, [Open("Vortex", 0)], "Vortex", EverythingInstalled);

        Assert.That(decision.HandleHere, Is.True);
    }

    [Test]
    public void ShouldSendAStardewLinkToTheStardewManagerOpenedMostRecently()
    {
        var decision = NxmLinkRouter.Decide(
            Stardew, [Open("Vortex", 0), Open("Stardrop", 30), Open("AIM", 60)], null, EverythingInstalled);

        Assert.Multiple(() =>
        {
            Assert.That(decision.ManagerName, Is.EqualTo("Stardrop"));
            Assert.That(decision.HandleHere, Is.False);
        });
    }

    [Test]
    public void ShouldSendAStardewLinkToVortexWhenVortexWasOpenedAfterStardrop()
    {
        var decision = NxmLinkRouter.Decide(
            Stardew, [Open("Stardrop", 0), Open("Vortex", 30)], null, EverythingInstalled);

        Assert.That(decision.ManagerName, Is.EqualTo("Vortex"));
    }

    [Test]
    public void ShouldNeverSendAnotherGamesLinkToStardrop()
    {
        var decision = NxmLinkRouter.Decide(
            Cyberpunk, [Open("Vortex", 0), Open("Stardrop", 30)], "Stardrop", EverythingInstalled);

        Assert.That(decision.ManagerName, Is.EqualTo("Vortex"));
    }

    [Test]
    public void ShouldSendAnotherGamesLinkToVortexWhenNothingIsOpen()
    {
        var decision = NxmLinkRouter.Decide(Cyberpunk, [], null, EverythingInstalled);

        Assert.That(decision.ManagerName, Is.EqualTo("Vortex"));
    }

    [Test]
    public void ShouldPreferTheManagerThatReceivedTheLastLinkWhenNoneIsOpen()
    {
        var decision = NxmLinkRouter.Decide(Stardew, [], "Vortex", EverythingInstalled);

        Assert.That(decision.ManagerName, Is.EqualTo("Vortex"));
    }

    [Test]
    public void ShouldIgnoreALastManagerThatCannotUseTheGame()
    {
        // Stardrop received the last link, but this one is for another game.
        var decision = NxmLinkRouter.Decide(Cyberpunk, [], "Stardrop", EverythingInstalled);

        Assert.That(decision.ManagerName, Is.EqualTo("Vortex"));
    }

    [Test]
    public void ShouldPreferTheManagerBuiltForTheGameWhenNothingIsOpenAndNoneReceivedALink()
    {
        var decision = NxmLinkRouter.Decide(Stardew, [], null, EverythingInstalled);

        Assert.That(decision.ManagerName, Is.EqualTo("Stardrop"));
    }

    [Test]
    public void ShouldFallBackToVortexForAStardewLinkWhenStardropIsNotInstalled()
    {
        var decision = NxmLinkRouter.Decide(Stardew, [], null, name => name == "Vortex");

        Assert.That(decision.ManagerName, Is.EqualTo("Vortex"));
    }

    [Test]
    public void ShouldKeepTheLinkWhenNoManagerCanTakeIt()
    {
        // A missing link is worse than a link in the "wrong" manager, so AIM takes it.
        Assert.That(NxmLinkRouter.Decide(Cyberpunk, [], null, _ => false).HandleHere, Is.True);
        Assert.That(NxmLinkRouter.Decide(Stardew, [Open("Stardrop", 5)], "Stardrop", _ => false).HandleHere, Is.True);
    }

    [Test]
    public void ShouldSkipAnOpenManagerWhoseExecutableCannotBeFound()
    {
        // Stardrop is the newest, but AIM does not know where it lives, so it cannot be launched.
        var decision = NxmLinkRouter.Decide(
            Stardew, [Open("Vortex", 0), Open("Stardrop", 30)], null, name => name == "Vortex");

        Assert.That(decision.ManagerName, Is.EqualTo("Vortex"));
    }

    [Test]
    public void ShouldOfferALinkWithNoReadableGameToEveryManager()
    {
        var decision = NxmLinkRouter.Decide(
            null, [Open("Vortex", 0), Open("Stardrop", 30)], null, EverythingInstalled);

        Assert.That(decision.ManagerName, Is.EqualTo("Stardrop"));
    }

    [TestCase("nxm://fieldsofmistria/mods/1/files/2?key=a&expires=1&user_id=2", "fieldsofmistria")]
    [TestCase("NXM://StardewValley/mods/1/files/2", "stardewvalley")]
    [TestCase("  nxm://cyberpunk2077/mods/9/files/8  ", "cyberpunk2077")]
    public void ShouldReadTheGameFromALink(string link, string expected)
    {
        Assert.That(NxmLinkRouter.GetGameDomain(link), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("not a link")]
    [TestCase("https://www.nexusmods.com/fieldsofmistria")]
    public void ShouldReadNoGameFromAnythingThatIsNotAnNxmLink(string link)
    {
        Assert.That(NxmLinkRouter.GetGameDomain(link), Is.Null);
    }

    [TestCase("AIM", "fieldsofmistria", true)]
    [TestCase("AIM", "stardewvalley", false)]
    [TestCase("aim", "FieldsOfMistria", true)]
    [TestCase("Stardrop", "stardewvalley", true)]
    [TestCase("Stardrop", "fieldsofmistria", false)]
    [TestCase("Vortex", "fieldsofmistria", true)]
    [TestCase("Vortex", "cyberpunk2077", true)]
    [TestCase("SomethingElse", "stardewvalley", false)]
    public void ShouldKnowWhichGamesEachManagerCanUse(string manager, string game, bool expected)
    {
        Assert.That(NxmLinkRouter.Supports(manager, game), Is.EqualTo(expected));
    }

    [TestCase("-d \"%1\"", "-d \"nxm://fieldsofmistria/mods/1/files/2\"", TestName = "Vortex arguments")]
    [TestCase("--nxm \"%1\"", "--nxm \"nxm://fieldsofmistria/mods/1/files/2\"", TestName = "Stardrop arguments")]
    [TestCase("--open", "--open \"nxm://fieldsofmistria/mods/1/files/2\"", TestName = "a template without %1")]
    public void ShouldBuildEachManagersCommandLine(string template, string expected)
    {
        Assert.That(
            NxmLinkRouter.BuildArguments(template, "nxm://fieldsofmistria/mods/1/files/2"),
            Is.EqualTo(expected));
    }

    [Test]
    public void ShouldKeepTheQueryStringOfASignedLinkIntact()
    {
        const string link = "nxm://fieldsofmistria/mods/1/files/2?key=abc&expires=123&user_id=4";

        Assert.That(NxmLinkRouter.BuildArguments("-d \"%1\"", link), Is.EqualTo($"-d \"{link}\""));
    }

    [Test]
    public void ShouldKnowHowVortexAndStardropReceiveLinks()
    {
        var managers = NxmLinkRouter.KnownManagers.ToDictionary(m => m.Name, m => m.ArgumentTemplate);

        Assert.Multiple(() =>
        {
            Assert.That(managers["Vortex"], Is.EqualTo("-d \"%1\""));
            Assert.That(managers["Stardrop"], Is.EqualTo("--nxm \"%1\""));
        });
    }
}
