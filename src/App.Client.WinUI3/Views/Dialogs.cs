using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace App.Client.WinUI3.Views;

public static class Dialogs
{
    private static bool s_isOpen;

    /// <summary>
    /// Shows <paramref name="dialog"/>, unless another one is open, as only one can be at a time; then <see langword="null"/> is returned.
    /// </summary>
    public static async Task<ContentDialogResult?> ShowAsync(ContentDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        if (s_isOpen)
        {
            return null;
        }

        s_isOpen = true;
        var root = dialog.XamlRoot?.Content as FrameworkElement;
        void UpdateTheme(FrameworkElement sender, object args) =>
            dialog.RequestedTheme = sender.ActualTheme;

        try
        {
            // Dialogs are hosted in a popup, outside the window's themed visual tree.
            if (root is not null)
            {
                dialog.RequestedTheme = root.ActualTheme;
                root.ActualThemeChanged += UpdateTheme;
            }

            return await dialog.ShowAsync();
        }
        finally
        {
            if (root is not null)
            {
                root.ActualThemeChanged -= UpdateTheme;
            }

            s_isOpen = false;
        }
    }
}
