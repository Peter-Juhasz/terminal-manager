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

    /// <summary>The kinds, those run on this machine first, then those run elsewhere.</summary>
    public IReadOnlyList<ProjectKindGroup> KindGroups { get; } = (ProjectKindGroup[])
    [
        new(name: "Local", kinds: ProjectKind.All.Where(static kind => !kind.IsRemote)),
        new(name: "Remote", kinds: ProjectKind.All.Where(static kind => kind.IsRemote)),
    ];

    /// <summary>The kind picked, or <see langword="null"/> when the dialog was cancelled.</summary>
    public ProjectKind? SelectedKind { get; private set; }

    private void OnKindClick(object sender, ItemClickEventArgs e)
    {
        SelectedKind = (ProjectKind)e.ClickedItem;
        Hide();
    }
}

/// <summary>
/// A group of <see cref="ProjectKind"/>s under a header, which is itself the list of its kinds, as a grouped <see cref="Microsoft.UI.Xaml.Data.CollectionViewSource"/> takes it.
/// </summary>
public sealed class ProjectKindGroup(string name, IEnumerable<ProjectKind> kinds) : List<ProjectKind>(kinds)
{
    public string Name { get; } = name;
}
