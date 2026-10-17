using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Garethp.ModsOfMistriaGUI.Models;
using Garethp.ModsOfMistriaGUI.Services;
using Garethp.ModsOfMistriaInstallerLib;
using Garethp.ModsOfMistriaInstallerLib.GmlMods;

namespace Garethp.ModsOfMistriaGUI.Views;

public partial class LoadOrderResultWindow : Window
{
    public sealed record ReportContent(string Summary, IReadOnlyList<LoadOrderNote> Notes);

    private readonly Func<Task<ReportContent>>? _refreshReportAsync;

    // Not readonly: a refresh replaces the report the window is showing rather than opening a
    // second one, so the summary and the notes are whatever the last scan produced.
    private string _summary;
    private List<LoadOrderNote> _notes;

    /// <summary>Null for the load-order window, which reports actions rather than open questions.</summary>
    private readonly DismissedIssueStore? _dismissedIssues;

    /// <summary>Null when the window is only describing an issue, not offering to fix it.</summary>
    private readonly ConflictReportActions? _actions;

    /// <summary>
    /// Told whenever an issue is ticked off or put back, so the mod list can re-run its sweep.
    ///
    /// The report is modeless: it sits beside the list rather than over it, and it used to tell the
    /// list nothing until it was closed. So a user who worked down a screenful of issues watched the
    /// warning triangles stay exactly where they were, on mods whose only issue they had just
    /// settled - the icon and the report disagreeing for as long as the window stayed open.
    /// </summary>
    private readonly Action? _dismissalsChanged;

    private bool _showDismissed;

    /// <summary>Whether the "Handled automatically" section is open. Closed until the user opens it.</summary>
    private bool _handledExpanded;

    /// <summary>The issues currently marked fine, as of the last rebuild. The search filters these.</summary>
    private List<LoadOrderNote> _dismissedNotes = [];

    /// <summary>How the marked-fine list is ordered. Newest first unless the user picks otherwise.</summary>
    private DismissedOrder _dismissedOrder = DismissedOrder.NewestFirst;

    private enum DismissedOrder
    {
        NewestFirst,
        OldestFirst,
        ByName
    }

    // Rebuilt alongside the visible list so that what "Copy report" produces matches what the
    // window shows, dismissals included.
    private string _report = string.Empty;

    // Required by Avalonia's compiled XAML loader; normal callers use ShowAsync below.
    public LoadOrderResultWindow() : this(string.Empty, [])
    {
    }

    private LoadOrderResultWindow(
        string summary,
        IReadOnlyList<LoadOrderNote> notes,
        string? title = null,
        bool showCopyButton = false,
        bool compact = false,
        Func<Task<ReportContent>>? refreshReportAsync = null,
        DismissedIssueStore? dismissedIssues = null,
        ConflictReportActions? actions = null,
        Action? dismissalsChanged = null)
    {
        InitializeComponent();
        App.ApplyThemeClass(this);

        // A dialog taller than the screen's working area is centred with its top edge off
        // the display, which puts the title bar out of reach. See DialogBounds.
        Opened += (_, _) => this.FitToScreen();

        ScrollEnds.Attach(NotesScroller);

        Title = title ?? LocalizationService.Instance["GUILoadOrderTitle"];
        _refreshReportAsync = refreshReportAsync;
        _summary = summary;
        _notes = notes.ToList();
        _dismissedIssues = dismissedIssues;
        _actions = actions;
        _dismissalsChanged = dismissalsChanged;
        CopyButton.IsVisible = showCopyButton;
        RefreshButton.IsVisible = refreshReportAsync is not null;
        CloseButton.IsVisible = !compact;
        if (compact)
        {
            SizeToContent = SizeToContent.Height;
            Width = 520;
            MinWidth = 420;
            MaxWidth = 720;
            MinHeight = 0;
            MaxHeight = 420;
        }
        RefreshButton.Content = LocalizationService.Instance["GUIRefreshReport"];
        CopyButton.Content = LocalizationService.Instance["GUICopyReport"];
        CloseButton.Content = LocalizationService.Instance["GUIClose"];
        CopyButton.Click += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
                await clipboard.SetTextAsync(_report);
        };
        RefreshButton.Click += async (_, _) => await RefreshReportAsync();
        CloseButton.Click += (_, _) => Close();

