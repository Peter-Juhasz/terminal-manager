using System.ComponentModel;
using System.Diagnostics;
using App.Client.WinUI3.Models;
using App.Client.WinUI3.Services;
using App.Client.WinUI3.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Core;

namespace App.Client.WinUI3.Views;

/// <summary>
/// The commands run in a project, and where the next one is typed.
/// </summary>
public sealed partial class ProjectView : UserControl
{
    public ProjectView()
    {
        InitializeComponent();

        // the session is set before the view is loaded at startup, when it can't be focused yet
        Loaded += (_, _) => FocusInput();
    }

    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session),
        typeof(ProjectSessionViewModel),
        typeof(ProjectView),
        new PropertyMetadata(null, OnSessionChanged));

    public ProjectSessionViewModel? Session
    {
        get => (ProjectSessionViewModel?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    private static void OnSessionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var view = (ProjectView)sender;
        if (e.OldValue is ProjectSessionViewModel previous)
        {
            previous.CommandStarted -= view.OnCommandStarted;
            previous.PropertyChanged -= view.OnSessionPropertyChanged;
            view.CloseCompletionList();
            view._cycling = null;
        }

        if (e.NewValue is ProjectSessionViewModel session)
        {
            session.CommandStarted += view.OnCommandStarted;
            session.PropertyChanged += view.OnSessionPropertyChanged;

            // the pane is out of the way until there is a script to show in it
            view.SetScriptsPaneVisible(session.HasScripts);
            view.ScrollToEnd();
            view.FocusInput();
            session.PrepareCompletion();
        }
    }

    public static readonly DependencyProperty WrapOutputProperty = DependencyProperty.Register(
        nameof(WrapOutput),
        typeof(bool),
        typeof(ProjectView),
        new PropertyMetadata(true, OnWrapOutputChanged));

    /// <summary>Whether the long lines a command writes are wrapped, rather than scrolled horizontally.</summary>
    public bool WrapOutput
    {
        get => (bool)GetValue(WrapOutputProperty);
        set => SetValue(WrapOutputProperty, value);
    }

    private static void OnWrapOutputChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var view = (ProjectView)sender;

        // the entries laid out already; the others get it when they are prepared
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(view.HistoryRepeater); i++)
        {
            if (VisualTreeHelper.GetChild(view.HistoryRepeater, i) is FrameworkElement element)
            {
                view.ApplyWrapOutput(element);
            }
        }
    }

    // The setting is out of reach of the template.
    private void OnEntryElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is FrameworkElement element)
        {
            ApplyWrapOutput(element);
        }
    }

    private void ApplyWrapOutput(FrameworkElement element)
    {
        if (element.FindName("OutputScroller") is not ScrollViewer scroller || element.FindName("OutputText") is not RichTextBlock text)
        {
            return;
        }

        text.TextWrapping = WrapOutput ? TextWrapping.Wrap : TextWrapping.NoWrap;
        scroller.HorizontalScrollMode = WrapOutput ? ScrollMode.Disabled : ScrollMode.Enabled;
        scroller.HorizontalScrollBarVisibility = WrapOutput ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
    }

    private void OnCommandStarted(object? sender, CommandEntryViewModel e) => ScrollToEnd();

    // Deferred, so the history is laid out with what was just added to it first.
    private void ScrollToEnd() => DispatcherQueue.TryEnqueue(() =>
    {
        HistoryScroller.UpdateLayout();
        HistoryScroller.ChangeView(null, HistoryScroller.ScrollableHeight, null, disableAnimation: true);
    });

    // Deferred, as whatever changed the session, such as the navigation menu, may still be taking focus.
    private void FocusInput() => DispatcherQueue.TryEnqueue(() => InputBox.Focus(FocusState.Programmatic));

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        if (Session?.Project.ExpandedWorkingFolder is { } folder && Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose();
        }
    }

    // The first script saved, such as from the history, is shown where it can be run.
    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProjectSessionViewModel.HasScripts) && Session is { HasScripts: true })
        {
            SetScriptsPaneVisible(true);
        }

        // the working folder may have changed
        if (e.PropertyName is nameof(ProjectSessionViewModel.Project) && Session is { } session)
        {
            session.PrepareCompletion();
        }
    }

    private void OnToggleScriptsClick(object sender, RoutedEventArgs e) =>
        SetScriptsPaneVisible(ScriptsPane.Visibility is Visibility.Collapsed);

    private void SetScriptsPaneVisible(bool show)
    {
        ScriptsPane.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ScriptsToggleIcon.Glyph = show ? "\uE89F" : "\uE8A0";

        var label = show ? "Hide scripts" : "Show scripts";
        AutomationProperties.SetName(ScriptsToggleButton, label);
        ToolTipService.SetToolTip(ScriptsToggleButton, label);
    }

    // The run button of a script gets the command of the session, which is out of reach of the template.
    private void OnScriptElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is Panel { Children: [Button run, ..] })
        {
            run.Command = Session?.RunScriptCommand;
        }
    }

    // The command runs the script, and the input is focused to type the next one.
    private void OnScriptButtonClick(object sender, RoutedEventArgs e) => FocusInput();

    private void OnScriptMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Parent: FrameworkElement { ContextFlyout: { } flyout } })
        {
            flyout.ShowAt((FrameworkElement)sender);
        }
    }

    private async void OnAddScriptClick(object sender, RoutedEventArgs e)
    {
        if (Session is not { } session)
        {
            return;
        }

        var dialog = new ScriptDialog(new ScriptEditorViewModel(session.Input.Trim())) { XamlRoot = XamlRoot };
        if (await Dialogs.ShowAsync(dialog) is ContentDialogResult.Primary)
        {
            await session.SaveScriptAsync(dialog.ViewModel.Build(), CancellationToken.None);
        }
    }

    private async void OnSaveAsScriptClick(object sender, RoutedEventArgs e)
    {
        if (Session is not { } session || (sender as FrameworkElement)?.Tag is not CommandEntryViewModel entry)
        {
            return;
        }

        var dialog = new ScriptDialog(new ScriptEditorViewModel(entry.Command)) { XamlRoot = XamlRoot };
        if (await Dialogs.ShowAsync(dialog) is ContentDialogResult.Primary)
        {
            await session.SaveScriptAsync(dialog.ViewModel.Build(), CancellationToken.None);
        }
    }

    // What it wrote so far, if it is still running.
    private async void OnSaveOutputClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not CommandEntryViewModel entry)
        {
            return;
        }

        var output = entry.Output.ToString();
        var picker = new FileSavePicker(XamlRoot.ContentIslandEnvironment.AppWindowId)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = "output",
            DefaultFileExtension = ".txt",
        };
        picker.FileTypeChoices.Add("Text", [".txt"]);
        picker.FileTypeChoices.Add("Log", [".log"]);

        if (Session?.Project.ExpandedWorkingFolder is { } folder && Directory.Exists(folder))
        {
            picker.SuggestedFolder = folder;
        }

        if (await picker.PickSaveFileAsync() is not { } result)
        {
            return;
        }

        await File.WriteAllTextAsync(result.Path, output, CancellationToken.None);
    }

    private void OnRunScriptClick(object sender, RoutedEventArgs e)
    {
        if (Session is not { } session || (sender as FrameworkElement)?.Tag is not SavedScript script)
        {
            return;
        }

        session.RunScriptCommand.Execute(script);
        FocusInput();
    }

    private void OnEnqueueScriptClick(object sender, RoutedEventArgs e)
    {
        if (Session is not { } session || (sender as FrameworkElement)?.Tag is not SavedScript script)
        {
            return;
        }

        session.EnqueueScriptCommand.Execute(script);
        FocusInput();
    }

    private void OnRunScriptElevatedClick(object sender, RoutedEventArgs e)
    {
        if (Session is not { } session || (sender as FrameworkElement)?.Tag is not SavedScript script)
        {
            return;
        }

        session.RunScriptElevatedCommand.Execute(script);
        FocusInput();
    }

    private void OnCancelQueuedClick(object sender, RoutedEventArgs e)
    {
        if (Session is not { } session || (sender as FrameworkElement)?.Tag is not QueuedCommand queued)
        {
            return;
        }

        session.CancelQueuedCommand.Execute(queued);
    }

    private async void OnEditScriptClick(object sender, RoutedEventArgs e)
    {
        if (Session is not { } session || (sender as FrameworkElement)?.Tag is not SavedScript script)
        {
            return;
        }

        var dialog = new ScriptDialog(new ScriptEditorViewModel(script)) { XamlRoot = XamlRoot };
        if (await Dialogs.ShowAsync(dialog) is ContentDialogResult.Primary)
        {
            await session.SaveScriptAsync(dialog.ViewModel.Build(), CancellationToken.None);
        }
    }

    private async void OnDeleteScriptClick(object sender, RoutedEventArgs e)
    {
        if (Session is not { } session || (sender as FrameworkElement)?.Tag is not SavedScript script)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = "Delete script?",
            Content = $"'{script.Name}' is removed from the project.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await Dialogs.ShowAsync(dialog) is ContentDialogResult.Primary)
        {
            await session.DeleteScriptAsync(script, CancellationToken.None);
        }
    }

    /// <summary>
    /// Enter runs the command, Ctrl+Enter queues it, Ctrl+Shift+Enter runs it as an administrator, and Shift+Enter starts a new line.
    /// The arrow keys recall the commands run before, from the first and last lines.
    /// Ctrl+C stops the command started last of those running, unless there is text selected to copy.
    /// Tab completes the text before the caret, and goes on to the next match when pressed again, Shift+Tab to the one before.
    /// Ctrl+Space lists the matches, which the arrow keys move through, Enter or Tab takes, and Escape closes; typing -, $ or : lists them too.
    /// </summary>
    private void OnInputPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (Session is not { } session)
        {
            return;
        }

        if (CompletionPopup.IsOpen && HandleCompletionListKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Enter when IsKeyDown(VirtualKey.Control) && IsKeyDown(VirtualKey.Shift):
                e.Handled = true;
                if (session.RunElevatedCommand.CanExecute(null))
                {
                    session.RunElevatedCommand.Execute(null);
                }

                break;

            case VirtualKey.Enter when IsKeyDown(VirtualKey.Control) && !IsKeyDown(VirtualKey.Shift):
                e.Handled = true;
                if (session.EnqueueCommand.CanExecute(null))
                {
                    session.EnqueueCommand.Execute(null);
                }

                break;

            case VirtualKey.Enter when !IsKeyDown(VirtualKey.Shift):
                e.Handled = true;
                if (session.RunCommand.CanExecute(null))
                {
                    session.RunCommand.Execute(null);
                }

                break;

            case VirtualKey.Up when !InputBox.Text[..InputBox.SelectionStart].Contains('\r', StringComparison.Ordinal):
                e.Handled = session.RecallPrevious();
                if (e.Handled)
                {
                    MoveCaretToEnd();
                }

                break;

            case VirtualKey.Down when !InputBox.Text[(InputBox.SelectionStart + InputBox.SelectionLength)..].Contains('\r', StringComparison.Ordinal):
                e.Handled = session.RecallNext();
                if (e.Handled)
                {
                    MoveCaretToEnd();
                }

                break;

            case VirtualKey.C when IsKeyDown(VirtualKey.Control) && InputBox.SelectionLength == 0:
                e.Handled = session.StopLatest();
                break;

            // otherwise it moves the focus on, as it does for the Command Prompt, which can't complete
            case VirtualKey.Tab when session.CanComplete && !IsKeyDown(VirtualKey.Control):
                e.Handled = true;
                _ = CycleCompletionAsync(session, isBackward: IsKeyDown(VirtualKey.Shift));
                break;

            case VirtualKey.Space when session.CanComplete && IsKeyDown(VirtualKey.Control):
                e.Handled = true;
                _ = ListCompletionAsync(session);
                break;
        }
    }

    // The completion that Tab goes through the matches of, while the input is left as the last one put in.
    private InputCompletion? _cycling;

    // The completion whose matches are listed, for the input as it is.
    private InputCompletion? _listed;

    // The completion being asked for, which is canceled when it is asked for again.
    private CancellationTokenSource? _completing;

    // Whether a match is being put in the input, so the list isn't refreshed for it.
    private bool _isPutting;

    // How far the text of a match is from the left of the list, after its icon, so the list is moved left by as much for it to line up with the text it completes.
    // Measured each time the list is laid out, as it depends on the padding of the items in the theme; until the first, about as far.
    private double _completionTextIndent = 40;

    // Moves through the matches listed, or takes the one selected, or closes the list; the other keys are left to the input, which refreshes the list.
    private bool HandleCompletionListKey(VirtualKey key)
    {
        switch (key)
        {
            case VirtualKey.Up:
                MoveCompletionSelection(-1);
                return true;

            case VirtualKey.Down:
                MoveCompletionSelection(1);
                return true;

            case VirtualKey.Enter or VirtualKey.Tab:
                TakeListedCompletion(CompletionList.SelectedIndex);
                return true;

            case VirtualKey.Escape:
                CloseCompletionList();
                return true;

            default:
                return false;
        }
    }

    private async Task CycleCompletionAsync(ProjectSessionViewModel session, bool isBackward)
    {
        if (_cycling is { } current && current.Text == InputBox.Text)
        {
            var count = current.Completion.Matches.Count;
            PutCompletion(current with { Match = (current.Match + (isBackward ? count - 1 : 1)) % count });
            return;
        }

        _cycling = null;
        if (await RequestCompletionAsync(session) is not { Completion.Matches.Count: > 0 } requested)
        {
            return;
        }

        PutCompletion(requested with { Match = isBackward ? requested.Completion.Matches.Count - 1 : 0 });
    }

    // When it is listed for a character typed, a single match is listed too, as what is typed on may not be it.
    private async Task ListCompletionAsync(ProjectSessionViewModel session, bool isTyped = false)
    {
        _cycling = null;
        if (await RequestCompletionAsync(session) is not { } requested)
        {
            return;
        }

        switch (requested.Completion.Matches.Count)
        {
            case 0:
                CloseCompletionList();
                break;

            // there is nothing to choose from, so it is put in right away, as PSReadLine does
            case 1 when !isTyped:
                CloseCompletionList();
                PutCompletion(requested);
                break;

            default:
                ShowCompletionList(requested);
                break;
        }
    }

    // The list is kept to the matches of the input as it is typed, until there are none.
    private async Task RefreshCompletionListAsync(ProjectSessionViewModel session)
    {
        if (await RequestCompletionAsync(session) is not { } requested || !CompletionPopup.IsOpen)
        {
            return;
        }

        if (requested.Completion.Matches.Count == 0)
        {
            CloseCompletionList();
            return;
        }

        ShowCompletionList(requested);
    }

    // Asks for the completion of the input as it is, with no matches when it can't be completed,
    // or returns null when it was asked for again, or the input was changed, or another project selected, while waiting for it.
    private async Task<InputCompletion?> RequestCompletionAsync(ProjectSessionViewModel session)
    {
        _completing?.Cancel();
        using var completing = _completing = new CancellationTokenSource();
        var input = InputBox.Text;
        var cursor = InputBox.SelectionStart;

        CommandCompletion? completion;
        try
        {
            completion = await session.CompleteAsync(input, cursor, completing.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            if (_completing == completing)
            {
                _completing = null;
            }
        }

        if (InputBox.Text != input || InputBox.SelectionStart != cursor || Session != session)
        {
            return null;
        }

        return new InputCompletion(Input: input, Completion: completion ?? new(ReplacementIndex: cursor, ReplacementLength: 0, Matches: []), Match: 0);
    }

    // Puts the match in the input, which Tab then goes on from.
    private void PutCompletion(InputCompletion completion)
    {
        _cycling = completion;
        _isPutting = true;
        try
        {
            InputBox.Text = completion.Text;
            InputBox.Select(completion.Completion.GetCaret(completion.Match), 0);
        }
        finally
        {
            _isPutting = false;
        }
    }

    private void ShowCompletionList(InputCompletion completion)
    {
        _listed = completion;
        CompletionList.ItemsSource = completion.Completion.Matches;
        CompletionList.SelectedIndex = 0;

        // hidden until it is placed, as its height and the room before the text of its items are only known once it is laid out
        if (!CompletionPopup.IsOpen)
        {
            CompletionPopup.Child.Opacity = 0;
            CompletionPopup.IsOpen = true;
        }

        DispatcherQueue.TryEnqueue(PlaceCompletionList);
    }

    // Above the input, with the text of the matches lined up with the text completed, as far as it fits in the window.
    // Both are measured where they are in the window, so it doesn't matter what the offsets of the popup are relative to.
    private void PlaceCompletionList()
    {
        if (!CompletionPopup.IsOpen || _listed is not { } listed || CompletionPopup.Child is not FrameworkElement list)
        {
            return;
        }

        // the matches may have just been replaced
        list.UpdateLayout();
        if (CompletionList.ContainerFromIndex(0) is ListViewItem { ContentTemplateRoot: StackPanel { Children: [_, TextBlock text, ..] } })
        {
            _completionTextIndent = text.TransformToVisual(list).TransformPoint(default).X;
        }

        var origin = CompletionPopup.TransformToVisual(null).TransformPoint(default);
        var left = GetCharacterLeftInWindow(listed.Completion.ReplacementIndex) - _completionTextIndent;
        var windowWidth = XamlRoot.Size.Width;
        CompletionPopup.HorizontalOffset = Math.Max(0, Math.Min(left, windowWidth - list.ActualWidth)) - origin.X;
        CompletionPopup.VerticalOffset = InputBox.TransformToVisual(null).TransformPoint(default).Y - list.ActualHeight - 4 - origin.Y;
        list.Opacity = 1;

        // what the match selected is is shown beside where the list is now
        if (CompletionList.SelectedItem is CompletionMatch selected)
        {
            ShowCompletionInfo(selected);
        }
    }

    private void CloseCompletionList()
    {
        _listed = null;
        CompletionInfoPopup.IsOpen = false;
        CompletionPopup.IsOpen = false;
        CompletionList.ItemsSource = null;
    }

    private void MoveCompletionSelection(int offset)
    {
        var count = CompletionList.Items.Count;
        if (count == 0)
        {
            return;
        }

        CompletionList.SelectedIndex = (CompletionList.SelectedIndex + offset + count) % count;
    }

    private void TakeListedCompletion(int match)
    {
        // the list is still being refreshed for what was typed last, which it is taken from once it is
        if (_listed is not { } listed || listed.Input != InputBox.Text || match < 0)
        {
            return;
        }

        CloseCompletionList();
        PutCompletion(listed with { Match = match });
    }

    private void OnCompletionItemClick(object sender, ItemClickEventArgs e)
    {
        if (_listed is not { } listed || e.ClickedItem is not CompletionMatch clicked)
        {
            return;
        }

        TakeListedCompletion(listed.Completion.Matches.IndexOf(clicked));
    }

    private void OnCompletionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CompletionList.SelectedItem is not CompletionMatch selected)
        {
            CompletionInfoPopup.IsOpen = false;
            return;
        }

        CompletionList.ScrollIntoView(selected);

        // deferred, as the item scrolled into view, or listed for the first time, is only laid out after this
        DispatcherQueue.TryEnqueue(() => ShowCompletionInfo(selected));
    }

    // Shows what the match selected is beside it, unless it only repeats the match, which isn't worth the room.
    private void ShowCompletionInfo(CompletionMatch match)
    {
        CompletionInfoPopup.IsOpen = false;
        if (!CompletionPopup.IsOpen
            || !ReferenceEquals(CompletionList.SelectedItem, match)
            || match?.ToolTip?.Trim() is not { Length: > 0 } toolTip
            || toolTip == match.ListItemText
            || CompletionList.ContainerFromItem(match) is not FrameworkElement container)
        {
            return;
        }

        CompletionToolTip.Text = toolTip;
        CompletionInfoPopup.PlacementTarget = container;
        CompletionInfoPopup.IsOpen = true;
    }

    // The input as it was when the caret last moved, to tell a character typed from other changes.
    private string _lastInput = "";

    private void OnInputSelectionChanged(object sender, RoutedEventArgs e)
    {
        var lastInput = _lastInput;
        _lastInput = InputBox.Text;
        if (_isPutting || Session is not { } session)
        {
            return;
        }

        if (CompletionPopup.IsOpen)
        {
            _ = RefreshCompletionListAsync(session);
            return;
        }

        // a parameter, a variable, or a drive, scope or static member is listed as soon as it is started, as Visual Studio does
        if (session.CanComplete && IsCompletionTriggerTyped(lastInput))
        {
            _ = ListCompletionAsync(session, isTyped: true);
        }
    }

    // Whether the input is the one before with only a character that starts a completion typed before the caret.
    private bool IsCompletionTriggerTyped(string lastInput)
    {
        var input = InputBox.Text;
        var caret = InputBox.SelectionStart;
        return InputBox.SelectionLength == 0
            && caret > 0
            && input.Length == lastInput.Length + 1
            && input[caret - 1] is '-' or '$' or ':'
            && string.CompareOrdinal(input, 0, lastInput, 0, caret - 1) == 0
            && string.CompareOrdinal(input, caret, lastInput, caret - 1, lastInput.Length - caret + 1) == 0;
    }

    // The list is never focused, so the focus leaving the input means it is done with.
    private void OnInputLostFocus(object sender, RoutedEventArgs e) => CloseCompletionList();

    // Where the character at index starts, from the left of the input; at the end of the input, where the last one ends.
    // Where the character at index starts in the window; at the end of the input, where the last one ends.
    private double GetCharacterLeftInWindow(int index)
    {
        var inputLeft = InputBox.TransformToVisual(null).TransformPoint(default).X;
        var textLeft = InputBox.BorderThickness.Left + InputBox.Padding.Left;
        var length = InputBox.Text.Length;
        if (length == 0)
        {
            return inputLeft + textLeft;
        }

        var left = index < length
            ? InputBox.GetRectFromCharacterIndex(index, trailingEdge: false).X
            : InputBox.GetRectFromCharacterIndex(length - 1, trailingEdge: true).X;

        // the rectangles are relative to where the text starts, inside the border and the padding, when the first character isn't as far in as that
        var isRelativeToText = InputBox.GetRectFromCharacterIndex(0, trailingEdge: false).X < textLeft / 2;
        return inputLeft + left + (isRelativeToText ? textLeft : 0);
    }

    // The input as it was when the completion was asked for, with the match at Match put in.
    private sealed record InputCompletion(string Input, CommandCompletion Completion, int Match)
    {
        public string Text => Completion.Apply(Input, Match);
    }

    private void MoveCaretToEnd() => DispatcherQueue.TryEnqueue(() => InputBox.Select(InputBox.Text.Length, 0));

    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}