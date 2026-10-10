using Garethp.ModsOfMistriaInstallerLib.Research;

namespace ModsOfMistriaInstallerLibTests.Research;

// Reading a comment or a bug report for what it says about a crash, and for what it says to do.
[TestFixture]
public class CrashLanguageTest
{
    [TestCase("The game crashes every time I enter the inn")]
    [TestCase("Getting a fatal error on load")]
    [TestCase("game won't load after installing this")]
    [TestCase("It froze on the loading screen")]
    [TestCase("Black screen after the intro")]
    [TestCase("FoM doesn't start with this enabled")]
    public void ShouldRecogniseTalkOfTheGameFailing(string text)
    {
        Assert.That(CrashLanguage.IsAboutAFailure(text), Is.True);
    }

    [TestCase("Love this mod, March is adorable")]
    [TestCase("The sprite is a slightly different colour")]
    [TestCase("Updates the mod so it no longer breaks with game patches/GML updates")]
    public void ShouldNotCallEveryProblemACrash(string text)
    {
        Assert.That(CrashLanguage.IsAboutAFailure(text), Is.False);
    }

    [TestCase("Does this work with the March Sylus mod?", true)]
    [TestCase("is this compatible with Crys's tiles", true)]
    [TestCase("They are now compatible if you download the optional file", false)]
    public void ShouldTellAQuestionFromAStatement(string text, bool question)
    {
        Assert.That(CrashLanguage.IsAQuestion(text), Is.EqualTo(question));
    }

    [Test]
    public void ShouldMatchTheCrashsOwnIdentifiersAsWholeWordsOnly()
    {
        string[] terms = ["spr_decor_cottage_arch", "item"];

        Assert.That(CrashLanguage.FirstTermIn("ERROR: spr_decor_cottage_arch not found", terms),
            Is.EqualTo("spr_decor_cottage_arch"));
        Assert.That(CrashLanguage.FirstTermIn("I sold all my items", terms), Is.Null,
            "\"item\" is not a mention of \"items\"");
        Assert.That(CrashLanguage.FirstTermIn("spr_decor_cottage_arch_v2 missing", terms), Is.Null,
            "a longer identifier is a different identifier");
    }

    [TestCase("Fixed in 2.0.11, please update", "Update March Expanded to 2.0.11 or later.")]
    [TestCase("This was resolved as of version 1.4.2", "Update March Expanded to 1.4.2 or later.")]
    [TestCase("Please update to v2.1 and it should stop", "Update March Expanded to v2.1 or later.")]
    [TestCase("Just grab the latest version from the files tab", "Update March Expanded to its latest version.")]
    [TestCase("March is now updated to the current version", "Update March Expanded to its latest version.")]
    [TestCase("Delete the old folder before installing the new one", "Delete the old March Expanded folder, then install the current version fresh.")]
    [TestCase("Try a clean reinstall", "Reinstall March Expanded from a fresh download.")]
    [TestCase("Load it after Butch March and it works", "Load March Expanded after Butch March.")]
    [TestCase("make sure to download both the regular March Expanded as well as the Portrait Compatibility Patch",
        "Install the Portrait Compatibility Patch.")]
    [TestCase("You need the latest MOMI for this", "Update MOMI to its latest version.")]
    [TestCase("Disable Seasonal Grass first.", "Turn off Seasonal Grass.")]
    public void ShouldTurnWhatAPageSaysToDoIntoAnInstruction(string text, string expected)
    {
        Assert.That(CrashLanguage.Remedy(text, "March Expanded"), Is.EqualTo(expected));
    }

    [TestCase("the game was updated and now it crashes")]
    [TestCase("Love the mod!")]
    [TestCase("disable it and see")]
    public void ShouldNotInventARemedy(string text)
    {
        Assert.That(CrashLanguage.Remedy(text, "March Expanded"), Is.Null);
    }

    [TestCase("2.0.10", "2.0.9", 1)]
    [TestCase("v1.2", "1.2.0", 0)]
    [TestCase("1.0", "1.0.1", -1)]
    public void ShouldCompareVersionsTheWayAuthorsWriteThem(string left, string right, int expected)
    {
        Assert.That(CrashLanguage.CompareVersions(left, right), Is.EqualTo(expected));
    }

    [Test]
    public void ShouldNotGuessWhichOfTwoNonVersionsIsNewer()
    {
        Assert.That(CrashLanguage.CompareVersions("Beta", "1.0"), Is.Null);
        Assert.That(CrashLanguage.CompareVersions("1.0", null), Is.Null);
    }

    [TestCase("Fix crash on startup", true)]
    [TestCase("- Fix crash on startup", true)]
    [TestCase("Fixed the inn crash", true)]
    [TestCase("works now, thanks!", true)]
    [TestCase("Tried the fix you posted, still crashes", false)]
    public void ShouldTellAFixFromAMentionOfOne(string text, bool saysFixed)
    {
        Assert.That(CrashLanguage.SaysFixed(text), Is.EqualTo(saysFixed));
    }

    [Test]
    public void ShouldNotTakeAnInstructionFromAQuestion()
    {
        // The researcher refuses remedies from questions before asking for one; the language rule
        // itself only has to recognise the question.
        Assert.That(CrashLanguage.IsAQuestion("Is this fixed in 2.0.11?"), Is.True);
    }
}
