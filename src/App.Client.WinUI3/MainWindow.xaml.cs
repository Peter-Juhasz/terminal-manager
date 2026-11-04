using System.Runtime.InteropServices;
using App.Client.WinUI3.Models;
using App.Client.WinUI3.Services;
using App.Client.WinUI3.ViewModels;
using App.Client.WinUI3.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace App.Client.WinUI3;

public sealed partial class MainWindow : Window
{
	// The size the window opens with, in view pixels.
	private const int DefaultWidth = 1200;
	private const int DefaultHeight = 800;

	private readonly SettingsService _settingsService;

	public MainWindow(MainViewModel viewModel, SettingsService settingsService)
	{
		ViewModel = viewModel;
		_settingsService = settingsService;
		InitializeComponent();
		NavView.Loaded += (_, _) =>
		{
			// Opens a dialog rather than a page; its label would otherwise follow the system language, not the app's.
			var settingsItem = (NavigationViewItem)NavView.SettingsItem;
			settingsItem.SelectsOnInvoked = false;
			settingsItem.Content = "Settings";
			AutomationProperties.SetName(settingsItem, "Settings");
		};

		ExtendsContentIntoTitleBar = true;
		SetTitleBar(AppTitleBar);
		AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
		ApplySettings();

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

		ViewModel.CommandCompleted += (_, _) =>
		{
			if (!_isActive)
			{
				FlashTaskbar(window);
			}
		};
		Activated += (_, e) => _isActive = e.WindowActivationState is not WindowActivationState.Deactivated;

		Closed += (_, _) => ViewModel.StopAll();
	}

	// Whether the window has the focus, so a command that ends is noticed without flashing.
	private bool _isActive = true;

	/// <summary>Flashes the taskbar button of <paramref name="window"/> until it is brought to the foreground.</summary>
	private static void FlashTaskbar(HWND window)
	{
		var info = new FLASHWINFO
		{
			cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
			hwnd = window,
			dwFlags = FLASHWINFO_FLAGS.FLASHW_TRAY | FLASHWINFO_FLAGS.FLASHW_TIMERNOFG,
		};
		PInvoke.FlashWindowEx(in info);
	}

	public MainViewModel ViewModel { get; }

