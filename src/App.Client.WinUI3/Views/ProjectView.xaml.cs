using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using App.Client.WinUI3.Controls;
using App.Client.WinUI3.Models;
using App.Client.WinUI3.Services;
using App.Client.WinUI3.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
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
        _inputHover = QuickInfoHover.Attach(InputBox, GetQuickInfoAsync);

        // the session is set before the view is loaded at startup, when it can't be focused yet
        Loaded += (_, _) => FocusInput();
    }

    private readonly QuickInfoHover _inputHover;

    // The commands of the history are attached to once, however many times their element is recycled.
    private readonly ConditionalWeakTable<RichTextBlock, QuickInfoHover> _commandHovers = [];

    // What a token of a command is, as told by the session the view shows now.
    private Task<QuickInfo?> GetQuickInfoAsync(string text, int position, CancellationToken cancellationToken) =>
        Session?.GetQuickInfoAsync(text, position, cancellationToken) ?? Task.FromResult<QuickInfo?>(null);

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
            view.SetScriptSearchVisible(session.ScriptFilter is not "");
            view.ScrollToEnd();
            view.FocusInput();
            session.PrepareCompletion();
        }
    }

    /// <summary>Raised when the project of the session is to be edited, which the window does, as it shows the dialog for it.</summary>
    public event EventHandler<ProjectSessionViewModel>? EditProjectRequested;

    /// <summary>Raised when the project of the session is to be deleted, which the window confirms first.</summary>
    public event EventHandler<ProjectSessionViewModel>? DeleteProjectRequested;

    private void OnEditProjectClick(object sender, RoutedEventArgs e)
    {
        if (Session is { } session)
        {
            EditProjectRequested?.Invoke(this, session);
        }
    }

    private void OnDeleteProjectClick(object sender, RoutedEventArgs e)
    {
        if (Session is { } session)
        {
            DeleteProjectRequested?.Invoke(this, session);
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

    public static readonly DependencyProperty MaxOutputHeightProperty = DependencyProperty.Register(
        nameof(MaxOutputHeight),
        typeof(double),
        typeof(ProjectView),
        new PropertyMetadata(double.PositiveInfinity, OnMaxOutputHeightChanged));

    /// <summary>How tall the output of a command grows before it is scrolled, following what is written last; infinity for as tall as it is.</summary>
    public double MaxOutputHeight
    {
        get => (double)GetValue(MaxOutputHeightProperty);
        set => SetValue(MaxOutputHeightProperty, value);
    }

    private static void OnMaxOutputHeightChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var view = (ProjectView)sender;

        // the entries laid out already; the others get it when they are prepared
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(view.HistoryRepeater); i++)
        {
            if (VisualTreeHelper.GetChild(view.HistoryRepeater, i) is FrameworkElement element)
            {
                view.ApplyMaxOutputHeight(element);
            }
        }
    }

    /// <summary>Whether the command of a script template, with its variables filled in, is shown to be confirmed before it is run.</summary>
    public bool ConfirmTemplateScripts { get; set; }

    // The settings and the search are out of reach of the template.
    private void OnEntryElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is FrameworkElement element)
        {
            ApplyWrapOutput(element);
            ApplyMaxOutputHeight(element);
            ApplySearch(element, args.Index);
            if (element.FindName("CommandText") is RichTextBlock command)
            {
                _commandHovers.GetValue(command, block => QuickInfoHover.Attach(block, GetQuickInfoAsync));
            }
        }
    }

    /// <summary>What is searched for in the history, highlighted where it is found; nothing while the search is off, or its pattern is invalid.</summary>
    private TextSearch? _search;

    /// <summary>What the lines of the output are filtered by, while only those it is found in are shown.</summary>
    private TextSearch? LineFilter => FilterLinesToggleButton.IsChecked is true ? _search : null;

    private void OnToggleSearchClick(object sender, RoutedEventArgs e)
    {
        var isOn = SearchToggleButton.IsChecked is true;
        SearchPanel.Visibility = isOn ? Visibility.Visible : Visibility.Collapsed;
        if (isOn)
        {
            SearchBox.Focus(FocusState.Programmatic);
            SearchBox.SelectAll();
        }

        OnSearchChanged();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => OnSearchChanged();

    private void OnSearchOptionClick(object sender, RoutedEventArgs e) => OnSearchChanged();

    // escape ends the search, as it does in a browser
    private void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not VirtualKey.Escape)
        {
            return;
        }

        e.Handled = true;
        SearchToggleButton.IsChecked = false;
        SearchPanel.Visibility = Visibility.Collapsed;
        OnSearchChanged();
        FocusInput();
    }

    // The search is made once for all the entries, from what is typed and the options as they are now.
    private void OnSearchChanged()
    {
        var query = SearchToggleButton.IsChecked is true ? SearchBox.Text : null;
        var isRegex = RegexToggleButton.IsChecked is true;
        _search = TextSearch.Create(query, isCaseSensitive: CaseSensitiveToggleButton.IsChecked is true, isRegex);
        SearchBox.Description = isRegex && !string.IsNullOrEmpty(query) && _search is null ? "Invalid regular expression" : null;

        // the entries laid out already; the others get it when they are prepared, so only the output that is shown is searched
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(HistoryRepeater); i++)
        {
            if (VisualTreeHelper.GetChild(HistoryRepeater, i) is FrameworkElement element)
            {
                ApplySearch(element, HistoryRepeater.GetElementIndex(element));
            }
        }

        // the lines found in the output of the others are let go, and searched for again if they are shown
        var lineFilter = LineFilter;
        foreach (var entry in Session?.Entries ?? [])
        {
            if (!Equals(entry.LineFilter, lineFilter))
            {
                entry.FilterLines(null);
            }
        }
    }

    // the entry is found by its index, as the data context isn't set on an element whose template uses x:Bind; one let go to be recycled has none
    private void ApplySearch(FrameworkElement element, int index)
    {
        if (index >= 0 && HistoryRepeater.ItemsSourceView?.GetAt(index) is CommandEntryViewModel entry)
        {
            entry.FilterLines(LineFilter);
        }

        if (element.FindName("CommandText") is RichTextBlock command)
        {
            SearchHighlighting.SetSearch(command, _search);
        }

        if (element.FindName("OutputText") is RichTextBlock output)
        {
            SearchHighlighting.SetSearch(output, _search);
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

    private void ApplyMaxOutputHeight(FrameworkElement element)
    {
        if (element.FindName("OutputScroller") is not ScrollViewer scroller)
        {
            return;
        }

        var isLimited = double.IsFinite(MaxOutputHeight);
        scroller.MaxHeight = MaxOutputHeight;
        scroller.VerticalScrollMode = isLimited ? ScrollMode.Enabled : ScrollMode.Disabled;
        scroller.VerticalScrollBarVisibility = isLimited ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;

        // the entry may have been recycled from one scrolled elsewhere
        ScrollOutputToEnd(scroller);
    }

    // The output follows what is written last, unless it was scrolled up from the end to read what came before.
    private void OnOutputTextSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not FrameworkElement { Parent: ScrollViewer scroller } || !double.IsFinite(MaxOutputHeight))
        {
            return;
        }

        var wasAtEnd = scroller.VerticalOffset >= e.PreviousSize.Height - scroller.ViewportHeight - 1;
        if (wasAtEnd)
        {
            ScrollOutputToEnd(scroller);
        }
    }

    // Deferred, so the scroller is laid out with the output as it is now first.
    private void ScrollOutputToEnd(ScrollViewer scroller) => DispatcherQueue.TryEnqueue(() =>
        scroller.ChangeView(null, scroller.ScrollableHeight, null, disableAnimation: true));

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
        if (Session?.Project is LocalProject { IsWorkingFolderOnWindows: true, ExpandedWorkingFolder: var folder } && Directory.Exists(folder))
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

        if (e.PropertyName is nameof(ProjectSessionViewModel.ScriptSearch))
        {
            ApplyScriptSearchToRealized();
        }
    }

    private void OnToggleScriptSearchClick(object sender, RoutedEventArgs e)
    {
        var isOn = ScriptSearchToggleButton.IsChecked is true;
        SetScriptSearchVisible(isOn);
        if (isOn)
        {
            ScriptSearchBox.Focus(FocusState.Programmatic);
            ScriptSearchBox.SelectAll();
            return;
        }

        // all the scripts are shown again once the search is off
        Session?.ScriptFilter = string.Empty;
    }

    private void SetScriptSearchVisible(bool show)
    {
        ScriptSearchToggleButton.IsChecked = show;
        ScriptSearchBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    // escape ends the search, as it does for the history
    private void OnScriptSearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not VirtualKey.Escape)
        {
            return;
        }

        e.Handled = true;
        SetScriptSearchVisible(false);
        Session?.ScriptFilter = string.Empty;
        FocusInput();
    }

    // the scripts laid out already, in each group; the others get it when they are prepared
    private void ApplyScriptSearchToRealized()
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(ScriptGroupsRepeater); i++)
        {
            if (VisualTreeHelper.GetChild(ScriptGroupsRepeater, i) is not FrameworkElement group || group.FindName("GroupScriptsRepeater") is not ItemsRepeater scripts)
            {
                continue;
            }

            for (var j = 0; j < VisualTreeHelper.GetChildrenCount(scripts); j++)
            {
                if (VisualTreeHelper.GetChild(scripts, j) is FrameworkElement element)
                {
                    ApplyScriptSearch(element);
                }
            }
        }
    }

    private void ApplyScriptSearch(FrameworkElement element)
    {
        var search = Session?.ScriptSearch;
        if (element.FindName("ScriptNameText") is TextBlock name)
        {
            SearchHighlighting.SetSearch(name, search);
        }

        if (element.FindName("ScriptCommandText") is TextBlock command)
        {
            SearchHighlighting.SetSearch(command, search);
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
            // a template is run by the click, once its variables are filled in
            run.Command = sender.ItemsSourceView.GetAt(args.Index) is ScriptItem { Script.IsTemplate: true } ? null : Session?.RunScriptCommand;
        }

        if (args.Element is FrameworkElement element)
        {
            ApplyScriptSearch(element);
        }
    }

    // The command runs the script, and the input is focused to type the next one.
    private async void OnScriptButtonClick(object sender, RoutedEventArgs e)
    {
        if (Session is { } session && (sender as Button)?.CommandParameter is Script { IsTemplate: true } template
            && await ExpandTemplateAsync(template, "Run", CancellationToken.None) is { } script)
        {
            session.RunScriptCommand.Execute(script);
        }

        FocusInput();
    }

    /// <summary>Asks for the values of the variables of <paramref name="script"/> if it is a template, and returns it with them filled in; <see langword="null"/> when that is cancelled.</summary>
    private async Task<Script?> ExpandTemplateAsync(Script script, string primaryButtonText, CancellationToken cancellationToken)
    {
        if (!script.IsTemplate)
        {
            return script;
        }

        var viewModel = new ScriptTemplateViewModel(script, primaryButtonText);
        if (viewModel.Variables is not [])
        {
            var dialog = new ScriptTemplateDialog(viewModel) { XamlRoot = XamlRoot };
            if (await Dialogs.ShowAsync(dialog) is not ContentDialogResult.Primary || cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }

        var expanded = viewModel.Build();
        if (!ConfirmTemplateScripts)
        {
            return expanded;
        }

        return await ConfirmScriptAsync(expanded, primaryButtonText, cancellationToken) ? expanded : null;
    }

    /// <summary>Shows the command of <paramref name="script"/> as it is going to be run, and returns whether it is confirmed.</summary>
    private async Task<bool> ConfirmScriptAsync(Script script, string primaryButtonText, CancellationToken cancellationToken)
    {
        var command = new TextBox
        {
            Header = "Review the command before it is run",
            Text = script.Command,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = (FontFamily)Application.Current.Resources["MonospaceFontFamily"],
            MinWidth = 440,
            MinHeight = 96,
            MaxHeight = 400,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(command, ScrollBarVisibility.Auto);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = script.Name,
            Content = command,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await Dialogs.ShowAsync(dialog) is ContentDialogResult.Primary && !cancellationToken.IsCancellationRequested;
    }

    // opened by the app the file is opened by, to change its scripts there; they are shown as they are once it is saved
    private void OnOpenScriptFileClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ScriptGroupViewModel { FilePath: { } path } || !File.Exists(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception)
        {
            // there is no app to open it by, which Windows asks for itself
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

        if (Session?.Project is LocalProject { IsWorkingFolderOnWindows: true, ExpandedWorkingFolder: var folder } && Directory.Exists(folder))
        {
            picker.SuggestedFolder = folder;
        }

        if (await picker.PickSaveFileAsync() is not { } result)
        {
            return;
        }

        await File.WriteAllTextAsync(result.Path, output, CancellationToken.None);
    }

    private async void OnRunScriptClick(object sender, RoutedEventArgs e)
    {
        if (Session is not { } session || (sender as FrameworkElement)?.Tag is not ScriptItem { Script: var template }
            || await ExpandTemplateAsync(template, "Run", CancellationToken.None) is not { } script)
        {
            return;
        }

        session.RunScriptCommand.Execute(script);
        FocusInput();
    }

    private async void OnEnqueueScriptClick(object sender, RoutedEventArgs e)
    {
        if (Session is not { } session || (sender as FrameworkElement)?.Tag is not ScriptItem { Script: var template }
            || await ExpandTemplateAsync(template, "Queue", CancellationToken.None) is not { } script)
        {
            return;
        }

        session.EnqueueScriptCommand.Execute(script);
        FocusInput();
    }

    private async void OnRunScriptElevatedClick(object sender, RoutedEventArgs e)
    {
        if (Session is not { } session || (sender as FrameworkElement)?.Tag is not ScriptItem { Script: var template }
            || await ExpandTemplateAsync(template, "Run as administrator", CancellationToken.None) is not { } script)
        {
            return;
        }

        session.RunScriptElevatedCommand.Execute(script);
        FocusInput();
    }

    /// <summary>Puts the script into the input without running it, so it can be changed first.</summary>
    private async void OnLoadScriptClick(object sender, RoutedEventArgs e)
    {
        if (Session is not { } session || (sender as FrameworkElement)?.Tag is not ScriptItem { Script: var script })
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(session.Input) && session.Input != script.Command)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
                Title = "Replace input?",
                Content = $"What is typed in the input is replaced by '{script.Name}'.",
                PrimaryButtonText = "Replace",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await Dialogs.ShowAsync(dialog) is not ContentDialogResult.Primary)
            {
                return;
            }
        }

        session.Input = script.Command;
        FocusInput();
        MoveCaretToEnd();
    }

    private void OnRemoveEntryClick(object sender, RoutedEventArgs e)
    {
        if (Session is not { } session || (sender as FrameworkElement)?.Tag is not CommandEntryViewModel entry)
        {
            return;
        }

        session.RemoveEntryCommand.Execute(entry);
    }

    private void OnCancelQueuedClick(object sender, RoutedEventArgs e)
    {
        if (Session is not { } session || (sender as FrameworkElement)?.Tag is not QueuedCommand queued)
        {
            return;
        }

        session.CancelQueuedCommand.Execute(queued);
    }

    private void OnCopyScriptClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ScriptItem { Script: var script })
        {
            return;
        }

        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(script.Command);
        Clipboard.SetContent(package);
    }

    /// <summary>
    /// Saves a copy of the script as a new one, after its name and command can be changed; a script read from a file is saved to the project by the same name,
    /// so it can be changed there.
    /// </summary>
    private async void OnDuplicateScriptClick(object sender, RoutedEventArgs e)
    {
        if (Session is not { } session || (sender as FrameworkElement)?.Tag is not ScriptItem { Script: var script, Group: var group })
        {
            return;
        }

        var viewModel = new ScriptEditorViewModel(script.Command) { Name = group.IsReadOnly ? script.Name : $"{script.Name} (copy)", IsTemplate = script.IsTemplate };
        var dialog = new ScriptDialog(viewModel) { XamlRoot = XamlRoot };
        if (await Dialogs.ShowAsync(dialog) is ContentDialogResult.Primary)
        {
            await session.SaveScriptAsync(dialog.ViewModel.Build(), CancellationToken.None);
        }
    }

    private async void OnEditScriptClick(object sender, RoutedEventArgs e)
    {
        if (Session is not { } session || (sender as FrameworkElement)?.Tag is not ScriptItem { Script: var script })
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
        if (Session is not { } session || (sender as FrameworkElement)?.Tag is not ScriptItem { Script: var script })
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

            // otherwise it moves the focus on, as it does for a project which can't complete
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
    private readonly CancellationSeries _completing = new(CancellationToken.None);

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
        var cancellationToken = _completing.GetNext();
        var input = InputBox.Text;
        var cursor = InputBox.SelectionStart;

        CommandCompletion? completion;
        try
        {
            completion = await session.CompleteAsync(input, cursor, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
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

        // both are shown above the input, so the list takes the place of the quick info
        _inputHover.Close();
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

        // stops at the ends instead of wrapping around
        CompletionList.SelectedIndex = Math.Clamp(CompletionList.SelectedIndex + offset, 0, count - 1);
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

    // Scrolled by whole matches, so none is left cut off at the top; set on the scroll viewer of its template, which doesn't pass the snap points through.
    private void OnCompletionListLoaded(object sender, RoutedEventArgs e)
    {
        if (FindDescendant<ScrollViewer>(CompletionList) is not { } scrollViewer)
        {
            return;
        }

        scrollViewer.VerticalSnapPointsType = SnapPointsType.Mandatory;
        scrollViewer.VerticalSnapPointsAlignment = SnapPointsAlignment.Near;
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if ((child as T ?? FindDescendant<T>(child)) is { } found)
            {
                return found;
            }
        }

        return null;
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

    // Where the character at index starts in the window; at the end of the input, where the last one ends.
    private double GetCharacterLeftInWindow(int index)
    {
        var inputLeft = InputBox.TransformToVisual(null).TransformPoint(default).X;
        var length = InputBox.Text.Length;
        var left = length switch
        {
            0 => InputBox.BorderThickness.Left + InputBox.Padding.Left,
            _ when index < length => InputBox.GetCharacterEdge(index, trailingEdge: false).X,
            _ => InputBox.GetCharacterEdge(length - 1, trailingEdge: true).X,
        };
        return inputLeft + left;
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
