using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace App.Client.WinUI3.Services;

/// <summary>
/// Completes commands, and tells what their tokens are, the way PowerShell does, by asking a pwsh that keeps running in the working folder, so it only starts up once.
/// Completion and quick info are asked of the same pwsh, as quick info is found by completing, which goes by what that pwsh has loaded already.
/// </summary>
public sealed partial class PowerShellLanguageService : ICommandCompleter, IQuickInfoProvider, IDisposable
{
    // Reads a request from each line it is given, and writes the answer on a line of its own: a completion, or quick info, when it asks for that.
    // The variables are private, so they aren't offered by the completion of variables; for the same reason nothing is defined as a function,
    // and statements are used instead of script blocks, which couldn't see the private variables.
    private const string Script = $$"""
        [Console]::InputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        $ProgressPreference = 'SilentlyContinue'
        while ($null -ne ($private:line = [Console]::In.ReadLine())) {
            $private:response = try {
                $private:request = ConvertFrom-Json -InputObject $line
                if ($request.quickInfo) {
        {{QuickInfoHandler}}
                }
                else {
        {{CompletionHandler}}
                }
            }
            catch {
                @{ replacementIndex = 0; replacementLength = 0; matches = @() }
            }
            [Console]::Out.WriteLine((ConvertTo-Json -InputObject $response -Depth 4 -Compress))
        }
        """;

    // Completes the text before the cursor.
    private const string CompletionHandler = """
        $private:completion = TabExpansion2 -inputScript $request.input -cursorColumn $request.cursor
        @{
            replacementIndex = $completion.ReplacementIndex
            replacementLength = $completion.ReplacementLength
            matches = @($completion.CompletionMatches | ForEach-Object {
                @{
                    completionText = $_.CompletionText
                    listItemText = $_.ListItemText
                    resultType = $_.ResultType.ToString()
                    toolTip = $_.ToolTip
                }
            })
        }
        """;

    // Quick info is the tooltip of the match that completing the token at the position, as it is up to its end, gives for the token itself;
    // the text after it is left out, as a parameter that is given already isn't offered again.
    // When the tooltip says no more than the name, the syntax of a command, or the full name of a type, is told instead.
    private const string QuickInfoHandler = """
        $private:tokens = $null
        $private:errors = $null
        $null = [System.Management.Automation.Language.Parser]::ParseInput($request.input, [ref]$tokens, [ref]$errors)
        $private:position = $request.cursor
        $private:token = $null
        $private:candidates = $tokens
        while ($candidates) {
            $private:next = $null
            foreach ($private:candidate in $candidates) {
                if ($candidate.Extent.StartOffset -le $position -and $position -lt $candidate.Extent.EndOffset) { $next = $candidate; break }
            }
            if ($null -eq $next) { break }
            $token = $next
            $candidates = $next.NestedTokens
        }
        $private:flags = [System.Management.Automation.Language.TokenFlags]
        $private:isCommand = $token -and ($token.TokenFlags -band $flags::CommandName)
        $private:isType = $token -and ($token.TokenFlags -band $flags::TypeName)
        $private:isMember = $token -and ($token.TokenFlags -band $flags::MemberName)
        $private:text = $null
        if ($isCommand -or $isType -or $isMember -or $token.Kind -in 'Parameter', 'Variable', 'SplattedVariable') {
            $private:name = if ($token.Kind -eq 'Parameter') { '-' + $token.ParameterName } else { $token.Text }
            $private:end = $token.Extent.StartOffset + $name.Length
            $private:completion = TabExpansion2 -inputScript $request.input.Substring(0, $end) -cursorColumn $end
            foreach ($private:match in $completion.CompletionMatches) {
                if ($match.CompletionText -eq $name -or $match.CompletionText.TrimEnd('(', ')') -eq $name -or $match.ListItemText -eq $name) { $text = $match.ToolTip; break }
            }
            if ($text) { $text = $text.Trim() }
            if ($text -eq ($name.TrimStart('$', '@', '-') -replace '^\w+:', '')) { $text = $null }
            if ($isCommand) {
                $private:command = Get-Command -Name $name -ErrorAction Ignore | Select-Object -First 1
                if ($command -is [System.Management.Automation.AliasInfo] -and $command.ResolvedCommand) {
                    $text = "Alias of $($command.ResolvedCommand.Name)`n" + ((Get-Command -Name $command.ResolvedCommand.Name -Syntax -ErrorAction Ignore) -join "`n").Trim()
                }
                elseif (-not $text -and $command) {
                    $text = ((Get-Command -Name $name -Syntax -ErrorAction Ignore) -join "`n").Trim()
                }
            }
            if (-not $text -and $isType -and ($private:type = $name -as [type])) {
                $private:typeKind = if ($type.IsEnum) { 'enum' } elseif ($type.IsInterface) { 'interface' } elseif ($type.IsValueType) { 'struct' } else { 'class' }
                $text = "$typeKind $($type.FullName)"
            }
        }
        if ($text) {
            # the syntax of a command can have many sets, of which only the first are told
            $private:lines = @($text -split '\r?\n' | Where-Object { $_.Trim() })
            $text = ($lines | Select-Object -First 8) -join "`n"
            if ($lines.Count -gt 8) { $text += "`n…" }
            @{ start = $token.Extent.StartOffset; length = $name.Length; text = $text }
        }
        else {
            @{ start = 0; length = 0 }
        }
        """;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, PowerShellHost> _hosts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Starts the pwsh for <paramref name="workingFolder"/>, and has it load what completion needs, so the first completion is quick too.</summary>
    public void Prepare(string workingFolder) =>
        _ = CompleteAsync(workingFolder, "Get-", 4, CancellationToken.None);

