namespace App.Client.WinUI3.Services;

/// <summary>
/// Keeps secrets, such as passwords, out of the projects, by a name of their own.
/// </summary>
public interface ICredentialStore
{
    /// <summary>The secret kept by <paramref name="target"/>, or <see langword="null"/> when none is.</summary>
    string? Read(string target);

    /// <summary>Keeps <paramref name="secret"/> of <paramref name="userName"/> by <paramref name="target"/>, in place of the one kept by it before.</summary>
    void Write(string target, string userName, string secret);
}
