using System.ComponentModel;
using App.Client.WinUI3.ViewModels;
using App.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace App.Client.WinUI3.Views;

/// <summary>
/// Shows the outputs of two commands side by side, in a window of its own, so it can be as large as the screen, and kept open beside the main one.
/// </summary>
public sealed partial class OutputComparisonWindow : Window
{
    // The size the window opens with, in view pixels.
    private const int DefaultWidth = 1400;
    private const int DefaultHeight = 860;

    // the row of the change moved to last, from which the next and the previous one are found
    private int _currentRow = -1;

    /// <summary>Opens the comparison in <paramref name="theme"/>, the theme of the window it was opened from.</summary>
    public OutputComparisonWindow(OutputComparisonViewModel viewModel, ElementTheme theme)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ViewModel = viewModel;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(WindowTitleBar);
        Title = WindowTitleBar.Title;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        Root.RequestedTheme = theme;
        AppWindow.TitleBar.PreferredTheme = theme switch
        {
            ElementTheme.Dark => TitleBarTheme.Dark,
            ElementTheme.Light => TitleBarTheme.Light,
            _ => TitleBarTheme.UseDefaultAppMode,
        };

        var window = new HWND(Win32Interop.GetWindowFromWindowId(AppWindow.Id));
        var scale = PInvoke.GetDpiForWindow(window) / 96d;
        AppWindow.Resize(new SizeInt32((int)(DefaultWidth * scale), (int)(DefaultHeight * scale)));

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) =>
        {
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.Dispose();
        };
    }

    public OutputComparisonViewModel ViewModel { get; }

    /// <summary>Shows the window, and compares the outputs.</summary>
    public async Task ShowAsync()
    {
        Activate();
        await ViewModel.CompareAsync();
    }

    // the first change is shown once the outputs are compared, and again each time they are compared with other options
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(OutputComparisonViewModel.Rows))
        {
            return;
        }

        // given as a list, so the source generator of CsWinRT makes it something WinRT can take; given as any other type, it can't
        RowsRepeater.ItemsSource = ViewModel.Rows switch
        {
            List<DiffRow> list => list,
            var rows => rows.ToList(),
        };

        _currentRow = -1;
        RowsScroller.ChangeView(horizontalOffset: null, verticalOffset: 0, zoomFactor: null, disableAnimation: true);
        if (ViewModel.FindNextChange(_currentRow) is { } first)
        {
            // laid out once the repeater took the new rows
            DispatcherQueue.TryEnqueue(() => ShowRow(first));
        }
    }

    private void OnNextChangeClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.FindNextChange(_currentRow) is { } next)
        {
            ShowRow(next);
        }
    }

    private void OnPreviousChangeClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.FindPreviousChange(_currentRow is -1 ? int.MaxValue : _currentRow) is { } previous)
        {
            ShowRow(previous);
        }
    }

    // a little below the top, so the lines before the change are in view too
    private void ShowRow(int index)
    {
        // the repeater may not have taken the rows yet, before the window is loaded
        if (RowsRepeater.ItemsSourceView is not { Count: var count } || index >= count)
        {
            return;
        }

        _currentRow = index;
        RowsRepeater.GetOrCreateElement(index).StartBringIntoView(new BringIntoViewOptions
        {
            VerticalAlignmentRatio = 0.25,
            AnimationDesired = true,
        });
    }
}
