using App.Client.WinUI3.ViewModels;
using App.Client.WinUI3.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace App.Client.WinUI3;

public sealed partial class MainWindow : Window
{
	// The size the window opens with, in view pixels.
	private const int DefaultWidth = 1200;
	private const int DefaultHeight = 800;

	public MainWindow(MainViewModel viewModel)
	{
		ViewModel = viewModel;
		InitializeComponent();

		ExtendsContentIntoTitleBar = true;
		SetTitleBar(AppTitleBar);
		AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

		var window = new HWND(Win32Interop.GetWindowFromWindowId(AppWindow.Id));
		var scale = PInvoke.GetDpiForWindow(window) / 96d;
		AppWindow.Resize(new SizeInt32((int)(DefaultWidth * scale), (int)(DefaultHeight * scale)));

		var taskbarProgress = new TaskbarProgress(window);
		ViewModel.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName is nameof(MainViewModel.OverallProgress))
			{
				taskbarProgress.Report(ViewModel.OverallProgress);
			}
		};

		Closed += (_, _) => ViewModel.StopAll();
	}

	public MainViewModel ViewModel { get; }

	private void OnPaneToggleRequested(TitleBar sender, object args) => NavView.IsPaneOpen = !NavView.IsPaneOpen;

	private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
	{
		if (args.SelectedItem is ProjectSessionViewModel session)
		{
			ViewModel.SelectedSession = session;
		}
	}

	private async void OnNavigationItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
	{
		if (args.InvokedItemContainer?.DataContext is NewProjectMenuItem)
		{
			await AddProjectAsync();
		}
	}

	private async void OnNewProjectClick(object sender, RoutedEventArgs e) => await AddProjectAsync();

	private async void OnEditProjectClick(object sender, RoutedEventArgs e)
	{
		if ((sender as FrameworkElement)?.Tag is not ProjectSessionViewModel session)
		{
			return;
		}

		var dialog = new ProjectDialog(new ProjectEditorViewModel(session.Project)) { XamlRoot = Content.XamlRoot };
		if (await Dialogs.ShowAsync(dialog) is ContentDialogResult.Primary)
		{
			await ViewModel.UpdateAsync(session, dialog.ViewModel.Build(), CancellationToken.None);
		}
	}

	private async void OnDeleteProjectClick(object sender, RoutedEventArgs e)
	{
		if ((sender as FrameworkElement)?.Tag is not ProjectSessionViewModel session)
		{
			return;
		}

		var dialog = new ContentDialog
		{
			XamlRoot = Content.XamlRoot,
			Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
			Title = "Delete project?",
			Content = $"'{session.Project.DisplayName}' is removed from the list. Its working folder isn't deleted.",
			PrimaryButtonText = "Delete",
			CloseButtonText = "Cancel",
			DefaultButton = ContentDialogButton.Close,
		};
		if (await Dialogs.ShowAsync(dialog) is ContentDialogResult.Primary)
		{
			await ViewModel.DeleteAsync(session, CancellationToken.None);
		}
	}

	private async Task AddProjectAsync()
	{
		var dialog = new ProjectDialog(new ProjectEditorViewModel()) { XamlRoot = Content.XamlRoot };
		if (await Dialogs.ShowAsync(dialog) is ContentDialogResult.Primary)
		{
			await ViewModel.AddAsync(dialog.ViewModel.Build(), CancellationToken.None);
		}
	}
}
