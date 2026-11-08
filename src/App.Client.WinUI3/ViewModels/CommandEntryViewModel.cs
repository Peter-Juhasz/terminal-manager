using System.Globalization;
using App.Client.WinUI3.Models;
using App.Client.WinUI3.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.ApplicationModel.DataTransfer;

namespace App.Client.WinUI3.ViewModels;

/// <summary>
/// A command that was run, as an administrator if <paramref name="isElevated"/>, with what it wrote, and how it ended.
/// </summary>
public sealed partial class CommandEntryViewModel(string command, bool isElevated, Action stop, Func<Task> rerun) : ObservableObject
{
    public string Command { get; } = command;

    /// <summary>The command, with what it is made of colored, once it was parsed; until then, or when it can't be parsed, it is shown as it is.</summary>
    [ObservableProperty]
    public partial HighlightedCode HighlightedCommand { get; set; } = HighlightedCode.Plain(command);

    /// <summary>Whether it was run as an administrator, as it is when it is run again.</summary>
    public bool IsElevated { get; } = isElevated;

    /// <summary>When the command was started.</summary>
    public DateTimeOffset StartedAt { get; private init; } = DateTimeOffset.Now;

    /// <summary>Shows a command run before the app was started, as it ended, with its output collapsed; it can only be run again.</summary>
    public static CommandEntryViewModel Restore(HistoryEntry saved, Func<Task> rerun)
    {
        ArgumentNullException.ThrowIfNull(saved);

        var entry = new CommandEntryViewModel(saved.Command, saved.IsElevated, static () => { }, rerun)
        {
            StartedAt = saved.StartedAt,
            IsPinned = saved.IsPinned,
            IsExpanded = false,
        };
        entry.AppendOutput(saved.Output ?? []);
        entry.Complete(saved.ExitCode, saved.WasStopped, saved.Duration);
        return entry;
    }

    /// <summary>The command as it is saved, once it has ended.</summary>
    public HistoryEntry ToHistoryEntry() => new(
        Command: Command,
        IsElevated: IsElevated,
        StartedAt: StartedAt,
        Output: Output.IsEmpty ? null : Output.Chunks.ToArray(),
        ExitCode: ExitCode,
        WasStopped: WasStopped,
        Duration: Duration,
        IsPinned: IsPinned);

    public string StartedAtText => StartedAt.ToLocalTime().ToString("T", CultureInfo.CurrentCulture);

    public string StartedAtToolTip => StartedAt.ToLocalTime().ToString("F", CultureInfo.CurrentCulture);

    /// <summary>What the command wrote, in the order it was written.</summary>
    public CommandOutput Output { get; } = new();

    public bool HasOutput => !Output.IsEmpty;

    // the lines of the output shown while they are filtered
    private OutputLineFilter? _lineFilter;

    /// <summary>What is searched for in the lines of the output, while only those it is found in are shown.</summary>
    public TextSearch? LineFilter => _lineFilter?.Search;

