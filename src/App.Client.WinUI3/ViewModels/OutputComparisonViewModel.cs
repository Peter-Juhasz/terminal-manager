using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace App.Client.WinUI3.ViewModels;

/// <summary>One side of a comparison: the command, when it was run, and what it wrote.</summary>
public sealed record ComparedOutput(string Command, DateTimeOffset StartedAt, string StatusText, string Text)
{
    /// <summary>When it was run, and how it ended.</summary>
    public string Description => $"{StartedAt.ToLocalTime().ToString("G", CultureInfo.CurrentCulture)} · {StatusText}";

    public static ComparedOutput From(CommandEntryViewModel entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new(Command: entry.Command, StartedAt: entry.StartedAt, StatusText: entry.StatusText, Text: entry.Output.ToString());
    }
}

/// <summary>
/// The outputs of two commands, compared line by line side by side, again whenever what is ignored changes; the comparison is made off the UI thread,
/// as long outputs take a while.
/// </summary>
public sealed partial class OutputComparisonViewModel : ObservableObject
{
    // a number, with the separators of decimals and times, so durations and timestamps don't make every line differ
    private static readonly Regex Numbers = NumbersRegex();

    private readonly IReadOnlyList<string> _leftLines;
    private readonly IReadOnlyList<string> _rightLines;
    private CancellationTokenSource? _comparison;

    public OutputComparisonViewModel(ComparedOutput left, ComparedOutput right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        Left = left;
        Right = right;
        _leftLines = TextDiff.SplitLines(left.Text);
        _rightLines = TextDiff.SplitLines(right.Text);
    }

    public ComparedOutput Left { get; }

    public ComparedOutput Right { get; }

    public string Title => Left.Command == Right.Command ? Left.Command : $"{Left.Command} ↔ {Right.Command}";

    [ObservableProperty]
    public partial bool IgnoreWhitespace { get; set; }

    [ObservableProperty]
    public partial bool IgnoreCase { get; set; }

    [ObservableProperty]
    public partial bool IgnoreNumbers { get; set; }

    /// <summary>The rows of the comparison, laid out side by side.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoChanges))]
    public partial IReadOnlyList<DiffRow> Rows { get; private set; } = (DiffRow[])[];

    /// <summary>The index of the first row of each run of changed rows, in order, which are moved between.</summary>
    public IReadOnlyList<int> ChangeStarts { get; private set; } = (int[])[];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoChanges))]
    public partial bool IsComparing { get; private set; }

    /// <summary>Whether the outputs were too different to find the fewest changes between them, so more may be shown changed than are.</summary>
    [ObservableProperty]
    public partial bool IsApproximate { get; private set; }

    [ObservableProperty]
    public partial string SummaryText { get; private set; } = string.Empty;

    /// <summary>Why the outputs couldn't be compared, if they couldn't.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorText { get; private set; }

    public bool HasError => ErrorText is not null;

    public bool HasNoChanges => !IsComparing && ChangeStarts.Count is 0;

    partial void OnIgnoreWhitespaceChanged(bool value) => _ = CompareAsync();

    partial void OnIgnoreCaseChanged(bool value) => _ = CompareAsync();

    partial void OnIgnoreNumbersChanged(bool value) => _ = CompareAsync();

    /// <summary>Compares the outputs with what is ignored now, in place of a comparison that is still being made.</summary>
    public async Task CompareAsync()
    {
        // disposed by the comparison it was made for, once it is cancelled
        _comparison?.Cancel();
        using var comparison = _comparison = new();
        var cancellationToken = comparison.Token;

        var options = new TextDiffOptions
        {
            IgnoreCase = IgnoreCase,
            IgnoreWhitespace = IgnoreWhitespace,
            IgnoredPatterns = IgnoreNumbers ? (Regex[])[Numbers] : (Regex[])[],
        };

        IsComparing = true;
        ErrorText = null;
        try
        {
            var (result, changeStarts) = await Task.Run(() =>
            {
                var result = TextDiff.Compare(_leftLines, _rightLines, options, cancellationToken);
                return (result, FindChangeStarts(result.Rows));
            }, cancellationToken);

            ChangeStarts = changeStarts;
            Rows = result.Rows;
            IsApproximate = result.IsApproximate;
            SummaryText = Summarize(result.Rows);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // compared again with other options
            return;
        }
        catch (Exception ex)
        {
            // shown, rather than comparing on forever
            Debug.WriteLine($"The outputs couldn't be compared: {ex}");
            ErrorText = ex.Message;
        }
        finally
        {
            if (_comparison == comparison)
            {
                _comparison = null;
            }
        }

        IsComparing = false;
    }

    /// <summary>The index of the start of the first run of changes after the row at <paramref name="index"/>, or the first one past the last.</summary>
    public int? FindNextChange(int index) => ChangeStarts switch
    {
        [] => null,
        _ => ChangeStarts.FirstOrDefault(start => start > index, ChangeStarts[0]),
    };

    /// <summary>The index of the start of the last run of changes before the row at <paramref name="index"/>, or the last one before the first.</summary>
    public int? FindPreviousChange(int index) => ChangeStarts switch
    {
        [] => null,
        _ => ChangeStarts.LastOrDefault(start => start < index, ChangeStarts[^1]),
    };

    private static int[] FindChangeStarts(IReadOnlyList<DiffRow> rows)
    {
        List<int> starts = [];
        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index].Kind is not DiffKind.Unchanged && (index is 0 || rows[index - 1].Kind is DiffKind.Unchanged))
            {
                starts.Add(index);
            }
        }

        return [.. starts];
    }

    private static string Summarize(IReadOnlyList<DiffRow> rows)
    {
        var modified = rows.Count(static row => row.Kind is DiffKind.Modified);
        var deleted = rows.Count(static row => row.Kind is DiffKind.Deleted);
        var inserted = rows.Count(static row => row.Kind is DiffKind.Inserted);
        if (modified + deleted + inserted is 0)
        {
            return "No differences";
        }

        List<string> parts = [];
        if (modified > 0)
        {
            parts.Add(string.Create(CultureInfo.CurrentCulture, $"{modified} changed"));
        }

        if (deleted > 0)
        {
            parts.Add(string.Create(CultureInfo.CurrentCulture, $"{deleted} removed"));
        }

        if (inserted > 0)
        {
            parts.Add(string.Create(CultureInfo.CurrentCulture, $"{inserted} added"));
        }

        return $"{string.Join(", ", parts)} {(modified + deleted + inserted is 1 ? "line" : "lines")}";
    }

    [GeneratedRegex(@"\d+(?:[.,:]\d+)*")]
    private static partial Regex NumbersRegex();
}
