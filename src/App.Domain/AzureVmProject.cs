using System.Text.Json.Serialization;

namespace App.Domain;

/// <summary>
/// A project whose commands are run on an Azure virtual machine, each by <c>az vm run-command invoke</c>, as a script that changes to the working folder on the machine first:
/// by the shell of a Linux machine for <see cref="AzureVmShellProject"/>, or by PowerShell on a Windows machine for <see cref="AzureVmPowerShellProject"/>.
/// </summary>
/// <remarks>
/// Commands are run as root, or as the local system account, which is why they can't be run as an administrator in addition.
/// Each command runs to its end before any of its output is shown, as run commands report it only then, and only the last 4 KB of each stream of it.
/// Stopping a command stops waiting for it, but it keeps running on the machine.
/// </remarks>
public abstract record class AzureVmProject : Project
{
    /// <summary>The name or id of the subscription the machine is in, or <see langword="null"/> for the default subscription of the Azure CLI.</summary>
    public string? Subscription { get; init; }

    /// <summary>The resource group the machine is in.</summary>
    public string ResourceGroup { get; init; } = string.Empty;

    /// <summary>The name of the machine.</summary>
    public string VmName { get; init; } = string.Empty;

    /// <summary>The id of the built-in run command that runs the script, such as <c>RunShellScript</c>.</summary>
    [JsonIgnore]
    public abstract string CommandId { get; }

    /// <summary>The extension of the file the script is written to, so the Azure CLI reads it.</summary>
    [JsonIgnore]
    public abstract string ScriptFileExtension { get; }

    [JsonIgnore]
    public override string LocationDisplayName => $"{VmName}: {WorkingFolder}";

    private static readonly Uri IconUri = new("ms-appx:///Assets/azure-vm.svg");

    [JsonIgnore]
    public override Uri? TerminalIconUri => IconUri;

    /// <summary>
    /// The script run on the machine for <paramref name="command"/>: it changes to the working folder, sets the environment variables, and runs
    /// <see cref="PrependScript"/>, the command and <see cref="AppendScript"/>, each on lines of their own, then writes <paramref name="exitCodeMarker"/>
    /// to the standard output, at the start of a line of its own, followed by the exit code of the command.
    /// </summary>
    public string CreateScript(string command, string exitCodeMarker)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrEmpty(exitCodeMarker);

        // the machine may not take carriage returns as the end of a line, which text boxes end them with
        var body = Wrap(command).ReplaceLineEndings("\n");
        return CreateScript(body, ExpandedEnvironmentVariables, exitCodeMarker);
    }

    /// <summary>The script run for <paramref name="body"/>, which is the command with the scripts before and after it, as <see cref="CreateScript(string, string)"/> describes it.</summary>
    private protected abstract string CreateScript(string body, IReadOnlyDictionary<string, string> environmentVariables, string exitCodeMarker);
}
