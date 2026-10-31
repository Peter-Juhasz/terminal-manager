using System.Diagnostics;
using App.Client.WinUI3.Models;
using App.Client.WinUI3.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
        }

        if (e.NewValue is ProjectSessionViewModel session)
        {
            session.CommandStarted += view.OnCommandStarted;
            view.ScrollToEnd();
            view.FocusInput();
        }
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

    private void OnToggleScriptsClick(object sender, RoutedEventArgs e)
    {
        var show = ScriptsPane.Visibility is Visibility.Collapsed;
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
    }

    private void OnEnqueueScriptClick(object sender, RoutedEventArgs e)
    {
        if (Session is not { } session || (sender as FrameworkElement)?.Tag is not SavedScript script)
        {
            return;
        }

        session.EnqueueScriptCommand.Execute(script);
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
    /// Enter runs the command, Ctrl+Enter queues it, and Shift+Enter starts a new line. The arrow keys recall the commands run before, from the first and last lines.
    /// Ctrl+C stops the command started last of those running, unless there is text selected to copy.
    /// </summary>
    private void OnInputPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (Session is not { } session)
        {
            return;
        }

        switch (e.Key)
        {
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
        }
    }

    private void MoveCaretToEnd() => DispatcherQueue.TryEnqueue(() => InputBox.Select(InputBox.Text.Length, 0));

    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
