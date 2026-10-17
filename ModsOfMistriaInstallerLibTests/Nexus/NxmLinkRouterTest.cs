using Garethp.ModsOfMistriaInstallerLib.Nexus;

namespace ModsOfMistriaInstallerLibTests.Nexus;

// Who receives a clicked "Mod Manager Download" link when link sharing is on: the manager opened
// most recently, else the one that received the last link, else AIM.
[TestFixture]
public class NxmLinkRouterTest
{
    private static readonly DateTime Morning = new(2026, 10, 17, 9, 0, 0);

    private static NxmRunningManager Open(string name, int minutesAfterMorning) =>
        new(name, Morning.AddMinutes(minutesAfterMorning));

    private static bool EverythingInstalled(string _) => true;

    [Test]
    public void ShouldSendTheLinkToTheManagerOpenedMostRecently()
    {
        var decision = NxmLinkRouter.Decide(
            [Open("Vortex", 0), Open("Stardrop", 30), Open("AIM", 10)], null, EverythingInstalled);

        Assert.Multiple(() =>
        {
            Assert.That(decision.ManagerName, Is.EqualTo("Stardrop"));
            Assert.That(decision.HandleHere, Is.False);
        });
    }

    [Test]
    public void ShouldKeepTheLinkWhenAimWasOpenedMostRecently()
    {
        var decision = NxmLinkRouter.Decide(
            [Open("Vortex", 0), Open("AIM", 30)], "Vortex", EverythingInstalled);

        Assert.That(decision.HandleHere, Is.True);
    }

    [Test]
    public void ShouldUseTheManagerThatReceivedTheLastLinkWhenNoneIsOpen()
    {
        var decision = NxmLinkRouter.Decide([], "Vortex", EverythingInstalled);

        Assert.That(decision.ManagerName, Is.EqualTo("Vortex"));
    }

    [Test]
    public void ShouldKeepTheLinkWhenNothingIsOpenAndAimReceivedTheLastLink()
    {
        Assert.That(NxmLinkRouter.Decide([], "AIM", EverythingInstalled).HandleHere, Is.True);
    }

    [Test]
    public void ShouldKeepTheLinkWhenNothingIsOpenAndNoLinkWasEverShared()
    {
        Assert.That(NxmLinkRouter.Decide([], null, EverythingInstalled).HandleHere, Is.True);
    }

    [Test]
    public void ShouldSkipAnOpenManagerWhoseExecutableCannotBeFound()
    {
        // Stardrop is the newest, but AIM does not know where it lives, so it cannot be launched.
        var decision = NxmLinkRouter.Decide(
            [Open("Vortex", 0), Open("Stardrop", 30)], null, name => name == "Vortex");

        Assert.That(decision.ManagerName, Is.EqualTo("Vortex"));
    }

    [Test]
    public void ShouldKeepTheLinkWhenTheLastManagerCannotBeFound()
    {
        // A missing link is worse than a link in the "wrong" manager, so AIM takes it.
        var decision = NxmLinkRouter.Decide([], "Stardrop", _ => false);

        Assert.That(decision.HandleHere, Is.True);
    }

    [Test]
    public void ShouldTreatTheAimNameCaseInsensitively()
    {
        Assert.That(NxmLinkRouter.Decide([Open("aim", 5)], null, _ => false).HandleHere, Is.True);
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
