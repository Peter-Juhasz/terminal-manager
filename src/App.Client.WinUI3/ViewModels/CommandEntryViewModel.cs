using System.Collections.ObjectModel;
using System.Globalization;
using App.Client.WinUI3.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.ApplicationModel.DataTransfer;

namespace App.Client.WinUI3.ViewModels;

/// <summary>
/// A command that was run, with what it wrote, and how it ended.
/// </summary>
public sealed partial class CommandEntryViewModel(string command, Action stop) : ObservableObject
{
    public string Command { get; } = command;

    /// <summary>What the command wrote, in the order it was written, with consecutive writes to the same stream joined.</summary>
    public ObservableCollection<OutputSegment> Segments { get; } = [];

    public bool HasOutput => Segments.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOutputVisible), nameof(ExpandGlyph), nameof(ExpandToolTip))]
    public partial bool IsExpanded { get; set; } = true;

    public bool IsOutputVisible => HasOutput && IsExpanded;

    public string ExpandGlyph => IsExpanded ? "\uE70E" : "\uE70D";

    public string ExpandToolTip => IsExpanded ? "Collapse output" : "Expand output";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCompleted), nameof(IsSucceeded), nameof(IsFailed), nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    public partial bool IsRunning { get; private set; } = true;

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
        { IsRunning: true } => "Running…",
        { WasStopped: true } => "Stopped",
        { ExitCode: { } exitCode } => string.Create(CultureInfo.CurrentCulture, $"Exit code {exitCode}"),
        _ => "Couldn't start",
    };

    public string DurationText => FormatDuration(Duration);

    public void Append(OutputChunk chunk)
    {
        if (Segments.Count > 0 && Segments[^1] is { } last && last.Stream == chunk.Stream)
        {
            last.Append(chunk.Text);
            return;
        }

        Segments.Add(new OutputSegment(chunk.Stream, chunk.Text));
        if (Segments.Count == 1)
        {
            OnPropertyChanged(nameof(HasOutput));
            OnPropertyChanged(nameof(IsOutputVisible));
        }
    }

    public void Complete(int? exitCode, bool wasStopped, TimeSpan duration)
    {
        Duration = duration;
        ExitCode = exitCode;
        WasStopped = wasStopped;
        IsRunning = false;
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
    private void Copy()
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(string.Concat(Segments.Select(static segment => segment.Text)));
        Clipboard.SetContent(package);
    }

    private static string FormatDuration(TimeSpan duration) => duration switch
    {
        { TotalSeconds: < 1 } => string.Create(CultureInfo.CurrentCulture, $"{duration.TotalMilliseconds:0} ms"),
        { TotalMinutes: < 1 } => string.Create(CultureInfo.CurrentCulture, $"{duration.TotalSeconds:0.00} s"),
        { TotalHours: < 1 } => string.Create(CultureInfo.CurrentCulture, $"{duration.Minutes} min {duration.Seconds} s"),
        _ => string.Create(CultureInfo.CurrentCulture, $"{(int)duration.TotalHours} h {duration.Minutes} min {duration.Seconds} s"),
    };
}