	private void ApplySettings()
	{
		ProjectView.WrapOutput = _settingsService.Settings.WrapOutput;
		ProjectView.MaxOutputHeight = _settingsService.Settings.MaxOutputHeight ?? double.PositiveInfinity;
		ProjectView.ConfirmTemplateScripts = _settingsService.Settings.ConfirmTemplateScripts;

		((FrameworkElement)Content).RequestedTheme = _settingsService.Settings.Theme switch
		{
			AppTheme.Dark => ElementTheme.Dark,
			AppTheme.Light => ElementTheme.Light,
			_ => ElementTheme.Default,
		};
		AppWindow.TitleBar.PreferredTheme = _settingsService.Settings.Theme switch
		{
			AppTheme.Dark => TitleBarTheme.Dark,
			AppTheme.Light => TitleBarTheme.Light,
			_ => TitleBarTheme.UseDefaultAppMode,
		};
	}

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
		if (args.IsSettingsInvoked)
		{
			var dialog = new SettingsDialog(_settingsService) { XamlRoot = Content.XamlRoot };
			if (await Dialogs.ShowAsync(dialog) is ContentDialogResult.Primary)
			{
				ApplySettings();
			}
		}
		else if (args.InvokedItemContainer?.DataContext is NewProjectMenuItem)
		{
			await AddProjectAsync();
		}
	}

	private async void OnNewProjectClick(object sender, RoutedEventArgs e) => await AddProjectAsync();

	private async void OnEditProjectClick(object sender, RoutedEventArgs e)
	{
		if ((sender as FrameworkElement)?.Tag is ProjectSessionViewModel session)
		{
			await EditProjectAsync(session);
		}
	}

	private async void OnEditProjectRequested(object? sender, ProjectSessionViewModel session) => await EditProjectAsync(session);

	private async Task EditProjectAsync(ProjectSessionViewModel session)
	{
		var dialog = new ProjectDialog(new ProjectEditorViewModel(session.Project)) { XamlRoot = Content.XamlRoot };
		if (await Dialogs.ShowAsync(dialog) is ContentDialogResult.Primary)
		{
			await ViewModel.UpdateAsync(session, dialog.ViewModel.Build(), CancellationToken.None);
		}
	}

	private async void OnDuplicateProjectClick(object sender, RoutedEventArgs e)
	{
		if ((sender as FrameworkElement)?.Tag is ProjectSessionViewModel session)
		{
			await ViewModel.DuplicateAsync(session, CancellationToken.None);
		}
	}

	private async void OnDeleteProjectClick(object sender, RoutedEventArgs e)
	{
		if ((sender as FrameworkElement)?.Tag is ProjectSessionViewModel session)
		{
			await DeleteProjectAsync(session);
		}
	}

	private async void OnDeleteProjectRequested(object? sender, ProjectSessionViewModel session) => await DeleteProjectAsync(session);

	private async Task DeleteProjectAsync(ProjectSessionViewModel session)
	{
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

	// A folder dropped on the pane is added as a project, from the dialog filled in with it.
	private async void OnNavigationDragOver(object sender, DragEventArgs e)
	{
		if (!IsOverPane(e))
		{
			return;
		}

		var deferral = e.GetDeferral();
		try
		{
			if (await GetDroppedFolderAsync(e.DataView) is not null)
			{
				e.AcceptedOperation = DataPackageOperation.Link;
				e.DragUIOverride.Caption = "New project";
			}
		}
		finally
		{
			deferral.Complete();
		}
	}

	private async void OnNavigationDrop(object sender, DragEventArgs e)
	{
		if (!IsOverPane(e))
		{
			return;
		}

		// the drop is completed before the dialog is shown, so the source isn't kept waiting on it
		var deferral = e.GetDeferral();
		string? folder;
		try
		{
			folder = await GetDroppedFolderAsync(e.DataView);
		}
		finally
		{
			deferral.Complete();
		}

		if (folder is null)
		{
			return;
		}

		await AddProjectAsync(new ProjectEditorViewModel
		{
			WorkingFolder = folder,
			DisplayName = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)),
		});
	}

	// The content of the navigation view is the project, which isn't a place to drop on.
	private bool IsOverPane(DragEventArgs e) =>
		e.GetPosition(NavView).X <= (NavView.IsPaneOpen ? NavView.OpenPaneLength : NavView.CompactPaneLength);

	/// <summary>The folder dropped from the file explorer, or the path of one dropped as text.</summary>
	private static async Task<string?> GetDroppedFolderAsync(DataPackageView data)
	{
		if (data.Contains(StandardDataFormats.StorageItems))
		{
			var items = await data.GetStorageItemsAsync();
			return items.OfType<StorageFolder>().FirstOrDefault()?.Path;
		}

		if (data.Contains(StandardDataFormats.Text))
		{
			var text = (await data.GetTextAsync()).Trim().Trim('"');
			return Directory.Exists(Environment.ExpandEnvironmentVariables(text)) ? text : null;
		}

		return null;
	}

	private async Task AddProjectAsync() => await AddProjectAsync(new ProjectEditorViewModel());

	private async Task AddProjectAsync(ProjectEditorViewModel editor)
	{
		// the terminal is picked first, as what else is filled in depends on it
		var picker = new TerminalPickerDialog { XamlRoot = Content.XamlRoot };
		await Dialogs.ShowAsync(picker);
		if (picker.SelectedKind is not { } kind)
		{
			return;
		}

		editor.Kind = kind;
		var dialog = new ProjectDialog(editor) { XamlRoot = Content.XamlRoot };
		if (await Dialogs.ShowAsync(dialog) is ContentDialogResult.Primary)
		{
			await ViewModel.AddAsync(dialog.ViewModel.Build(), CancellationToken.None);
		}
	}
}