    /// <summary>The output as it is shown: all of it, or the lines <see cref="LineFilter"/> is found in.</summary>
    public CommandOutput DisplayedOutput => _lineFilter?.Output ?? Output;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOutputVisible), nameof(IsNoMatchingLinesVisible), nameof(ExpandGlyph), nameof(ExpandToolTip))]
    public partial bool IsExpanded { get; set; } = true;

    public bool IsOutputVisible => HasOutput && IsExpanded && !DisplayedOutput.IsEmpty;

    /// <summary>Whether the output is filtered, and none of its lines are shown, so it says so instead.</summary>
    public bool IsNoMatchingLinesVisible => HasOutput && IsExpanded && _lineFilter is { Output.IsEmpty: true };

    /// <summary>
    /// Shows only the lines of the output <paramref name="search"/> is found in, or all of it when it is null. The lines written later are searched
    /// as they are written; the output is searched again only when what is searched for changes.
    /// </summary>
    public void FilterLines(TextSearch? search)
    {
        if (Equals(search, LineFilter))
        {
            return;
        }

        if (search is null)
        {
            _lineFilter = null;
        }
        else
        {
            _lineFilter = new(search);
            _lineFilter.Update(Output.Chunks);
            if (IsCompleted)
            {
                _lineFilter.Complete();
            }
        }

        OnPropertyChanged(nameof(LineFilter));
        OnPropertyChanged(nameof(DisplayedOutput));
        OnDisplayedOutputChanged();
    }

    private void OnDisplayedOutputChanged()
    {
        OnPropertyChanged(nameof(IsOutputVisible));
        OnPropertyChanged(nameof(IsNoMatchingLinesVisible));
    }

    public string ExpandGlyph => IsExpanded ? "\uE70E" : "\uE70D";

    public string ExpandToolTip => IsExpanded ? "Collapse output" : "Expand output";

    /// <summary>Whether it is kept when the history is cleared of those that aren't.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotPinned), nameof(PinToolTip))]
    public partial bool IsPinned { get; set; }

    public bool IsNotPinned => !IsPinned;

    public string PinToolTip => IsPinned ? "Unpin" : "Pin";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCompleted), nameof(IsSucceeded), nameof(IsFailed), nameof(StatusText), nameof(IsProgressVisible), nameof(DurationText))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    public partial bool IsRunning { get; private set; } = true;

    /// <summary>The progress the command last reported on its standard output, the way it would be shown on the taskbar by Windows Terminal.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProgressVisible), nameof(IsProgressIndeterminate), nameof(IsProgressError), nameof(IsProgressPaused), nameof(StatusText))]
    public partial ProgressState ProgressState { get; private set; }

    /// <summary>The percent of the progress.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial double ProgressValue { get; private set; }

    public bool IsProgressVisible => IsRunning && ProgressState is not ProgressState.None;

    public bool IsProgressIndeterminate => ProgressState is ProgressState.Indeterminate;

    public bool IsProgressError => ProgressState is ProgressState.Error;

    public bool IsProgressPaused => ProgressState is ProgressState.Paused;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSucceeded), nameof(IsFailed), nameof(StatusText))]
    public partial int? ExitCode { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSucceeded), nameof(IsFailed), nameof(StatusText))]
    public partial bool WasStopped { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    public partial TimeSpan Duration { get; set; }

    public bool IsCompleted => !IsRunning;

    public bool IsSucceeded => IsCompleted && !WasStopped && ExitCode == 0;

    public bool IsFailed => IsCompleted && !IsSucceeded;

    public string StatusText => this switch
    {
        { IsRunning: true, ProgressState: ProgressState.Normal or ProgressState.Error or ProgressState.Paused } =>
            string.Create(CultureInfo.CurrentCulture, $"Running… {ProgressValue:0}%"),
        { IsRunning: true } => "Running…",
        { WasStopped: true } => "Stopped",
        { ExitCode: { } exitCode } => string.Create(CultureInfo.CurrentCulture, $"Exit code {exitCode}"),
        _ => "Couldn't start",
    };

    /// <summary>The time spent so far in whole seconds, as it is updated once a second, and exactly once the command ended.</summary>
    public string DurationText => FormatDuration(Duration, IsRunning);

    /// <summary>Shows what the command wrote since the last batch, and the progress it reported so far.</summary>
    public void Append(OutputBatch batch)
    {
        AppendOutput(batch.Chunks);
        ProgressValue = batch.Progress.Value ?? 0;
        ProgressState = batch.Progress.State;
    }

    /// <summary>Shows <paramref name="chunk"/> as if the command wrote it, like the reason it couldn't be run.</summary>
    public void Append(OutputChunk chunk) => AppendOutput((OutputChunk[])[chunk]);

    private void AppendOutput(IReadOnlyList<OutputChunk> chunks)
    {
        var hadOutput = HasOutput;
        var hadDisplayedOutput = !DisplayedOutput.IsEmpty;
        Output.Append(chunks);
        _lineFilter?.Update(Output.Chunks);
        if (!hadOutput && HasOutput)
        {
            OnPropertyChanged(nameof(HasOutput));
        }

        if (hadOutput != HasOutput || hadDisplayedOutput != !DisplayedOutput.IsEmpty)
        {
            OnDisplayedOutputChanged();
        }
    }

    public void Complete(int? exitCode, bool wasStopped, TimeSpan duration)
    {
        Duration = duration;
        ExitCode = exitCode;
        WasStopped = wasStopped;
        IsRunning = false;
        Output.Complete();

        // the last line ended with the output
        if (_lineFilter is { } filter)
        {
            var hadDisplayedOutput = !filter.Output.IsEmpty;
            filter.Complete();
            if (hadDisplayedOutput != !filter.Output.IsEmpty)
            {
                OnDisplayedOutputChanged();
            }
        }
    }

    /// <summary>Stops the command, and all the processes it started, if it is still running.</summary>
    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Stop()
    {
        if (IsRunning)
        {
            stop();
        }
    }

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void TogglePinned() => IsPinned = !IsPinned;

    [RelayCommand]
    private void Copy() => SetClipboardText(Output.ToString());

    [RelayCommand]
    private void CopyInput() => SetClipboardText(Command);

    /// <summary>Runs the command again, as a new entry; this one is left as it is.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task RerunAsync() => rerun();

    private static void SetClipboardText(string text)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private static string FormatDuration(TimeSpan duration, bool isRunning) => duration switch
    {
        { TotalMinutes: < 1 } when isRunning => string.Create(CultureInfo.CurrentCulture, $"{duration.Seconds} s"),
        { TotalSeconds: < 1 } => string.Create(CultureInfo.CurrentCulture, $"{duration.TotalMilliseconds:0} ms"),
        { TotalMinutes: < 1 } => string.Create(CultureInfo.CurrentCulture, $"{duration.TotalSeconds:0.00} s"),
        { TotalHours: < 1 } => string.Create(CultureInfo.CurrentCulture, $"{duration.Minutes} min {duration.Seconds} s"),
        _ => string.Create(CultureInfo.CurrentCulture, $"{(int)duration.TotalHours} h {duration.Minutes} min {duration.Seconds} s"),
    };
}
