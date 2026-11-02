using App.Client.WinUI3.Models;
using App.Client.WinUI3.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace App.Client.WinUI3.ViewModels;

/// <summary>
/// The values filled in for the variables of a script template, before it is run.
/// </summary>
public sealed class ScriptTemplateViewModel
{
    private readonly SavedScript _script;

    /// <param name="primaryButtonText">What is done with the script once filled in, such as Run.</param>
    public ScriptTemplateViewModel(SavedScript script, string primaryButtonText)
    {
        ArgumentNullException.ThrowIfNull(script);

        _script = script;
        PrimaryButtonText = primaryButtonText;
        Variables = [.. ScriptTemplate.GetVariables(script.Command).Select(variable => new TemplateVariableViewModel(variable.Name) { Value = variable.DefaultValue ?? string.Empty })];
    }

    public string Title => _script.Name;

    public string PrimaryButtonText { get; }

    public TemplateVariableViewModel[] Variables { get; }

    /// <summary>The script with its variables replaced by the values filled in.</summary>
    public SavedScript Build() => _script with
    {
        Command = ScriptTemplate.Expand(_script.Command, Variables.ToDictionary(variable => variable.Name, variable => variable.Value, StringComparer.Ordinal)),
        IsTemplate = false,
    };
}

/// <summary>
/// A variable of a script template, and the value filled in for it.
/// </summary>
public sealed partial class TemplateVariableViewModel(string name) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    public partial string Value { get; set; } = string.Empty;
}
