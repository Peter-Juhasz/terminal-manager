using App.Client.WinUI3.Controls;
using App.Client.WinUI3.Services;
using App.Client.WinUI3.ViewModels;

namespace App.Client.WinUI3.Benchmarks;

/// <summary>
/// What the redesigned version does, without the UI itself: the threads reading the output write each piece to the buffer, and on every refresh
/// the UI thread appends what was written since to the output, which the control lays out in runs, setting the text of those that changed.
/// </summary>
internal sealed class RedesignedPipeline
{
    private readonly OutputBuffer _buffer = new();
    private readonly CommandOutput _output = new();
    private readonly OutputRunBuilder _builder = new();
    private readonly List<RunUpdate> _updates = [];
    private int _chunkCount;
    private int _shown;

    public RedesignedPipeline() => _output.Appended += (_, _) => Show();

    public ProgressReport Progress { get; private set; }

    public void Write(OutputStream stream, ReadOnlySpan<char> text) => _buffer.Write(stream, text);

    /// <summary>Shows what was written since the last refresh, returning the length of the text the runs were set to.</summary>
    public int Refresh()
    {
        var batch = _buffer.Read();
        _output.Append(batch.Chunks);
        Progress = batch.Progress;

        var shown = _shown;
        _shown = 0;
        return shown;
    }

    public int Complete()
    {
        _buffer.Complete();
        return Refresh();
    }

    private void Show()
    {
        var chunks = _output.Chunks;
        for (; _chunkCount < chunks.Count; _chunkCount++)
        {
            _builder.Append(chunks[_chunkCount]);
        }

        _builder.Flush(_updates);
        foreach (var update in _updates)
        {
            _shown += update.Text.Length;
        }

        _updates.Clear();
    }
}
