using App.Client.WinUI3.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace App.Client.WinUI3.ViewModels;

/// <summary>
/// What is filled in to save a script, or to edit one.
/// </summary>
public sealed partial class ScriptEditorViewModel : ObservableObject
{
    private readonly SavedScript? _script;

    /// <summary>Starts saving <paramref name="command"/> as a new script.</summary>
    public ScriptEditorViewModel(string command)
    {
        Command = command;
    }

    /// <summary>Starts editing <paramref name="script"/>.</summary>
    public ScriptEditorViewModel(SavedScript script)
    {
        ArgumentNullException.ThrowIfNull(script);

        _script = script;
        Name = script.Name;
        Command = script.Command;
    }

    public bool IsNew => _script is null;

    public string Title => IsNew ? "Save script" : "Edit script";

    public string PrimaryButtonText => "Save";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    public partial string Command { get; set; } = string.Empty;

    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Command);

    public SavedScript Build() => _script switch
    {
        null => new SavedScript { Id = Guid.NewGuid(), Name = Name.Trim(), Command = Command.Trim() },
        { } script => script with { Name = Name.Trim(), Command = Command.Trim() },
    };
}
