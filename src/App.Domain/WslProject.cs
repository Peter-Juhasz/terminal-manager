using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;

namespace App.Domain;

/// <summary>
/// A project whose commands are run by <c>bash</c> in a distribution of the Windows Subsystem for Linux, each by <c>wsl.exe</c>, as a script of a login shell,
/// so the profile of the user is read first, as it is in a terminal.
/// </summary>
/// <remarks>
/// <para>
/// The working folder is either a folder of Windows, such as <c>C:\src\app</c>, which <c>wsl.exe</c> is started in, so the shell starts in it as <c>/mnt/c/src/app</c> on its own;
/// or a folder of Linux, such as <c>~/src/app</c> or <c>/home/me/app</c>, which the script changes to first, while <c>wsl.exe</c> is started in a folder of Windows that is sure to exist,
/// as a process can't be started in a folder of Linux.
/// </para>
/// <para>
/// The script is passed by <c>--exec</c>, so it reaches <c>bash</c> as it is; after <c>--</c>, the rest of the command line would be run by the default shell of the user,
/// which would expand what is in it once more. A folder of Linux is changed to by the script, rather than by <c>--cd</c>, which only takes <c>~</c> itself or an absolute folder.
/// </para>
/// <para>
/// The environment variables are exported by the script, after the profile, as those of <c>wsl.exe</c> only reach Linux when <c>WSLENV</c> names them.
/// </para>
/// </remarks>
public sealed record class WslProject : LocalProject
{
    private static readonly Uri IconUri = new("ms-appx:///Assets/linux.svg");

    /// <summary>The name of the distribution commands are run in, as <c>wsl --list</c> shows it, or <see langword="null"/> for the default distribution.</summary>
    public string? Distribution { get; init; }

    /// <summary>The user of the distribution commands are run as, or <see langword="null"/> for its default user.</summary>
    public string? User { get; init; }

    [JsonIgnore]
    public override CommandLanguage Language => CommandLanguage.Shell;

    [JsonIgnore]
    public override string TerminalDisplayName => "WSL (bash)";

    [JsonIgnore]
    public override Uri? TerminalIconUri => IconUri;

    [JsonIgnore]
    public override string LocationDisplayName => Distribution switch
    {
        { Length: > 0 } distribution => $"{distribution}: {WorkingFolder}",
        _ => WorkingFolder,
    };

    [JsonIgnore]
    public override bool IsWorkingFolderOnWindows => !IsLinuxFolder(WorkingFolder);

    /// <summary>Whether <paramref name="folder"/> is a folder of Linux, as it starts with <c>/</c> or <c>~</c>, rather than one of Windows, such as <c>C:\src</c> or <c>\\server\share</c>.</summary>
    public static bool IsLinuxFolder(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        var trimmed = folder.TrimStart();
        return trimmed.StartsWith('/') || trimmed.StartsWith('~');
    }

    public override ProcessStartInfo CreateStartInfo(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Linux may not take carriage returns as the end of a line, which text boxes end them with
        var body = Wrap(command).ReplaceLineEndings("\n");

        // the profile is read before the script, so the variables of the project replace those it sets by the same name
        var script = new StringBuilder();
        if (!IsWorkingFolderOnWindows)
        {
            script.AppendShellChangeFolder(WorkingFolder.Trim());
        }

        script.AppendShellExports(ResolveEnvironmentVariables()).Append(body).Append('\n');

        var startInfo = new ProcessStartInfo("wsl.exe");
        if (Distribution is { Length: > 0 } distribution)
        {
            startInfo.ArgumentList.Add("--distribution");
            startInfo.ArgumentList.Add(distribution);
        }

        if (User is { Length: > 0 } user)
        {
            startInfo.ArgumentList.Add("--user");
            startInfo.ArgumentList.Add(user);
        }

        foreach (var argument in (string[])["--exec", "bash", "-l", "-c", script.ToString()])
        {
            startInfo.ArgumentList.Add(argument);
        }

        // a process can't be started in a folder of Linux, so it is started in one of Windows, which the script changes from
        if (!IsWorkingFolderOnWindows)
        {
            startInfo.WorkingDirectory = Path.GetTempPath();
        }

        // what wsl.exe itself writes, such as that the distribution isn't there, is written in UTF-16 otherwise, while the output is read as UTF-8
        startInfo.Environment["WSL_UTF8"] = "1";
        return startInfo;
    }
}
