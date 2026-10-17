using Garethp.ModsOfMistriaGUI.Views;
using NUnit.Framework;

namespace ModsOfMistriaGUITests;

public class IssueSummaryTest
{
    private const string AndMore = "{0} and {1} more";

    private static List<string> Names(int n) => Enumerable.Range(1, n).Select(i => $"Mod{i}").ToList();

    [Test]
    public void ShortListIsLeftAlone()
    {
        var names = Names(IssueSummary.Limit);
        var msg = string.Join(", ", names.Select(x => $"\"{x}\"")) + " write to the same file.";
        Assert.That(IssueSummary.Condense(msg, names, AndMore), Is.EqualTo(msg));
    }

    [Test]
    public void LongListIsCondensed()
    {
        var names = Names(10);
        var msg = string.Join(", ", names.Select(x => $"\"{x}\"")) + " write to the same file.";
        Assert.That(IssueSummary.Condense(msg, names, AndMore),
            Is.EqualTo("\"Mod1\", \"Mod2\", \"Mod3\" and 7 more write to the same file."));
    }

    [Test]
    public void UnexpectedWordingIsNeverMangled()
    {
        var names = Names(10);
        const string msg = "Something else entirely.";
        Assert.That(IssueSummary.Condense(msg, names, AndMore), Is.EqualTo(msg));
    }
}
