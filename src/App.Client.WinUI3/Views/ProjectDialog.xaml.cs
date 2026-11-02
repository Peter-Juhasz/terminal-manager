using App.Client.WinUI3.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace App.Client.WinUI3.Views;

/// <summary>
/// Adds a project, or edits one.
/// </summary>
public sealed partial class ProjectDialog : ContentDialog
{
    public ProjectDialog(ProjectEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        PageNavigation.SelectedItem = GeneralPageItem;
        Opened += (_, _) => DisplayNameBox.Focus(FocusState.Programmatic);
    }

    public ProjectEditorViewModel ViewModel { get; }

    private async void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        // Works unpackaged as well, as it is given the window rather than initialized with its handle.
        var picker = new FolderPicker(XamlRoot.ContentIslandEnvironment.AppWindowId)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };

        var workingFolder = Environment.ExpandEnvironmentVariables(ViewModel.WorkingFolder.Trim());
        if (Directory.Exists(workingFolder))
        {
            picker.SuggestedFolder = workingFolder;
        }

        if (await picker.PickSingleFolderAsync() is not { } result)
        {
            return;
        }

        ViewModel.WorkingFolder = result.Path;
        if (string.IsNullOrWhiteSpace(ViewModel.DisplayName))
        {
            ViewModel.DisplayName = Path.GetFileName(Path.TrimEndingDirectorySeparator(result.Path));
        }
    }
}
