using App.Client.WinUI3.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace App.Client.WinUI3.ViewModels;

/// <summary>
/// A run of output written to the same stream, which grows as more is written to it.
/// </summary>
public sealed partial class OutputSegment(OutputStream stream, string text) : ObservableObject
{
    public OutputStream Stream { get; } = stream;

    public bool IsError => Stream is OutputStream.StandardError;

    [ObservableProperty]
    public partial string Text { get; private set; } = text;

    public void Append(string text) => Text = string.Concat(Text, text);
}
