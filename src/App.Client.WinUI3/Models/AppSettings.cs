namespace App.Client.WinUI3.Models;

/// <param name="WrapOutput">Whether the long lines a command writes are wrapped, rather than scrolled horizontally.</param>
public sealed record AppSettings(AppTheme Theme = AppTheme.System, bool WrapOutput = true);
