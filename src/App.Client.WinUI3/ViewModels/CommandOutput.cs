using App.Client.WinUI3.Services;

namespace App.Client.WinUI3.ViewModels;

/// <summary>
/// What a command wrote, in the order it was read. It only ever grows, so what is shown of it is kept up to date by showing the chunks appended.
/// </summary>
/// <remarks>
/// The web addresses in it are looked for only once it is complete, as the end of one may yet be written until then,
/// and only when they are first asked for, so the output of the commands in the history isn't read until it is shown.
/// </remarks>
public sealed class CommandOutput
{
    /// <summary>The most web addresses shown as links, so output full of them is shown as quickly as any.</summary>
    public const int MaxLinks = 1000;

    private readonly List<OutputChunk> _chunks = [];
    private int _length;
    private IReadOnlyList<UrlMatch>? _links;

    /// <summary>The chunks of the output, none of which are empty.</summary>
    public IReadOnlyList<OutputChunk> Chunks => _chunks;

    public bool IsEmpty => _chunks.Count == 0;

    /// <summary>Whether the command ended, so nothing more is written, other than what tells why it couldn't be run.</summary>
    public bool IsCompleted { get; private set; }

    /// <summary>
    /// The web addresses in the output, in order, as they are counted in its text; none until it is complete.
    /// </summary>
    public IReadOnlyList<UrlMatch> Links => IsCompleted ? _links ??= UrlDetector.FindAll(ToString(), MaxLinks) : [];

    /// <summary>Raised when chunks were appended to <see cref="Chunks"/>.</summary>
    public event EventHandler? Appended;

    /// <summary>Raised when the output was completed, so its <see cref="Links"/> can be shown.</summary>
    public event EventHandler? Completed;

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
            _links = null;
            Appended?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Tells that nothing more will be written, so the web addresses in the output are known.</summary>
    public void Complete()
    {
        if (IsCompleted)
        {
            return;
        }

        IsCompleted = true;
        Completed?.Invoke(this, EventArgs.Empty);
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
