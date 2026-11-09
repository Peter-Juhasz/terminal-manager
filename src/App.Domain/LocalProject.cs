using System.Diagnostics;
using System.Text.Json.Serialization;

namespace App.Domain;

/// <summary>
/// A project whose commands are run on this machine, each by a process of its own, started in the working folder.
/// </summary>
public abstract record class LocalProject : Project
{
    /// <summary>The folder commands are run in, with the environment variables in <see cref="Project.WorkingFolder"/> expanded.</summary>
    [JsonIgnore]
    public string ExpandedWorkingFolder => Environment.ExpandEnvironmentVariables(WorkingFolder);

    /// <summary>
    /// Whether the working folder is a folder of Windows, so processes are started in it, and it can be opened; it isn't for a folder of Linux of a <see cref="WslProject"/>,
    /// which the commands change to themselves.
    /// </summary>
    [JsonIgnore]
    public virtual bool IsWorkingFolderOnWindows => true;

    /// <summary>
    /// Whether commands are also run with the variables in the .env file in the working folder, which those in <see cref="Project.EnvironmentVariables"/> replace by the same name;
    /// only when it is a folder of Windows.
    /// </summary>
    public bool LoadEnvFile { get; init; }

    /// <summary>
    /// Whether the scripts in well known files in the working folder, such as .terminal-manager.yml and package.json, are not shown; otherwise they are,
    /// as they are when the files change, when it is a folder of Windows.
    /// </summary>
    /// <remarks>
    /// Turned off rather than on, so a project saved before it was added shows them: the serializer sets an init-only property missing from the file
    /// to its default, whatever it is initialized to.
    /// </remarks>
    public bool IgnoreScriptFiles { get; init; }

    /// <summary>The folder the scripts of the files are read from, or <see langword="null"/> when they aren't, as <see cref="IgnoreScriptFiles"/> is set.</summary>
    [JsonIgnore]
    public string? ScriptFilesFolder => !IgnoreScriptFiles && IsWorkingFolderOnWindows ? ExpandedWorkingFolder : null;

    /// <summary>
    /// The environment variables commands are run with: those in the .env file in the working folder, as it is when they are run, if <see cref="LoadEnvFile"/> is set,
    /// then those in <see cref="Project.ExpandedEnvironmentVariables"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> ResolveEnvironmentVariables()
    {
        if (!LoadEnvFile || !IsWorkingFolderOnWindows)
        {
            return ExpandedEnvironmentVariables;
        }

        var variables = new Dictionary<string, string>(DotEnvFile.Read(Path.Combine(ExpandedWorkingFolder, DotEnvFile.FileName)), StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in ExpandedEnvironmentVariables)
        {
            variables[name] = value;
        }

        return variables;
    }

    /// <summary>
    /// Describes the process that runs <paramref name="command"/>. The redirection of its streams is set by the caller, and so is its working folder,
    /// when <see cref="IsWorkingFolderOnWindows"/> is set; otherwise, it is set here.
    /// </summary>
    public abstract ProcessStartInfo CreateStartInfo(string command);

    /// <summary>
    /// What is written to the standard input of the process that runs <paramref name="command"/>, which is closed after it,
    /// or <see langword="null"/> to close it right away, as when the command is passed by <see cref="CreateStartInfo"/> alone.
    /// </summary>
    public virtual string? CreateStandardInput(string command) => null;
}
