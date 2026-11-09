using System.Text.Json.Serialization;

namespace App.Domain;

// saved by name in the history, so it reads the same if more are added
[JsonConverter(typeof(JsonStringEnumConverter<OutputStream>))]
public enum OutputStream
{
    StandardOutput,
    StandardError,
}

/// <summary>A piece of what a command wrote, in the order it was read.</summary>
public readonly record struct OutputChunk(OutputStream Stream, string Text);
