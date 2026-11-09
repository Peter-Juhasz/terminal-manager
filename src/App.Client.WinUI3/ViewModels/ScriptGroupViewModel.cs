using System.Collections.ObjectModel;
using App.Client.WinUI3.Models;
using App.Client.WinUI3.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace App.Client.WinUI3.ViewModels;

/// <summary>
/// The scripts from one place, shown together, which can be collapsed: those saved to the project, which can be changed,
/// or those read from a file in its working folder, which can't be changed in the app, but are shown as they are when the file changes.
/// </summary>
/// <param name="key">What tells the group apart from the others of the project, which it is kept by when its scripts change.</param>
/// <param name="displayName">What the scripts are shown under.</param>
/// <param name="filePath">The file the scripts are read from, or <see langword="null"/> for those saved to the project.</param>
public sealed partial class ScriptGroupViewModel(string key, string displayName, string? filePath) : ObservableObject
{
    /// <summary>The key of the group of the scripts saved to the project.</summary>
    public const string ProjectKey = "";

    public string Key { get; } = key;

    public string DisplayName { get; } = displayName;

    /// <summary>The file the scripts are read from, or <see langword="null"/> for those saved to the project.</summary>
    public string? FilePath { get; } = filePath;

    /// <summary>Whether the scripts are read from a file, so they can't be changed or deleted in the app.</summary>
    public bool IsReadOnly => FilePath is not null;

    /// <summary>All the scripts, in the order they are shown.</summary>
    public IReadOnlyList<Script> Scripts { get; private set; } = [];

    /// <summary>The scripts that match the search, in the order they are shown.</summary>
    public ObservableCollection<ScriptItem> FilteredScripts { get; } = [];

    public int Count => Scripts.Count;

    /// <summary>Why the scripts couldn't be read from the file, or <see langword="null"/> when they could be.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(IsErrorVisible))]
    public partial string? Error { get; private set; }

    public bool HasError => Error is not null;

    /// <summary>Whether the error is shown, which it is unless the scripts are searched, as it can't match.</summary>
    public bool IsErrorVisible => HasError && !IsSearching && IsExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContentVisible), nameof(IsErrorVisible), nameof(ExpandGlyph), nameof(ExpandLabel))]
    public partial bool IsExpanded { get; set; } = true;

    /// <summary>Whether the scripts are searched, so all the groups with any that match are shown expanded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContentVisible), nameof(IsErrorVisible))]
    public partial bool IsSearching { get; private set; }

    public bool IsContentVisible => IsExpanded || IsSearching;

    /// <summary>Whether the group is shown, which it is when it has scripts that match the search, or an error while there is none.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; private set; }

    /// <summary>Whether the name of the group is shown above its scripts, which it isn't when only those saved to the project are shown, as they always were.</summary>
    [ObservableProperty]
    public partial bool IsHeaderVisible { get; set; }

    public string ExpandGlyph => IsExpanded ? "" : "";

    public string ExpandLabel => IsExpanded ? $"Collapse {DisplayName}" : $"Expand {DisplayName}";

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    /// <summary>Shows <paramref name="scripts"/>, or <paramref name="error"/> when they couldn't be read, with those that match <paramref name="search"/>.</summary>
    public void Update(IReadOnlyList<Script> scripts, string? error, TextSearch? search)
    {
        ArgumentNullException.ThrowIfNull(scripts);

        // when another file changed, the scripts shown are left as they are; a change of the search filters them by itself
        if (error == Error && scripts.SequenceEqual(Scripts))
        {
            return;
        }

        Scripts = scripts;
        Error = error;
        Filter(search);
    }

    /// <summary>Shows the scripts that match <paramref name="search"/>, in their names, commands and descriptions, or all of them when it is <see langword="null"/>.</summary>
    public void Filter(TextSearch? search)
    {
        FilteredScripts.Clear();
        foreach (var script in Scripts.Where(script => search is null || search.IsMatch(script.Name) || search.IsMatch(script.Command) || (script.Description is { } description && search.IsMatch(description))))
        {
            FilteredScripts.Add(new(script, this));
        }

        IsSearching = search is not null;
        IsVisible = FilteredScripts.Count > 0 || (HasError && search is null);
    }
}

/// <summary>
/// A script as it is shown, with the group it is shown in, which tells whether it can be changed.
/// </summary>
public sealed partial record class ScriptItem(Script Script, ScriptGroupViewModel Group);
