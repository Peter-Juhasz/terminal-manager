namespace App.Domain;

/// <summary>
/// The language the commands of a <see cref="Project"/> are written in, which tells how they can be highlighted, and what can be put before and after them.
/// </summary>
public enum CommandLanguage
{
    /// <summary>A command line of the Command Prompt (cmd).</summary>
    Cmd,

    /// <summary>The arguments of an app, as they are on a command line, such as those passed by <c>dotnet run --</c>.</summary>
    Arguments,

    /// <summary>A script of PowerShell, or of Windows PowerShell.</summary>
    PowerShell,

    /// <summary>A script of a POSIX shell, such as <c>sh</c>.</summary>
    Shell,

    /// <summary>The code of a C# file-based app.</summary>
    CSharp,

    /// <summary>A Python script.</summary>
    Python,

    /// <summary>A JavaScript script, as run by Node.js.</summary>
    JavaScript,
}
