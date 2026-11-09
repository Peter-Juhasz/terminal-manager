using System.Text.Json;
using App.Client.WinUI3.Services;
using App.Domain;
using Microsoft.UI.Xaml.Controls;

namespace App.Client.WinUI3.Views;

public sealed partial class SettingsDialog : ContentDialog
{
    private readonly SettingsService _settingsService;

    public SettingsDialog(SettingsService settingsService)
    {
        _settingsService = settingsService;
        InitializeComponent();
        PageNavigation.SelectedItem = ViewPageItem;
        ThemeBox.SelectedIndex = (int)settingsService.Settings.Theme;
        WrapOutputSwitch.IsOn = settingsService.Settings.WrapOutput;
        ShowLineNumbersSwitch.IsOn = settingsService.Settings.ShowLineNumbers;
        MaxOutputHeightBox.Value = settingsService.Settings.MaxOutputHeight ?? double.NaN;
        ConfirmTemplateScriptsSwitch.IsOn = settingsService.Settings.ConfirmTemplateScripts;
    }

    private async void OnSaveClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        IsPrimaryButtonEnabled = false;
        IsEnabled = false;
        SaveError.IsOpen = false;
        try
        {
            await _settingsService.SaveAsync(_settingsService.Settings with
            {
                Theme = (AppTheme)ThemeBox.SelectedIndex,
                WrapOutput = WrapOutputSwitch.IsOn,
                ShowLineNumbers = ShowLineNumbersSwitch.IsOn,
                ConfirmTemplateScripts = ConfirmTemplateScriptsSwitch.IsOn,

                // an empty box is NaN
                MaxOutputHeight = double.IsNaN(MaxOutputHeightBox.Value) ? null : (int)Math.Round(MaxOutputHeightBox.Value),
            });
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
