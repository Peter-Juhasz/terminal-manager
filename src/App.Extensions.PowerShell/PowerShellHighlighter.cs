using System.Text.Json;
using System.Text.Json.Serialization;
using App.Domain;
using App.Services;

namespace App.Extensions.PowerShell;

/// <summary>
/// Finds what to color in a script by the tokens the parser of PowerShell reads it into, by asking a pwsh that keeps running, so it only starts up once.
/// Parsing doesn't depend on the working folder, so the same pwsh parses for all the projects; it is another one than that of completion,
/// so parsing never waits for a completion that takes long.
/// </summary>
public sealed partial class PowerShellHighlighter : ISyntaxHighlighter, IDisposable
{
    // Reads a script from each line it is given, and writes the spans to color in it on a line of its own.
    // The text of an expandable string is colored around what is expanded in it, which is colored as it would be outside of it.
    // Bare words, such as the names of commands, are string literal tokens too, so strings are told apart by their kind.
    private const string Script = """
        [Console]::InputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        function Get-Spans($Tokens) {
            foreach ($token in $Tokens) {
                $start = $token.Extent.StartOffset
                $end = $token.Extent.EndOffset
                if ($token.Kind -in 'StringExpandable', 'HereStringExpandable' -and $token.NestedTokens) {
                    # the text between the tokens of a subexpression is part of it, rather than of the string
                    $depth = 0
                    foreach ($nested in $token.NestedTokens) {
                        if ($depth -eq 0 -and $nested.Extent.StartOffset -gt $start) {
                            @{ start = $start; length = $nested.Extent.StartOffset - $start; kind = 'String' }
                        }
                        Get-Spans $nested
                        if ($nested.Kind -in 'DollarParen', 'AtParen', 'AtCurly', 'LParen', 'LCurly') { $depth++ }
                        elseif ($nested.Kind -in 'RParen', 'RCurly') { $depth-- }
                        if ($depth -eq 0) { $start = $nested.Extent.EndOffset }
                    }
                    if ($end -gt $start) {
                        @{ start = $start; length = $end - $start; kind = 'String' }
                    }
                    continue
                }
                $kind = if ($token.Kind -eq 'Comment') { 'Comment' }
                    elseif ($token.TokenFlags -band [System.Management.Automation.Language.TokenFlags]::Keyword) { 'Keyword' }
                    elseif ($token.TokenFlags -band [System.Management.Automation.Language.TokenFlags]::TypeName) { 'TypeName' }
                    elseif ($token.Kind -in 'StringLiteral', 'StringExpandable', 'HereStringLiteral', 'HereStringExpandable') { 'String' }
                if ($null -ne $kind -and $end -gt $start) {
                    @{ start = $start; length = $end - $start; kind = $kind }
                }
            }
        }
        while ($null -ne ($private:line = [Console]::In.ReadLine())) {
            $private:response = try {
                $private:request = ConvertFrom-Json -InputObject $line
                $private:tokens = $null
                $private:errors = $null
                $null = [System.Management.Automation.Language.Parser]::ParseInput($request.input, [ref]$tokens, [ref]$errors)
                @{ spans = @(Get-Spans $tokens) }
            }
            catch {
                @{ spans = @() }
            }
            [Console]::Out.WriteLine((ConvertTo-Json -InputObject $response -Depth 4 -Compress))
        }
        """;

    private readonly Lock _lock = new();
    private PowerShellHost? _host;

    /// <summary>
    /// The spans to color in <paramref name="input"/>, or <see langword="null"/> when it couldn't be parsed, such as when pwsh isn't installed.
    /// The pwsh is left to answer when <paramref name="cancellationToken"/> is canceled, so it doesn't have to be started again.
    /// </summary>
    public Task<HighlightedCode?> HighlightAsync(string input, CancellationToken cancellationToken) =>
        HighlightAsync(prefix: string.Empty, input, cancellationToken);

    /// <inheritdoc/>
    /// <remarks>A span that starts in <paramref name="prefix"/> is colored from the start of <paramref name="input"/>.</remarks>
    public async Task<HighlightedCode?> HighlightAsync(string prefix, string input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(input);

        PowerShellHost host;
        lock (_lock)
        {
            if (_host is null || _host.HasExited)
            {
                _host?.Dispose();
                _host = new PowerShellHost(Script, workingFolder: null);
            }

            host = _host;
        }

        var request = JsonSerializer.Serialize(new HighlightRequest(Input: prefix + input), HighlightJsonSerializerContext.Default.HighlightRequest);
        var line = await host.SendAsync(request).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (line is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(line, HighlightJsonSerializerContext.Default.HighlightResponse) is { Spans: { } spans }
                ? HighlightedCode.Create(input, Shift(spans, prefix.Length))
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Moves spans back by offset, cutting off the part of those that start before it, and leaving out those that end before it.
    private static IEnumerable<SyntaxSpan> Shift(IEnumerable<SyntaxSpan> spans, int offset)
    {
        foreach (var span in spans)
        {
            var start = Math.Max(span.Start - offset, 0);
            var end = span.Start + span.Length - offset;
            if (end > start)
            {
                yield return span with { Start = start, Length = end - start };
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _host?.Dispose();
            _host = null;
        }
    }
}

internal sealed record HighlightRequest(string Input);

internal sealed record HighlightResponse(IReadOnlyList<SyntaxSpan>? Spans);

// Source generated, so serialization keeps working when the app is trimmed.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(HighlightRequest))]
[JsonSerializable(typeof(HighlightResponse))]
internal sealed partial class HighlightJsonSerializerContext : JsonSerializerContext;
