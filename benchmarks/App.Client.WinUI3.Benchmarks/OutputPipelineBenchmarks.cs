using System.Threading.Channels;
using App.Client.WinUI3.Benchmarks.Baseline;
using App.Domain;
using BenchmarkDotNet.Attributes;

namespace App.Client.WinUI3.Benchmarks;

/// <summary>
/// Compares what it takes to show the output of a command as it is read, from the pieces read to the text the runs are set to,
/// without what the UI does with that text, which the redesign also cut down to the last run.
/// </summary>
[MemoryDiagnoser]
public class OutputPipelineBenchmarks
{
    // As much as a read from the pipe returns.
    private const int ReadLength = 4096;

    private string[] _reads = [];

    /// <summary>The length of the output, in characters.</summary>
    [Params(128 * 1024, 1024 * 1024)]
    public int Length { get; set; }

    /// <summary>
    /// How many reads the UI thread gets to the output after: every one, as when a command writes slowly, or 16 of them,
    /// as when it writes over a megabyte a second, and the output is shown every 50 ms.
    /// </summary>
    [Params(1, 16)]
    public int ReadsPerRefresh { get; set; }

    [GlobalSetup]
    public void Setup() => _reads = LogText.Split(LogText.Create(Length, linesPerProgress: 50), ReadLength);

    [Benchmark(Baseline = true)]
    public int Original()
    {
        // The pieces read were passed to the UI thread by a channel, which showed each of them.
        var channel = Channel.CreateUnbounded<OutputChunk>(new UnboundedChannelOptions { SingleReader = true });
        var pipeline = new OriginalPipeline();
        var shown = 0;
        for (var index = 0; index < _reads.Length; index++)
        {
            channel.Writer.TryWrite(new OutputChunk(OutputStream.StandardOutput, new string(_reads[index].AsSpan())));
            if ((index + 1) % ReadsPerRefresh == 0)
            {
                while (channel.Reader.TryRead(out var chunk))
                {
                    shown += pipeline.Append(chunk);
                }
            }
        }

        while (channel.Reader.TryRead(out var chunk))
        {
            shown += pipeline.Append(chunk);
        }

        return shown + pipeline.Complete();
    }

    [Benchmark]
    public int Redesigned()
    {
        var pipeline = new RedesignedPipeline();
        var shown = 0;
        for (var index = 0; index < _reads.Length; index++)
        {
            pipeline.Write(OutputStream.StandardOutput, _reads[index]);
            if ((index + 1) % ReadsPerRefresh == 0)
            {
                shown += pipeline.Refresh();
            }
        }

        return shown + pipeline.Complete();
    }
}
