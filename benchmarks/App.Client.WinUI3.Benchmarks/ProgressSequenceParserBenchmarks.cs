using System.Text;
using App.Services;
using BenchmarkDotNet.Attributes;

namespace App.Client.WinUI3.Benchmarks;

/// <summary>Compares taking the progress sequences out of 256K characters of output, read in pieces of 4096.</summary>
[MemoryDiagnoser]
public class ProgressSequenceParserBenchmarks
{
    private const int ReadLength = 4096;

    private readonly StringBuilder _output = new(ReadLength);
    private string[] _reads = [];
    private ProgressReport _progress;

    /// <summary>How many lines there are for each progress sequence, or 0 for none.</summary>
    [Params(0, 50, 1)]
    public int LinesPerProgress { get; set; }

    [GlobalSetup]
    public void Setup() => _reads = LogText.Split(LogText.Create(256 * 1024, LinesPerProgress), ReadLength);

    [Benchmark(Baseline = true)]
    public int Original()
    {
        var parser = new Baseline.ProgressSequenceParser();
        var length = 0;
        foreach (var read in _reads)
        {
            // As the view model passed its method.
            length += parser.Parse(read, ReportProgress).Length;
        }

        return length + parser.Flush().Length;
    }

    [Benchmark]
    public int Redesigned()
    {
        // As the buffer appends to the text it collects, which is cleared when it is read.
        var parser = new ProgressSequenceParser();
        var length = 0;
        foreach (var read in _reads)
        {
            parser.Parse(read, _output);
            length += _output.Length;
            _output.Clear();
        }

        parser.Flush(_output);
        length += _output.Length;
        _output.Clear();
        _progress = parser.Progress;
        return length;
    }

    private void ReportProgress(ProgressReport report) => _progress = report;
}
