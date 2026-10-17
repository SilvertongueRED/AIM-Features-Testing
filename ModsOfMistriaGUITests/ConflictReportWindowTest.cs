using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Garethp.ModsOfMistriaGUI.Services;
using Garethp.ModsOfMistriaGUI.Views;
using Garethp.ModsOfMistriaInstallerLib;

namespace ModsOfMistriaGUITests;

// Overlaps the Variants Framework resolves are listed for information, apart from the issues and
// without the "marked as fine" checkbox: there is nothing to judge, so nothing to dismiss.
public class ConflictReportWindowTest
{
    private static IssueParticipant Mod(string id, string name) => new(id, name, "1.0", "/mods/" + id);

    [AvaloniaTest]
    public void Should_List_Handled_Overlaps_Apart_From_The_Issues()
    {
        LocalizationService.Instance.SetLanguage("en");
        var mods = Path.Combine(Path.GetTempPath(), "aim_report_" + Path.GetRandomFileName());
        Directory.CreateDirectory(mods);
        try
        {
            var issue = new LoadOrderNote(LoadOrderNoteKind.FileConflict, "\"Second\" overrides \"First\" (1 shared file).")
            {
                IssueKey = "a.first,a.second",
                Details = ["animations/foo.png"],
                Participants = [Mod("a.first", "First"), Mod("a.second", "Second")],
            };
            var handled = new LoadOrderNote(LoadOrderNoteKind.HandledByVariants,
                "\"Third\", \"Fourth\" replace the same 1 image. Kept as switchable looks by the Variants Framework.")
            {
                IssueKey = "a.fourth,a.third|variants",
                Details = ["images/replace/spr_lamp.png"],
                Participants = [Mod("a.third", "Third"), Mod("a.fourth", "Fourth")],
            };

            var window = LoadOrderResultWindow.Show(
                new LoadOrderResultWindow.ReportContent("", [handled, issue]),
                "Issues",
                () => Task.FromResult(new LoadOrderResultWindow.ReportContent("", [])),
                new DismissedIssueStore(mods));

            var panel = window.FindControl<StackPanel>("NotesPanel")!;
            var children = panel.Children.ToList();

            Assert.Multiple(() =>
            {
                Assert.That(children, Has.Count.EqualTo(2));
                // The issue first, with its checkbox...
                Assert.That(children[0], Is.InstanceOf<Grid>());
                Assert.That(((Grid)children[0]).Children.OfType<CheckBox>().Count(), Is.EqualTo(1));
                // ...then the informational section, header and note, with no checkbox.
                var section = children[1] as Expander;
                Assert.That(section, Is.Not.Null);
                Assert.That((section!.Header as TextBlock)?.Text, Is.EqualTo("Handled automatically (1)"));
                Assert.That(section.IsExpanded, Is.False);
                Assert.That(((StackPanel)section.Content!).Children[0], Is.InstanceOf<Expander>());
                Assert.That(window.FindControl<CheckBox>("ShowDismissedToggle")!.IsVisible, Is.False);
            });

            window.Close();
        }
        finally
        {
            Directory.Delete(mods, true);
        }
    }

    [Test]
    public void Should_Localize_The_Variants_Lines_In_Every_Language()
    {
        try
        {
            LocalizationService.Instance.SetLanguage("en");
            var englishLine = LocalizedTexts.Instance.GUIFileConflictVariants;
            var englishHeader = LocalizedTexts.Instance.GUIIssueHandledHeader;
            Assert.That(englishHeader, Is.EqualTo("Handled automatically"));

            foreach (var language in new[] { "bg", "de", "es", "fr", "id", "ja", "ko", "nl", "pl", "pt-br", "ru", "uk", "zh-Hans", "zh-Hant" })
            {
                LocalizationService.Instance.SetLanguage(language);
                // The product name stays as it is; the rest is translated, not an English fallback.
                Assert.That(LocalizedTexts.Instance.GUIFileConflictVariants, Does.Contain("Variants Framework"), language);
                Assert.That(LocalizedTexts.Instance.GUIFileConflictVariants, Is.Not.EqualTo(englishLine), language);
                Assert.That(LocalizedTexts.Instance.GUIIssueHandledHeader, Is.Not.EqualTo(englishHeader), language);
            }
        }
        finally
        {
            LocalizationService.Instance.SetLanguage("en");
        }
    }
}
