using App.Client.WinUI3.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace App.Client.WinUI3.Views;

/// <summary>
/// Saves a script to a project, or edits one.
/// </summary>
public sealed partial class ScriptDialog : ContentDialog
{
    public ScriptDialog(ScriptEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Opened += (_, _) => NameBox.Focus(FocusState.Programmatic);
    }

    public ScriptEditorViewModel ViewModel { get; }
}