    /// <summary>
    /// Completes the text before <paramref name="cursor"/> in <paramref name="input"/>, or returns <see langword="null"/> when it can't be completed, such as when pwsh isn't installed.
    /// The pwsh is left to answer when <paramref name="cancellationToken"/> is canceled, so it doesn't have to be started again.
    /// </summary>
    public Task<CommandCompletion?> CompleteAsync(string workingFolder, string input, int cursor, CancellationToken cancellationToken) =>
        CompleteAsync(workingFolder, prefix: string.Empty, input, cursor, cancellationToken);

    /// <inheritdoc/>
    public async Task<CommandCompletion?> CompleteAsync(string workingFolder, string prefix, string input, int cursor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workingFolder);
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(input);

        var request = JsonSerializer.Serialize(new LanguageServiceRequest(Input: prefix + input, Cursor: prefix.Length + cursor), LanguageServiceJsonSerializerContext.Default.LanguageServiceRequest);
        var line = await GetHost(workingFolder).SendAsync(request).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (Deserialize(line, LanguageServiceJsonSerializerContext.Default.CommandCompletion) is not { } completion)
        {
            return null;
        }

        // what isn't within the input couldn't be put in its place
        completion = completion with { ReplacementIndex = completion.ReplacementIndex - prefix.Length };
        return completion is { ReplacementIndex: >= 0, ReplacementLength: >= 0 } && completion.ReplacementIndex + completion.ReplacementLength <= input.Length
            ? completion.WithMatchedLengths(input, cursor)
            : null;
    }

    /// <inheritdoc/>
    /// <remarks>What is told about are commands, parameters, variables, members and types.</remarks>
    public async Task<QuickInfo?> GetQuickInfoAsync(string workingFolder, string prefix, string input, int position, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workingFolder);
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(input);

        if (position < 0 || position >= input.Length)
        {
            return null;
        }

        var request = JsonSerializer.Serialize(new LanguageServiceRequest(Input: prefix + input, Cursor: prefix.Length + position, QuickInfo: true), LanguageServiceJsonSerializerContext.Default.LanguageServiceRequest);
        var line = await GetHost(workingFolder).SendAsync(request).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (Deserialize(line, LanguageServiceJsonSerializerContext.Default.QuickInfo) is not { Text.Length: > 0 } info)
        {
            return null;
        }

        // what isn't within the input isn't where it was asked for
        info = info with { Start = info.Start - prefix.Length };
        return info is { Start: >= 0, Length: > 0 } && info.Start + info.Length <= input.Length ? info : null;
    }

    private PowerShellHost GetHost(string workingFolder)
    {
        lock (_lock)
        {
            if (!_hosts.TryGetValue(workingFolder, out var existing) || existing.HasExited)
            {
                existing?.Dispose();
                existing = new PowerShellHost(Script, workingFolder);
                _hosts[workingFolder] = existing;
            }

            return existing;
        }
    }

    private static T? Deserialize<T>(string? line, JsonTypeInfo<T> typeInfo) where T : class
    {
        if (line is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(line, typeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var host in _hosts.Values)
            {
                host.Dispose();
            }

            _hosts.Clear();
        }
    }
}

internal sealed record LanguageServiceRequest(string Input, int Cursor, bool QuickInfo = false);

// Source generated, so serialization keeps working when the app is trimmed.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(LanguageServiceRequest))]
[JsonSerializable(typeof(CommandCompletion))]
[JsonSerializable(typeof(QuickInfo))]
internal sealed partial class LanguageServiceJsonSerializerContext : JsonSerializerContext;
