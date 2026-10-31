namespace App.Client.WinUI3.Services;

/// <summary>
/// Where the output of a command is written to as it is read, such as the <see cref="OutputBuffer"/> it is shown from,
/// or the connection to the app, when it is run by the elevated host.
/// </summary>
public interface IOutputSink
{
    /// <summary>Adds <paramref name="text"/> written to <paramref name="stream"/>, unless the output is complete. Can be called from any thread.</summary>
    void Write(OutputStream stream, ReadOnlySpan<char> text);

    /// <summary>Ends the output, as nothing more will be written. Whatever is written after is ignored.</summary>
    void Complete();
}
