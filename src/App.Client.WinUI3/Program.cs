using App.Client.WinUI3.Services;

namespace App.Client.WinUI3;

/// <summary>
/// Starts the app, or the elevated host, when the app started a copy of itself as an administrator to run commands.
/// </summary>
public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // The host has no window, so nothing of WinUI is loaded for it.
        if (args is [ElevatedHost.Argument, ..])
        {
            return ElevatedHost.Run(args.AsSpan(1));
        }

        XamlGeneratedProgram.XamlGeneratedMain();
        return 0;
    }
}
