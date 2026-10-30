using System.Text.Json;
using App.Client.WinUI3.Models;
using App.Client.WinUI3.Services;
using Microsoft.UI.Xaml.Controls;

namespace App.Client.WinUI3.Views;

public sealed partial class SettingsDialog : ContentDialog
{
    private readonly SettingsService _settingsService;

    public SettingsDialog(SettingsService settingsService)
    {
        _settingsService = settingsService;
        InitializeComponent();
        ThemeBox.SelectedIndex = (int)settingsService.Settings.Theme;
    }

    private async void OnSaveClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        IsPrimaryButtonEnabled = false;
        IsEnabled = false;
        SaveError.IsOpen = false;
        try
        {
            await _settingsService.SaveAsync(_settingsService.Settings with { Theme = (AppTheme)ThemeBox.SelectedIndex });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            args.Cancel = true;
            SaveError.Message = $"Your changes haven't been applied. {ex.Message}";
            SaveError.IsOpen = true;
        }
        finally
        {
            IsEnabled = true;
            IsPrimaryButtonEnabled = true;
            deferral.Complete();
        }
    }
}
