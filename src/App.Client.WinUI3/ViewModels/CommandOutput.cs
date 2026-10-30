using App.Client.WinUI3.Services;

namespace App.Client.WinUI3.ViewModels;

/// <summary>
/// What a command wrote, in the order it was read. It only ever grows, so what is shown of it is kept up to date by showing the chunks appended.
/// </summary>
public sealed class CommandOutput
{
    private readonly List<OutputChunk> _chunks = [];
    private int _length;

    /// <summary>The chunks of the output, none of which are empty.</summary>
    public IReadOnlyList<OutputChunk> Chunks => _chunks;

    public bool IsEmpty => _chunks.Count == 0;

    /// <summary>Raised when chunks were appended to <see cref="Chunks"/>.</summary>
    public event EventHandler? Appended;

    public void Append(IReadOnlyList<OutputChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        var count = _chunks.Count;
        for (var index = 0; index < chunks.Count; index++)
        {
            var chunk = chunks[index];
            if (!string.IsNullOrEmpty(chunk.Text))
            {
                _chunks.Add(chunk);
                _length += chunk.Text.Length;
            }
        }

        if (_chunks.Count > count)
        {
            Appended?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Returns all of the output, as a single text.</summary>
    public override string ToString() => string.Create(_length, _chunks, static (destination, chunks) =>
    {
        foreach (var chunk in chunks)
        {
            chunk.Text.CopyTo(destination);
            destination = destination[chunk.Text.Length..];
        }
    });
}
