using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Garethp.ModsOfMistriaGUI.Services;
using Garethp.ModsOfMistriaInstallerLib.Nexus;

namespace Garethp.ModsOfMistriaGUI.Views;

/// <summary>
/// A mod's release notes, every version the author wrote them for, newest first.
///
/// Each version gets a heading and its own notes beneath, because the question this answers is
/// usually "what changed since the copy I had?" - which needs several versions read in order, not
/// just the newest one.
/// </summary>
public partial class ChangelogWindow : Window
{
    private readonly string _plainText;

    // Required by Avalonia's compiled XAML loader; normal callers use ShowAsync.
    public ChangelogWindow() : this("", null)
    {
    }

    private ChangelogWindow(string modName, IReadOnlyList<ModChangelogEntry>? entries, string? focusVersion = null)
    {
        InitializeComponent();
        App.ApplyThemeClass(this);

        // A dialog taller than the screen's working area is centred with its top edge off
        // the display, which puts the title bar out of reach. See DialogBounds.
        Opened += (_, _) => this.FitToScreen();

        var texts = LocalizedTexts.Instance;
        Title = texts.GUIChangelogTitle;
        ModNameText.Text = modName;
        CopyButton.Content = texts.GUICopyReport;
        CloseButton.Content = texts.GUIClose;

        _plainText = BuildPlainText(modName, entries);

        CopyButton.IsVisible = entries is { Count: > 0 };
        CopyButton.Click += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null) await clipboard.SetTextAsync(_plainText);
        };
        CloseButton.Click += (_, _) => Close();

        if (entries is null)
        {
            StatusText.Text = texts.GUIChangelogUnavailable;
            return;
        }

        if (entries.Count == 0)
        {
            StatusText.Text = texts.GUIChangelogNone;
            return;
        }

        StatusText.Text = string.Format(texts.GUIChangelogVersionCount, entries.Count);

        Control? focused = null;
        foreach (var entry in entries)
        {
            var isFocus = focused is null && SameVersion(entry.Version, focusVersion);
            var control = CreateVersion(entry, isFocus);
            if (isFocus) focused = control;
            VersionsPanel.Children.Add(control);
        }

        if (!string.IsNullOrWhiteSpace(focusVersion))
        {
            // Said either way: opened from a restore point, the user is asking about one version,
            // and "the author wrote no notes for 1.0.3" is an answer too.
            StatusText.Text = focused is null
                ? string.Format(texts.GUIChangelogVersionNotListed, focusVersion)
                : string.Format(texts.GUIChangelogShowingVersion, focusVersion);
        }

        // After layout, or there is nothing to scroll to yet.
        if (focused is not null)
            Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(
                () => focused.BringIntoView(), Avalonia.Threading.DispatcherPriority.Background);
    }

    public static Task ShowAsync(Window owner, string modName, IReadOnlyList<ModChangelogEntry>? entries) =>
        new ChangelogWindow(modName, entries).ShowDialog(owner);

    /// <summary>
    /// Opens the history scrolled to one version, which is highlighted - for the release-notes icon
    /// beside a restore point, where the question is about that version specifically.
    /// </summary>
    public static Task ShowAsync(
        Window owner, string modName, IReadOnlyList<ModChangelogEntry>? entries, string? focusVersion) =>
        new ChangelogWindow(modName, entries, focusVersion).ShowDialog(owner);

    /// <summary>
    /// Nexus versions and manifest versions are typed by hand in two different places, so "v2.0.10"
    /// and "2.0.10" are the same release.
    /// </summary>
    internal static bool SameVersion(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;

        static string Clean(string version) => version.Trim().TrimStart('v', 'V').Trim();

        return string.Equals(Clean(left), Clean(right), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One version: its number as a heading, a rule under it, then the author's lines.
    ///
    /// A rule rather than only a bold heading, because several versions of terse one-line notes run
    /// together otherwise and the reader loses track of which line belongs to which release.
    /// </summary>
    private static Control CreateVersion(ModChangelogEntry entry, bool highlighted = false)
    {
        var lines = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(0, 8, 0, 0) };

        foreach (var line in entry.Lines)
        {
            lines.Children.Add(new SelectableTextBlock
            {
                Text = $"• {line}",
                TextWrapping = TextWrapping.Wrap
            });
        }

        var version = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                new SelectableTextBlock
                {
                    Text = string.Format(LocalizedTexts.Instance.GUIChangelogVersionHeading, entry.Version),
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap
                },
                new Border
                {
                    Height = 1,
                    Background = Brushes.Gray,
                    Opacity = 0.4,
                    Margin = new Avalonia.Thickness(0, 4, 0, 0)
                },
                lines
            }
        };

        if (!highlighted) return version;

        // A tinted band with an accent edge, the same shape the crash window uses for a finding,
        // so the version the user asked about stands out without a colour that only reads in one
        // theme.
        var accent = Color.Parse("#4c8bf5");
        return new Border
        {
            Background = new SolidColorBrush(accent, 0.10),
            BorderBrush = new SolidColorBrush(accent),
            BorderThickness = new Avalonia.Thickness(3, 0, 0, 0),
            CornerRadius = new Avalonia.CornerRadius(0, 4, 4, 0),
            Padding = new Avalonia.Thickness(10, 6, 10, 8),
            Child = version
        };
    }

    private static string BuildPlainText(string modName, IReadOnlyList<ModChangelogEntry>? entries)
    {
        if (entries is null || entries.Count == 0) return modName;

        var blocks = entries.Select(entry =>
            string.Format(LocalizedTexts.Instance.GUIChangelogVersionHeading, entry.Version) +
            Environment.NewLine + entry.Text);

        return modName + Environment.NewLine + Environment.NewLine +
               string.Join(Environment.NewLine + Environment.NewLine, blocks);
    }
}
