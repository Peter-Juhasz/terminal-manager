namespace App.Client.WinUI3.Services;

/// <summary>Tells what the token at a position in a command is, such as the syntax of a command, or the type of a property.</summary>
public interface IQuickInfoProvider
{
    /// <summary>
    /// What is at <paramref name="position"/> in <paramref name="input"/>, as it would be when <paramref name="prefix"/> was before it, run in <paramref name="workingFolder"/>;
    /// <see langword="null"/> when there is nothing there to tell about, or it can't be told.
    /// </summary>
    Task<QuickInfo?> GetQuickInfoAsync(string workingFolder, string prefix, string input, int position, CancellationToken cancellationToken);
}

/// <summary>What the text between <paramref name="Start"/> and <paramref name="Length"/> of the input is, told by <paramref name="Text"/>.</summary>
public sealed record QuickInfo(int Start, int Length, string? Text);
