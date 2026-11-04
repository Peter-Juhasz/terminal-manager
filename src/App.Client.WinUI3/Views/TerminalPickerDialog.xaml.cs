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
        Filter(query: string.Empty);
        Opened += (_, _) => SearchBox.Focus(FocusState.Programmatic);
    }

    /// <summary>The kind picked, or <see langword="null"/> when the dialog was cancelled.</summary>
    public ProjectKind? SelectedKind { get; private set; }

    // shows the kinds whose name or description has the query in it, those run on this machine first, then those run elsewhere
    private void Filter(string query)
    {
        var matches = ProjectKind.All
            .Where(kind => query is "" || kind.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) || kind.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Select(kind => new ProjectKindMatch(Kind: kind, Query: query))
            .ToList();
        ProjectKindGroup[] groups =
        [
            new(name: "Local", matches: matches.Where(static match => !match.Kind.IsRemote)),
            new(name: "Remote", matches: matches.Where(static match => match.Kind.IsRemote)),
        ];

        // a group with nothing found in it is left out, header and all
        KindsSource.Source = groups.Where(static group => group.Count > 0).ToList();

        // a new source makes a new view, which the grid is given again to show
        KindsView.ItemsSource = KindsSource.View;
        NoMatchesText.Visibility = matches.Count is 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => Filter(sender.Text.Trim());

    // enter picks the first terminal found, as the one the search was narrowed down to
    private void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (KindsSource.Source is not IReadOnlyList<ProjectKindGroup> { Count: > 0 } groups)
        {
            return;
        }

        Pick(groups[0][0].Kind);
    }

    private void OnKindClick(object sender, ItemClickEventArgs e) => Pick(((ProjectKindMatch)e.ClickedItem).Kind);

    private void Pick(ProjectKind kind)
    {
        SelectedKind = kind;
        Hide();
    }
}

/// <summary>A <see cref="ProjectKind"/> found by the search, with what was searched for, so it can be highlighted.</summary>
public sealed record class ProjectKindMatch(ProjectKind Kind, string Query);

/// <summary>
/// A group of <see cref="ProjectKindMatch"/>es under a header, which is itself the list of its kinds, as a grouped <see cref="Microsoft.UI.Xaml.Data.CollectionViewSource"/> takes it.
/// </summary>
public sealed class ProjectKindGroup(string name, IEnumerable<ProjectKindMatch> matches) : List<ProjectKindMatch>(matches)
{
    public string Name { get; } = name;
}
