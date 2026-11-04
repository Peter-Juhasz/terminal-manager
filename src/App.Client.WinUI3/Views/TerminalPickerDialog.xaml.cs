using App.Client.WinUI3.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace App.Client.WinUI3.Views;

/// <summary>
/// The first step of adding a project, where the terminal its commands are run by is picked, before the rest is filled in by <see cref="ProjectDialog"/>.
/// </summary>
public sealed partial class TerminalPickerDialog : ContentDialog
{
    public TerminalPickerDialog()
    {
        InitializeComponent();
        Opened += (_, _) => KindsView.Focus(FocusState.Programmatic);
    }

    public IReadOnlyList<ProjectKind> Kinds => ProjectKind.All;

    /// <summary>The kind picked, or <see langword="null"/> when the dialog was cancelled.</summary>
    public ProjectKind? SelectedKind { get; private set; }

    private void OnKindClick(object sender, ItemClickEventArgs e)
    {
        SelectedKind = (ProjectKind)e.ClickedItem;
        Hide();
    }
}
