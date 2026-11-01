using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace App.Client.WinUI3.ViewModels;

/// <summary>
/// An environment variable of a project, as it is filled in; it is removed from the project by <paramref name="remove"/>.
/// </summary>
public sealed partial class EnvironmentVariableEditorViewModel(Action<EnvironmentVariableEditorViewModel> remove) : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;

    /// <summary>Whether nothing was filled in, so it is left out of the project.</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name) && string.IsNullOrEmpty(Value);

    [RelayCommand]
    private void Remove() => remove(this);
}
