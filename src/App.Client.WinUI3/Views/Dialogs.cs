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
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            s_isOpen = false;
        }
    }
}
