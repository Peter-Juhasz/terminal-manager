using System.Security.Cryptography;
using System.Text;
using App.Client.WinUI3.Services;

namespace App.Client.WinUI3.Models;

/// <summary>
/// The scripts read from a file in the working folder of a project, as it was when it was last read, or why they couldn't be.
/// </summary>
/// <param name="Format">The kind of file, which tells how it is read.</param>
/// <param name="Path">Where the file is.</param>
/// <param name="Scripts">The scripts in the file, in the order they are written in it; none when it couldn't be read.</param>
/// <param name="Error">Why the file couldn't be read, or <see langword="null"/> when it could.</param>
public sealed record class ScriptFileSnapshot(IScriptFileFormat Format, string Path, IReadOnlyList<Script> Scripts, string? Error = null)
{
    /// <summary>Whether <paramref name="other"/> has the same scripts read from the same file, so nothing shown changes.</summary>
    public bool IsEquivalentTo(ScriptFileSnapshot? other) =>
        other is not null && ReferenceEquals(Format, other.Format) && Path == other.Path && Error == other.Error && Scripts.SequenceEqual(other.Scripts);

    /// <summary>
    /// The id of the script named <paramref name="name"/> in a file named <paramref name="fileName"/>, which is the same each time the file is read,
    /// so the script is shown as the same one when the file changes.
    /// </summary>
    public static Guid CreateScriptId(string fileName, string name)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(name);

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes($"{fileName}\n{name}"), hash);
        return new Guid(hash[..16]);
    }
}
