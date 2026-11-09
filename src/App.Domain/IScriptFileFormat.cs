namespace App.Domain;

/// <summary>
/// A well known kind of file in the working folder of a project that scripts are read from, such as package.json; the scripts are shown, but can't be changed in the app.
/// </summary>
public interface IScriptFileFormat
{
    /// <summary>The name of the file in the working folder.</summary>
    string FileName { get; }

    /// <summary>What the scripts of the file are shown under.</summary>
    string DisplayName { get; }

    /// <summary>The names of other files in the working folder that change how the scripts are run, such as lock files, so they are read again when those change too.</summary>
    IReadOnlyList<string> RelatedFileNames { get; }

    /// <summary>Whether the scripts of the file can be run by <paramref name="project"/>, which depends on the language its commands are written in.</summary>
    bool IsSupportedBy(Project project);

    /// <summary>Reads the scripts in <paramref name="text"/>, for <paramref name="project"/>, which only has those it can run.</summary>
    /// <param name="relatedFiles">The names of those in <see cref="RelatedFileNames"/> that are in the working folder.</param>
    /// <exception cref="ScriptFileFormatException">The file isn't written as it has to be.</exception>
    IReadOnlyList<Script> Parse(string text, Project project, IReadOnlySet<string> relatedFiles);
}

/// <summary>
/// A file that scripts are read from isn't written as it has to be.
/// </summary>
/// <param name="reason">What is wrong with it.</param>
/// <param name="line">The line it is on, counted from 1, or <see langword="null"/> when it isn't known.</param>
public sealed class ScriptFileFormatException(string reason, int? line = null) : Exception(line is { } number ? $"Line {number}: {reason}" : reason)
{
    public string Reason { get; } = reason;

    public int? Line { get; } = line;
}
