using System.Diagnostics;
using App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace App.Client.WinUI3.Controls;

/// <summary>Asks what is at a position in a text, such as the syntax of a command, or the type of a property.</summary>
public delegate Task<QuickInfo?> QuickInfoSource(string text, int position, CancellationToken cancellationToken);

/// <summary>
/// Shows what the token the mouse rests on in a text is, in a tooltip lined up with where the token starts, as an editor does.
/// </summary>
/// <remarks>
/// The tooltip is set on the element only while it is shown, so it isn't opened by the element being hovered anywhere else.
/// The text is asked for again once it is answered, and nothing is shown when it changed in the meantime.
/// </remarks>
internal sealed partial class QuickInfoHover : IDisposable
{
    // How long the mouse rests before the token under it is told about.
    private static readonly TimeSpan HoverDelay = TimeSpan.FromMilliseconds(400);

    private readonly FrameworkElement _element;
    private readonly Func<string> _getText;
    private readonly Func<Point, int?> _getIndexFromPoint;
    private readonly Func<int, Rect?> _getCharacterBounds;
    private readonly QuickInfoSource _source;
    private readonly DispatcherQueueTimer _timer;
    private Point _point;
    private CancellationTokenSource? _request;
    private ToolTip? _toolTip;
    private QuickInfo? _shown;

    private QuickInfoHover(FrameworkElement element, Func<string> getText, Func<Point, int?> getIndexFromPoint, Func<int, Rect?> getCharacterBounds, QuickInfoSource source)
    {
        _element = element;
        _getText = getText;
        _getIndexFromPoint = getIndexFromPoint;
        _getCharacterBounds = getCharacterBounds;
        _source = source;
        _timer = element.DispatcherQueue.CreateTimer();
        _timer.Interval = HoverDelay;
        _timer.IsRepeating = false;
        _timer.Tick += OnTimerTick;

        // the text controls handle the pointer themselves, for selecting text
        element.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPointerMoved), handledEventsToo: true);
        element.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerLeft), handledEventsToo: true);
        element.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(OnPointerLeft), handledEventsToo: true);
        element.PointerExited += OnPointerLeft;
        element.PointerCanceled += OnPointerLeft;
        element.Unloaded += (_, _) => Close();
    }

    /// <summary>Shows what the tokens of the script in <paramref name="block"/> are.</summary>
    public static QuickInfoHover Attach(RichTextBlock block, QuickInfoSource source)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(source);

        return new(block, block.GetPlainText, block.GetCharacterIndexFromPoint, block.GetCharacterBounds, source);
    }

    /// <summary>Shows what the tokens of the script typed in <paramref name="box"/> are; the tooltip is closed when it is typed in.</summary>
    public static QuickInfoHover Attach(TextBox box, QuickInfoSource source)
    {
        ArgumentNullException.ThrowIfNull(box);
        ArgumentNullException.ThrowIfNull(source);

        var hover = new QuickInfoHover(box, () => box.Text, box.GetCharacterIndexFromPoint, index => index < box.Text.Length ? box.GetCharacterBounds(index) : null, source);
        box.TextChanged += (_, _) => hover.Close();
        return hover;
    }

    /// <summary>Closes the tooltip, and forgets what was asked for.</summary>
    public void Close()
    {
        _timer.Stop();
        _request?.Cancel();
        _request?.Dispose();
        _request = null;
        _shown = null;
        if (_toolTip is not null)
        {
            _toolTip.IsOpen = false;
            ToolTipService.SetToolTip(_element, null);
            _toolTip = null;
        }
    }

    /// <summary>Closes the tooltip, and lets go of the request that is still asked for.</summary>
    public void Dispose() => Close();

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_element);
        if (e.Pointer.PointerDeviceType is PointerDeviceType.Touch || point.Properties.IsLeftButtonPressed)
        {
            Close();
            return;
        }

        _point = point.Position;

        // moving within the token told about keeps the tooltip
        if (_shown is { } shown && _getIndexFromPoint(_point) is { } index && index >= shown.Start && index < shown.Start + shown.Length)
        {
            return;
        }

        Close();
        _timer.Start();
    }

    private void OnPointerLeft(object sender, PointerRoutedEventArgs e) => Close();

    // Quick info is only ever a hint, so whatever goes wrong while it is asked for or shown, such as the element being let go in the meantime, leaves it unshown,
    // rather than ending the app, as an exception out of an async void handler would.
    private async void OnTimerTick(DispatcherQueueTimer sender, object args)
    {
        try
        {
            if (_getIndexFromPoint(_point) is not { } index)
            {
                return;
            }

            var text = _getText();
            _request?.Cancel();
            _request?.Dispose();
            var request = _request = new CancellationTokenSource();
            var info = await _source(text, index, request.Token);
            if (request.IsCancellationRequested || info is null || _getText() != text)
            {
                return;
            }

            Show(info);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Quick info couldn't be shown: {ex}");
        }
    }

    // Lined up with where the token starts, above the line it starts on.
    private void Show(QuickInfo info)
    {
        if (_getCharacterBounds(info.Start) is not { } bounds)
        {
            return;
        }

        var content = new TextBlock
        {
            Text = info.Text,
            FontFamily = (FontFamily)Application.Current.Resources["MonospaceFontFamily"],
            FontSize = GetResource("ToolTipContentThemeFontSize", 12d),
            TextWrapping = TextWrapping.Wrap,
        };

        // a tooltip is centered over the rectangle it is placed by, so it starts where the token does when the rectangle is as wide as the tooltip
        var padding = GetResource("ToolTipBorderThemePadding", new Thickness(9, 6, 9, 8));
        var border = GetResource("ToolTipBorderThemeThickness", new Thickness(1));
        var chrome = padding.Left + padding.Right + border.Left + border.Right;
        content.Measure(new(MaxToolTipWidth - chrome, double.PositiveInfinity));
        var width = content.DesiredSize.Width + chrome;
        var toolTip = _toolTip = new()
        {
            Content = content,
            MaxWidth = MaxToolTipWidth,
            Placement = PlacementMode.Top,
            PlacementRect = new Rect(bounds.X, bounds.Y, width, bounds.Height),
        };

        // the width it was laid out with is the one to go by, when the estimate missed it
        toolTip.Opened += (_, _) =>
        {
            if (Math.Abs(toolTip.ActualWidth - width) > 1)
            {
                toolTip.PlacementRect = new(bounds.X, bounds.Y, toolTip.ActualWidth, bounds.Height);
            }
        };
        ToolTipService.SetToolTip(_element, toolTip);
        toolTip.IsOpen = true;
        _shown = info;
    }

    private const double MaxToolTipWidth = 640;

    // a resource of the theme, as the app or the controls define it, or what it is by default
    private static T GetResource<T>(string key, T fallback) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is T resource ? resource : fallback;

}
