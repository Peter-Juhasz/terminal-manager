using App.Client.WinUI3.Services;

namespace App.Client.WinUI3.Benchmarks.Baseline;

/// <summary>
/// What the original version did on the UI thread with each piece of output read, without the UI itself: the view model took the progress
/// sequences out of it, and joined it to the text of the last segment, then the control set the text of its run to all of it.
/// </summary>
internal sealed class OriginalPipeline
{
    private readonly ProgressSequenceParser _parser = new();
    private readonly List<Segment> _segments = [];

    public ProgressReport Progress { get; private set; } = new(ProgressState.None, 0);

    /// <summary>Appends <paramref name="chunk"/>, returning the length of the text the runs were set to, which the UI copied and laid out.</summary>
    public int Append(OutputChunk chunk)
    {
        if (chunk.Stream is OutputStream.StandardOutput)
        {
            chunk = chunk with { Text = _parser.Parse(chunk.Text, ReportProgress) };
        }

        return AppendText(chunk);
    }

    public int Complete() => AppendText(new OutputChunk(OutputStream.StandardOutput, _parser.Flush()));

    private void ReportProgress(ProgressReport report)
    {
        var value = Progress.Value;
        if (report.Value is { } reported && (reported > 0 || report.State is not (ProgressState.Error or ProgressState.Paused)))
        {
            value = reported;
        }
        else if (report.State is ProgressState.None)
        {
            value = 0;
        }

        Progress = new ProgressReport(report.State, value);
    }

    private int AppendText(OutputChunk chunk)
    {
        if (chunk.Text.Length == 0)
        {
            return 0;
        }

        // The run of the last segment was shown without the line breaks at its end.
        if (_segments.Count > 0 && _segments[^1] is { } last && last.Stream == chunk.Stream)
        {
            last.Text = string.Concat(last.Text, chunk.Text);
            return last.Text.TrimEnd('\r', '\n').Length;
        }

        // The run of the segment before was set to all of its text, with the line breaks at its end.
        var shown = _segments.Count > 0 ? _segments[^1].Text.Length : 0;
        _segments.Add(new Segment(chunk.Stream, chunk.Text));
        return shown + chunk.Text.TrimEnd('\r', '\n').Length;
    }

    private sealed class Segment(OutputStream stream, string text)
    {
        public OutputStream Stream { get; } = stream;

        public string Text { get; set; } = text;
    }
}
