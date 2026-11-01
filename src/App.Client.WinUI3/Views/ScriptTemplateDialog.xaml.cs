using App.Client.WinUI3.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace App.Client.WinUI3.Views;

/// <summary>
/// Asks for the values of the variables of a script template, before it is run.
/// </summary>
public sealed partial class ScriptTemplateDialog : ContentDialog
{
    private TextBox? _firstBox;
    private bool _isOpened;

    public ScriptTemplateDialog(ScriptTemplateViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Opened += (_, _) =>
        {
            _isOpened = true;
            FocusFirstBox();
        };
    }

    public ScriptTemplateViewModel ViewModel { get; }

    // the boxes may be loaded only after the dialog is opened, so whichever comes last focuses the first one
    private void OnVariableBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: TemplateVariableViewModel variable } box || ViewModel.Variables is not [var first, ..] || variable != first)
        {
            return;
        }

        _firstBox = box;
        FocusFirstBox();
    }

    private void FocusFirstBox()
    {
        if (!_isOpened || _firstBox is null)
        {
            return;
        }

        _firstBox.Focus(FocusState.Programmatic);
        _firstBox.SelectAll();
    }
}
