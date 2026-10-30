namespace App.Client.WinUI3.Services;

public enum OutputStream
{
    StandardOutput,
    StandardError,
}

/// <summary>A piece of what a command wrote, in the order it was read.</summary>
public readonly record struct OutputChunk(OutputStream Stream, string Text);