        ShowDismissedToggle.IsCheckedChanged += (_, _) =>
        {
            var wanted = ShowDismissedToggle.IsChecked == true;
            if (wanted == _showDismissed) return;
            _showDismissed = wanted;
            Rebuild();
        };

        DismissedSearch.Watermark = Texts.GUIIssueSearchDismissed;
        DismissedSearch.TextChanged += (_, _) => RebuildDismissed();

        DismissedSort.ItemsSource = new[]
        {
            Texts.GUIIssueSortNewest,
            Texts.GUIIssueSortOldest,
            Texts.GUIIssueSortName
        };
        DismissedSort.SelectedIndex = 0;
        DismissedSort.SelectionChanged += (_, _) =>
        {
            _dismissedOrder = DismissedSort.SelectedIndex switch
            {
                1 => DismissedOrder.OldestFirst,
                2 => DismissedOrder.ByName,
                _ => DismissedOrder.NewestFirst
            };
            RebuildDismissed();
        };

        Rebuild();
    }

    public static Task ShowAsync(
        Window owner,
        string summary,
        IReadOnlyList<LoadOrderNote> notes,
        string? title = null,
        bool showCopyButton = false,
        bool compact = false,
        DismissedIssueStore? dismissedIssues = null,
        ConflictReportActions? actions = null)
    {
        return new LoadOrderResultWindow(
                summary, notes, title, showCopyButton, compact,
                dismissedIssues: dismissedIssues, actions: actions)
            .ShowDialog(owner);
    }

    /// <summary>
    /// Opens a modeless issue report. The caller supplies a deliberate refresh
    /// action because scanning a large mod set should never run for every edit.
    /// </summary>
    public static LoadOrderResultWindow Show(
        ReportContent report,
        string title,
        Func<Task<ReportContent>> refreshReportAsync,
        DismissedIssueStore? dismissedIssues = null,
        ConflictReportActions? actions = null,
        Action? dismissalsChanged = null)
    {
        var window = new LoadOrderResultWindow(
            report.Summary,
            report.Notes,
            title,
            showCopyButton: true,
            refreshReportAsync: refreshReportAsync,
            dismissedIssues: dismissedIssues,
            actions: actions,
            dismissalsChanged: dismissalsChanged);
        // A modeless issue report must be an independent top-level window.
        // Showing it with AIM as its owner keeps it permanently above AIM on
        // Windows, which defeats the purpose of keeping the main mod list usable.
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Show();
        return window;
    }

    private void ApplyReport(ReportContent report)
    {
        _summary = report.Summary;
        _notes = report.Notes.ToList();
        Rebuild();
    }

    private async Task RefreshReportAsync()
    {
        if (_refreshReportAsync is null) return;

        RefreshButton.IsEnabled = false;
        try
        {
            ApplyReport(await _refreshReportAsync());
        }
        catch (Exception exception)
        {
            SummaryText.Text = exception.Message;
            SummaryText.IsVisible = true;
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private static LocalizedTexts Texts => LocalizedTexts.Instance;

    /// <summary>
    /// Says that the answer to an issue has changed, so the mod list can catch up.
    ///
    /// Guarded because the listener re-scans the mod list: a failure there is not a reason to lose
    /// the tick the user just made, and this is called from event handlers that nothing catches.
    /// </summary>
    private void DismissalsChanged()
    {
        try
        {
            _dismissalsChanged?.Invoke();
        }
        catch (Exception exception)
        {
            Logger.Log($"Refreshing the mod list after an issue was settled failed: {exception}");
        }
    }

    // ── Rendering ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Redraws the whole list.
    ///
    /// Acting on an issue moves it between two sections, changes its wording, or removes a mod from
    /// it, so the cheapest correct thing is to rebuild rather than to patch controls in place. A
    /// conflict report is a few dozen rows at worst.
    /// </summary>
    private void Rebuild()
    {
        NotesPanel.Children.Clear();
        _report = BuildReport(_summary, _notes, _dismissedIssues);
        SummaryText.Text = _summary;
        SummaryText.IsVisible = !string.IsNullOrWhiteSpace(_summary);

        if (_dismissedIssues is null)
        {
            foreach (var note in _notes)
                NotesPanel.Children.Add(CreateNoteControl(note));
            ShowDismissedToggle.IsVisible = false;
            DismissedSection.IsVisible = false;
            return;
        }

        var live = new List<LoadOrderNote>();
        var dismissed = new List<LoadOrderNote>();
        var informational = new List<LoadOrderNote>();
        foreach (var note in _notes)
        {
            if (note.IsInformational) informational.Add(note);
            else (_dismissedIssues.IsDismissed(note.StableKey) ? dismissed : live).Add(note);
        }

        foreach (var note in live)
            NotesPanel.Children.Add(CreateDismissableRow(note, isDismissed: false));

        // Things AIM already resolves (portraits Portrait Wardrobe keeps side by side). Shown so the
        // user can see which mods share art, but with no checkbox: there is nothing to judge, and a
        // tick here would only clutter the dismissal store.
        if (informational.Count > 0)
        {
            // Collapsed unless the user opened it: there can be dozens of these and none needs
            // action. Which way it was left survives a rebuild.
            var handled = new StackPanel { Spacing = 10 };
            foreach (var note in informational)
                handled.Children.Add(CreateNoteControl(note));

            var section = new Expander
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Margin = new Avalonia.Thickness(0, live.Count > 0 ? 12 : 0, 0, 0),
                Header = new TextBlock
                {
                    Text = $"{Texts.GUIIssueHandledHeader} ({informational.Count})",
                    FontWeight = FontWeight.SemiBold,
                    Opacity = 0.7
                },
                Content = handled,
                IsExpanded = _handledExpanded
            };
            section.PropertyChanged += (_, args) =>
            {
                if (args.Property == Expander.IsExpandedProperty) _handledExpanded = section.IsExpanded;
            };
            NotesPanel.Children.Add(section);
        }

        ShowDismissedToggle.IsVisible = dismissed.Count > 0;
        ShowDismissedToggle.Content = string.Format(Texts.GUIIssueShowDismissed, dismissed.Count);

        _dismissedNotes = dismissed;
        DismissedSection.IsVisible = dismissed.Count > 0 && _showDismissed;
        RebuildDismissed();
    }

    /// <summary>
    /// Redraws only the issues marked fine, filtered by the search box and in the chosen order.
    ///
    /// Kept apart from <see cref="Rebuild"/> so typing in the search box redraws the rows under it
    /// and nothing else - rebuilding the whole window on every keystroke would throw away the box
    /// the user is typing in.
    /// </summary>
    private void RebuildDismissed()
    {
        DismissedPanel.Children.Clear();
        if (!DismissedSection.IsVisible || _dismissedIssues is null) return;

        var query = DismissedSearch.Text?.Trim() ?? "";
        var matches = _dismissedNotes.Where(note => MatchesSearch(note, query)).ToList();

        var store = _dismissedIssues;
        IEnumerable<LoadOrderNote> ordered = _dismissedOrder switch
        {
            // Never-dated entries (a file written before timestamps were kept) sort as oldest.
            DismissedOrder.OldestFirst => matches.OrderBy(note => store.RecordedAt(note.StableKey) ?? DateTimeOffset.MinValue),
            DismissedOrder.ByName => matches.OrderBy(note => SplitMessage(note.Message).Header, StringComparer.CurrentCultureIgnoreCase),
            _ => matches.OrderByDescending(note => store.RecordedAt(note.StableKey) ?? DateTimeOffset.MinValue)
        };

        DismissedHeader.Text = query.Length == 0
            ? Texts.GUIIssueDismissedHeader
            : string.Format(Texts.GUIIssueDismissedMatches, matches.Count, _dismissedNotes.Count);

        DismissedNoMatch.IsVisible = query.Length > 0 && matches.Count == 0;
        DismissedNoMatch.Text = string.Format(Texts.GUIIssueDismissedNoMatch, query);

        foreach (var note in ordered)
            DismissedPanel.Children.Add(CreateDismissableRow(note, isDismissed: true));
    }

    /// <summary>
    /// Whether an issue matches what was typed: every word somewhere in its wording, its mods,
    /// the files at stake, or the verdict the user recorded - so "witchy arch" finds the conflict
    /// between Witchy Garden Furniture and the garden-arch sprites without the exact title.
    /// </summary>
    private bool MatchesSearch(LoadOrderNote note, string query)
    {
        if (query.Length == 0) return true;

        var verdict = _dismissedIssues?.Verdict(note.StableKey);
        var haystack = string.Join("\n", new[] { note.Message, verdict is null ? "" : DescribeVerdict(verdict) }
            .Concat(note.Participants.Select(participant => participant.Display))
            .Concat(note.Details));

        return query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(word => haystack.Contains(word, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>
    /// One issue with the checkbox that says "I have looked at this and it is fine".
    ///
    /// A dismissed issue is dimmed and struck through rather than deleted: the user needs to be
    /// able to find it again and change their mind, and a silently vanishing warning is exactly the
    /// behaviour that makes people distrust the report.
    /// </summary>
    private Control CreateDismissableRow(LoadOrderNote note, bool isDismissed)
    {
        var checkbox = new CheckBox
        {
            IsChecked = isDismissed,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Avalonia.Thickness(0, 2, 8, 0),
            MinWidth = 0
        };
        ToolTip.SetTip(checkbox, Texts.GUIIssueDismissTooltip);

        checkbox.IsCheckedChanged += (_, _) =>
        {
            var wanted = checkbox.IsChecked == true;
            if (wanted == isDismissed) return;

            _dismissedIssues?.SetDismissed(note.StableKey, wanted, note.Message);

            // Immediately, not when the window closes: the row in the mod list behind this one is
            // showing a warning triangle for the issue that was just settled.
            DismissalsChanged();

            // The dismissed section is left exactly as the user set it. Ticking a box used to force
            // it open, which on a list with fifty already-judged issues meant one click unfolded a
            // screenful of things the user had deliberately put away - and the row they had just
            // dealt with was somewhere in the middle of it. The count on the toggle going up by one
            // is trace enough of where the issue went.

            // Rebuilding tears down the very checkbox whose event is still being dispatched, so
            // let this event finish first.
            Dispatcher.UIThread.Post(Rebuild);
        };

        Control content = CreateNoteControl(note, isDismissed);

        // When it was marked fine, and why, under the issue itself: the list is sorted by the
        // first, and the second is what the user will want to remember when they find it again.
        if (isDismissed && DescribeDismissal(note) is { Length: > 0 } caption)
        {
            content = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    content,
                    new TextBlock
                    {
                        Text = caption,
                        FontSize = 11,
                        Opacity = 0.8,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Avalonia.Thickness(0, 0, 0, 2)
                    }
                }
            };
        }

        Grid.SetColumn(content, 1);

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Opacity = isDismissed ? 0.55 : 1.0
        };
        row.Children.Add(checkbox);
        row.Children.Add(content);
        return row;
    }

    /// <summary>"Marked fine 10/10/2026 9:42 AM - you chose Witchy Garden Furniture to win."</summary>
    private string DescribeDismissal(LoadOrderNote note)
    {
        var when = _dismissedIssues?.RecordedAt(note.StableKey);
        var verdict = _dismissedIssues?.Verdict(note.StableKey);
        var why = verdict is null ? "" : DescribeVerdict(verdict);

        var stamp = when is null
            ? ""
            : string.Format(Texts.GUIIssueMarkedFineOn,
                when.Value.LocalDateTime.ToString("g", System.Globalization.CultureInfo.CurrentCulture));

        return string.Join(" - ", new[] { stamp, why }.Where(part => part.Length > 0));
    }

    private Control CreateNoteControl(LoadOrderNote note, bool struckThrough = false)
    {
        var decorations = struckThrough ? TextDecorations.Strikethrough : null;
        var (header, detail) = SplitMessage(note.Message);

        // A pile-up of dozens of mods would otherwise be a headline several screens tall.
        header = IssueSummary.Condense(header, note.Participants.Select(p => p.Name).ToList(), Texts.GUIIssueAndMore);

        var body = BuildBody(note, detail);
        if (body is null)
        {
            return new SelectableTextBlock
            {
                Text = $"• {note.Message}",
                TextWrapping = TextWrapping.Wrap,
                TextDecorations = decorations
            };
        }

        return new Expander
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Header = new SelectableTextBlock
            {
                Text = header,
                TextWrapping = TextWrapping.Wrap,
                TextDecorations = decorations
            },
            // Wrapped rather than used directly: an expanded conflict can be seventy-four file
            // paths deep, and the buttons that act on it are above them. Without a way back this
            // is the one place in AIM where reading the detail costs you the controls.
            Content = ScrollEnds.Wrap(new ScrollViewer
            {
                MaxHeight = 320,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = body
            }),
            IsExpanded = false
        };
    }

    /// <summary>
    /// What sits inside an issue's expander: the rest of its message, the mods involved with the
    /// buttons that act on each, the files at stake, and the way out to the research window.
    /// Returns null when there is nothing worth expanding.
    /// </summary>
    private Control? BuildBody(LoadOrderNote note, string detail)
    {
        var panel = new StackPanel { Spacing = 8 };

        // "Find a fix" first, always. It used to sit beside the file list, which put it wherever
        // that list happened to start - below the detail text and one row per mod, so on a
        // three-way conflict it began half a screen down, and on a conflict with no files it fell
        // to the very bottom. It is the one control on an issue that does something about it, so
        // it is the first thing the issue shows when opened.
        var research = CreateResearchRow(note);
        if (research is not null) panel.Children.Add(research);

        // Mods straight under "Find a fix". For a file conflict, winner first.
        var shown = note.Kind == LoadOrderNoteKind.FileConflict
            ? note.Participants.Reverse().ToList()
            : note.Participants.ToList();
        foreach (var participant in shown)
            panel.Children.Add(CreateParticipantRow(note, participant));

        if (!string.IsNullOrWhiteSpace(detail))
        {
            panel.Children.Add(new SelectableTextBlock
            {
                Text = detail,
                TextWrapping = TextWrapping.Wrap
            });
        }

        if (note.Details.Count > 0)
        {
            var paths = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(0, 4, 0, 0) };
            paths.Children.Add(new TextBlock
            {
                Text = Texts.GUIConflictSharedFilesHeader,
                FontWeight = FontWeight.SemiBold,
                Opacity = 0.75
            });

            foreach (var path in note.Details)
            {
                paths.Children.Add(new SelectableTextBlock
                {
                    Text = $"• {path}",
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("monospace")
                });
            }

            panel.Children.Add(paths);
        }

        return panel.Children.Count == 0 ? null : panel;
    }

    // ── One mod inside an issue ──────────────────────────────────────────────────

    /// <summary>
    /// A mod's name, with everything else about it a hover away.
    ///
    /// The path used to be printed inline. With three mods installed under long Steam paths that
    /// turned a one-line shortcut warning into six wrapped lines of directory names, and the only
    /// thing the user needed - which mods - was the hardest part to find.
    /// </summary>
    private Control CreateParticipantRow(LoadOrderNote note, IssueParticipant participant)
    {
        var isCurrentWinner = note.Kind == LoadOrderNoteKind.FileConflict &&
                              ReferenceEquals(participant, note.Participants[^1]);

        var tip = participant.Detail.Length > 0
            ? $"{participant.SourcePath}\n{participant.Detail}"
            : participant.SourcePath;

        var name = CreateParticipantName(participant, tip);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { name }
        };

        if (isCurrentWinner)
        {
            row.Children.Add(new Border
            {
                Background = Brushes.Gray,
                CornerRadius = new Avalonia.CornerRadius(3),
                Padding = new Avalonia.Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = Texts.GUIConflictCurrentWinner, Foreground = Brushes.White }
            });
        }

        var action = note.Kind switch
        {
            LoadOrderNoteKind.FileConflict when !isCurrentWinner => CreateWinnerButton(note, participant),
            LoadOrderNoteKind.HotkeyConflict => CreateRebindButton(note, participant),
            _ => null
        };
        if (action is not null) row.Children.Add(action);

        return row;
    }

    /// <summary>
    /// The mod's name - a link to its Nexus page when AIM knows the page, so the source of an issue
    /// is one click from the issue itself rather than a trip back to the mod list and its menu.
    /// Plain text for a mod AIM cannot place on Nexus.
    /// </summary>
    private Control CreateParticipantName(IssueParticipant participant, string tip)
    {
        var page = _actions?.PageFor?.Invoke(participant);

        if (!ExternalUrl.IsAllowed(page))
        {
            var plain = new TextBlock
            {
                Text = participant.Display,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            ToolTip.SetTip(plain, tip);
            return plain;
        }

        var link = new Button
        {
            Content = new TextBlock
            {
                Text = participant.Display,
                TextWrapping = TextWrapping.Wrap,
                TextDecorations = TextDecorations.Underline
            },
            Foreground = new SolidColorBrush(Color.Parse("#4c8bf5")),
            Background = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            Padding = new Avalonia.Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };

        ToolTip.SetTip(link, $"{string.Format(Texts.GUIIssueOpenModPage, participant.Name)}\n{page}\n{tip}");
        link.Click += (_, _) => ExternalUrl.Open(page);

        return link;
    }

    private Control? CreateWinnerButton(LoadOrderNote note, IssueParticipant participant)
    {
        if (_actions is null) return null;

        var button = new Button { Content = Texts.GUIConflictMakeThisWin };
        ToolTip.SetTip(button, Texts.GUIConflictMakeThisWinTooltip);

        button.Click += (_, _) =>
        {
            if (!_actions.MakeModWin(note, participant)) return;

            // The note's own wording named the old winner, so it has to be rewritten rather than
            // left to contradict the list underneath it. The issue key is built from mod ids and
            // versions, neither of which reordering changes, so any dismissal survives this.
            var reordered = note.Participants.Where(other => !ReferenceEquals(other, participant))
                .Append(participant)
                .ToList();

            var settled = note with
            {
                Message = string.Format(Texts.GUIConflictWinnerNow, participant.Display),
                Participants = reordered
            };

            // Choosing the winner is the decision the issue was asking for, so it is marked fine
            // with that choice as the reason - the same way rebinding a clashing shortcut settles
            // a hotkey issue. Leaving it unticked meant an issue the user had just resolved sat in
            // the open list describing what they had done, waiting for a second click to agree
            // with themselves. The verdict travels with the tick, so the choice is still on record
            // in the marked-fine list and un-ticking it reopens the question.
            _dismissedIssues?.SetDismissed(
                note.StableKey, true, settled.Message,
                new IssueVerdict(DismissedIssueStore.VerdictWinnerChosen, null, participant.Display));

            DismissalsChanged();
            Replace(note, settled);
        };

        return button;
    }

    private Control? CreateRebindButton(LoadOrderNote note, IssueParticipant participant)
    {
        if (_actions is null) return null;

        var capability = _actions.InspectRebind(note, participant);
        var button = new Button
        {
            Content = Texts.GUIHotkeyRebindButton,
            IsEnabled = capability.CanRebind
        };
        ToolTip.SetTip(button, capability.CanRebind
            ? string.Format(Texts.GUIHotkeyRebindTooltip, string.Join(", ", capability.Bindings))
            : DescribeBlocker(capability.Blocker));

        button.Click += async (_, _) =>
        {
            var newKey = await _actions.RebindHotkey(note, participant);
            if (newKey is null) return;

            var remaining = note.Participants.Where(other => !ReferenceEquals(other, participant)).ToList();

            // One mod cannot clash with itself, so moving the second-to-last mod off the key ends
            // the issue. Mark it resolved rather than leaving a warning about a conflict the user
            // has just fixed - that was the whole point of the button.
            if (remaining.Count < 2)
            {
                var solved = note with
                {
                    Message = string.Format(Texts.GUIHotkeyReboundResolved, participant.Display, newKey),
                    Participants = remaining
                };
                _dismissedIssues?.SetDismissed(
                    note.StableKey, true, solved.Message,
                    new IssueVerdict(DismissedIssueStore.VerdictRebound, null, $"{participant.Display} → {newKey}"));

                // As with the checkbox: the dismissed section stays however the user left it, and
                // the mod list hears about it now rather than at closing time.
                DismissalsChanged();
                Replace(note, solved);
                return;
            }

            Replace(note, note with
            {
                Message = string.Format(Texts.GUIHotkeyRebound, participant.Display, newKey, note.HotkeyKey ?? ""),
                Participants = remaining
            });
        };

        return button;
    }

    private static string DescribeBlocker(RebindBlocker blocker) => blocker switch
    {
        RebindBlocker.NotAFolder => Texts.GUIHotkeyBlockedArchive,
        RebindBlocker.NotADeclaredBinding => Texts.GUIHotkeyBlockedNotDeclared,
        RebindBlocker.NoFreeKeys => Texts.GUIHotkeyBlockedNoFreeKeys,
        RebindBlocker.NotWritable => Texts.GUIHotkeyBlockedUnreadable,
        _ => ""
    };

    // ── Research ─────────────────────────────────────────────────────────────────

    private Control? CreateResearchRow(LoadOrderNote note)
    {
        // Only issues about two or more mods have a "do they actually get along" question to
        // answer. A single mod's compatibility warning is about the mod itself.
        if (_actions is null || note.Participants.Count < 2) return null;
        if (note.Kind is not (LoadOrderNoteKind.FileConflict or LoadOrderNoteKind.HookConflict
            or LoadOrderNoteKind.HotkeyConflict)) return null;

        var button = new Button
        {
            Content = Texts.GUIConflictFindAFix,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        ToolTip.SetTip(button, Texts.GUIConflictFindAFixTooltip);

        button.Click += async (_, _) =>
        {
            var verdict = await _actions.Research(this, note);
            if (verdict is null) return;

            var resolved = verdict.Kind is DismissedIssueStore.VerdictNotAnIssue
                or DismissedIssueStore.VerdictPatchExists;

            if (resolved)
            {
                // As with the checkbox: the dismissed section stays however the user left it.
                _dismissedIssues?.SetDismissed(note.StableKey, true, note.Message, verdict);
            }
            else
            {
                // "They really are incompatible" is an answer, not a resolution: the user still has
                // to disable one of them, so the issue stays visible with the finding attached.
                _dismissedIssues?.SetVerdict(note.StableKey, verdict);
            }

            DismissalsChanged();
            Rebuild();
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { button } };

        var recorded = _dismissedIssues?.Verdict(note.StableKey);
        if (recorded is not null)
        {
            var label = new TextBlock
            {
                Text = DescribeVerdict(recorded),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            row.Children.Add(label);

            if (ExternalUrl.IsAllowed(recorded.Link))
            {
                var open = new Button
                {
                    Content = Texts.GUIResearchOpenPatch
                };
                ToolTip.SetTip(open, recorded.Link);
                open.Click += (_, _) => ExternalUrl.Open(recorded.Link);
                row.Children.Add(open);
            }
        }

        return row;
    }

    private static string DescribeVerdict(IssueVerdict verdict) => verdict.Kind switch
    {
        DismissedIssueStore.VerdictNotAnIssue => Texts.GUIVerdictNotAnIssue,
        DismissedIssueStore.VerdictPatchExists => Texts.GUIVerdictPatchExists,
        DismissedIssueStore.VerdictIncompatible => Texts.GUIVerdictIncompatible,
        DismissedIssueStore.VerdictRebound => string.Format(Texts.GUIVerdictRebound, verdict.Note ?? ""),
        DismissedIssueStore.VerdictWinnerChosen => string.Format(Texts.GUIVerdictWinnerChosen, verdict.Note ?? ""),
        _ => ""
    };

    // ── Plumbing ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Swaps one note for an updated copy of itself.
    ///
    /// Matched by identity rather than by <c>IndexOf</c>: <see cref="LoadOrderNote"/> is a record,
    /// so equality is structural, and two notes of the same kind carrying the same message would be
    /// indistinguishable to it - which would silently rewrite the wrong row.
    /// </summary>
    private void Replace(LoadOrderNote original, LoadOrderNote updated)
    {
        var index = _notes.FindIndex(note => ReferenceEquals(note, original));
        if (index < 0) return;

        _notes[index] = updated;
        Dispatcher.UIThread.Post(Rebuild);
    }

    private static (string Header, string Detail) SplitMessage(string message)
    {
        var newLine = message.IndexOfAny(['\r', '\n']);
        if (newLine < 0) return (message, string.Empty);

        var detailStart = newLine;
        while (detailStart < message.Length && (message[detailStart] == '\r' || message[detailStart] == '\n'))
            detailStart++;

        return (message[..newLine], message[detailStart..]);
    }

    /// <summary>
    /// The plain-text version behind "Copy report".
    ///
    /// Dismissed findings stay in it - a report pasted into a bug thread should not quietly omit
    /// things - but they are labelled, so the reader can see which ones the user had already
    /// judged and which are still open.
    /// </summary>
    private static string BuildReport(
        string summary,
        IReadOnlyList<LoadOrderNote> notes,
        DismissedIssueStore? dismissedIssues)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(summary)) lines.Add(summary);

        var marker = LocalizationService.Instance["GUIIssueDismissedMarker"];

        foreach (var note in notes)
        {
            var isDismissed = dismissedIssues?.IsDismissed(note.StableKey) == true;
            lines.Add(isDismissed ? $"{marker} {note.Message}" : note.Message);
            lines.AddRange(note.Participants.Select(participant =>
                $"  - {participant.Display} [{participant.SourcePath}]"));
            lines.AddRange(note.Details.Select(path => $"  • {path}"));
        }

        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }
}
