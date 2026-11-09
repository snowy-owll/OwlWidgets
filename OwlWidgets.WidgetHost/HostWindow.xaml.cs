using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using OwlWidgets.Contracts;

namespace OwlWidgets.WidgetHost;

public partial class HostWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExAppWindow = 0x00040000;
    private const int WmWindowPosChanging = 0x0046;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpHideWindow = 0x0080;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint ZOrderFlags = SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder;
    private const int WsExTopmost = 0x00000008;

    private static readonly IntPtr HwndBottom = new(1);
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);

    // The same colors as the manager's App.xaml.
    private static readonly Color PrimaryColor = Color.FromRgb(0x3D, 0x6A, 0x9E);
    private static readonly Color SecondaryColor = Color.FromRgb(0x43, 0xA0, 0x47);
    private static readonly Uri MaterialDefaults =
        new("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml");

    /// <summary>
    /// A transparent margin between the window edge and the card. An effect such as a drop shadow is drawn
    /// outside the layout bounds of the view, and a window sized to the card alone would cut it off.
    /// Positions on the channel are those of the card, so the margin does not move a card on screen.
    /// </summary>
    private static readonly Thickness ShadowMargin = new(16);

    /// <summary>How long the size stays the same before it is reported: an animation sends no size per frame.</summary>
    private static readonly TimeSpan SizeSettleDelay = TimeSpan.FromSeconds(1);

    private readonly DispatcherTimer _sizeSettle;
    private bool _anchorKept;
    private ResourceDictionary? _material;
    private bool _dark;
    private bool _closing;
    private bool _allowZOrder;
    private bool _topmost;
    private bool _moveMode;
    private double _moveOriginX;
    private double _moveOriginY;

    public HostWindow()
    {
        InitializeComponent();
        Card.Margin = ShadowMargin;
        _sizeSettle = new DispatcherTimer { Interval = SizeSettleDelay };
        _sizeSettle.Tick += OnSizeSettled;
        SizeChanged += OnSizeChanged;
        // From the first frame on, a change of size keeps the anchor corner in place; the size before it was a guess.
        ContentRendered += (_, _) => _anchorKept = true;
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
        };
    }

    public IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    public bool MoveMode => _moveMode;

    /// <summary>
    /// The corner of the card that positions are given for and that stays in place when the card changes its size.
    /// Changing it does not move the card.
    /// </summary>
    public CardCorner Anchor { get; set; }

    /// <summary>The position of the <see cref="Anchor"/> corner of the card, not of the window.</summary>
    public (double X, double Y) AnchorPosition
    {
        get
        {
            var (width, height) = CardSize;
            return (CardLeft + (Anchor.IsRight() ? width : 0), CardTop + (Anchor.IsBottom() ? height : 0));
        }
    }

    /// <summary>The card without the transparent margin: the window as laid out, or as measured before it is shown.</summary>
    public (double Width, double Height) CardSize
    {
        get
        {
            var size = ActualWidth > 0 ? new Size(ActualWidth, ActualHeight) : MeasureWindow();
            return (Math.Max(0, size.Width - ShadowMargin.Left - ShadowMargin.Right),
                Math.Max(0, size.Height - ShadowMargin.Top - ShadowMargin.Bottom));
        }
    }

    /// <summary>The left edge of the card, not of the window: the window is wider by <see cref="ShadowMargin"/>.</summary>
    private double CardLeft
    {
        get => Left + ShadowMargin.Left;
        set => Left = value - ShadowMargin.Left;
    }

    /// <summary>The top edge of the card, not of the window.</summary>
    private double CardTop
    {
        get => Top + ShadowMargin.Top;
        set => Top = value - ShadowMargin.Top;
    }

    public event Action<double, double>? PlacementConfirmed;

    /// <summary>The size of the card has not changed for a while: an animation or a new layout has settled.</summary>
    public event Action<double, double>? CardSizeSettled;

    public event Action? PlacementCancelled;

    public event Action? MoveModeEntered;

    public void SetView(FrameworkElement? view) => Root.Content = view;

    /// <summary>Puts the <see cref="Anchor"/> corner of the card at the point; before the window is shown, by its measured size.</summary>
    public void SetAnchorPosition(double x, double y)
    {
        var (width, height) = CardSize;
        CardLeft = x - (Anchor.IsRight() ? width : 0);
        CardTop = y - (Anchor.IsBottom() ? height : 0);
    }

    /// <summary>
    /// Takes another corner as the anchor without moving the card and gives its position in whole units,
    /// as the settings screen enters them; the card moves by less than a unit to it.
    /// </summary>
    public (double X, double Y) ChangeAnchor(CardCorner anchor)
    {
        Anchor = anchor;
        var position = RoundedAnchorPosition();
        SetAnchorPosition(position.X, position.Y);
        return position;
    }

    private (double X, double Y) RoundedAnchorPosition()
    {
        var (x, y) = AnchorPosition;
        return (Math.Round(x, MidpointRounding.AwayFromZero), Math.Round(y, MidpointRounding.AwayFromZero));
    }

    /// <summary>The size the window takes for its content, before it has a size of its own.</summary>
    private Size MeasureWindow()
    {
        var content = (FrameworkElement)Content;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return content.DesiredSize;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _sizeSettle.Stop();
        _sizeSettle.Start();
    }

    private void OnSizeSettled(object? sender, EventArgs e)
    {
        _sizeSettle.Stop();
        var (width, height) = CardSize;
        CardSizeSettled?.Invoke(width, height);
    }

    /// <summary>
    /// The move frame always uses Material Design in the given base theme. With <paramref name="inheritMaterialDesign"/>
    /// the theme is also merged into the application resources of this process, so it reaches the widget view,
    /// its popups and <c>StaticResource</c> lookups made while the view is being built.
    /// </summary>
    public void ApplyTheme(bool dark, bool inheritMaterialDesign)
    {
        _dark = dark;
        if (_material != null)
            _material.MergedDictionaries[0] = CreateColorTheme(dark);

        var applicationResources = Application.Current.Resources.MergedDictionaries;
        if (inheritMaterialDesign)
        {
            var material = EnsureMaterial();
            if (!applicationResources.Contains(material))
                applicationResources.Add(material);
            Root.SetResourceReference(TextElement.ForegroundProperty, "MaterialDesign.Brush.Foreground");
            Root.SetResourceReference(TextElement.FontFamilyProperty, "MaterialDesignFont");
        }
        else if (_material != null && applicationResources.Remove(_material))
        {
            Root.ClearValue(TextElement.ForegroundProperty);
            Root.ClearValue(TextElement.FontFamilyProperty);
        }
    }

    /// <summary>
    /// Above all windows, games in a borderless window included. Meanwhile <see cref="PlaceBelow"/> is ignored;
    /// going back, the window stays where it is until the manager places it again.
    /// </summary>
    public void SetTopmost(bool topmost)
    {
        _topmost = topmost;
        if (Hwnd == IntPtr.Zero)
            return;

        _allowZOrder = true;
        try
        {
            SetWindowPos(Hwnd, topmost ? HwndTopmost : HwndNotTopmost, 0, 0, 0, 0, ZOrderFlags);
        }
        finally
        {
            _allowZOrder = false;
        }
    }

    public void PlaceBelow(IntPtr cover)
    {
        if (Hwnd == IntPtr.Zero || _topmost)
            return;

        _allowZOrder = true;
        try
        {
            if (cover == HwndBottom || cover == IntPtr.Zero)
            {
                SetWindowPos(Hwnd, HwndNotTopmost, 0, 0, 0, 0, ZOrderFlags);
                return;
            }

            var coverIsTopmost = (GetWindowLong(cover, GwlExStyle) & WsExTopmost) != 0;
            SetWindowPos(Hwnd, coverIsTopmost ? HwndTopmost : HwndNotTopmost, 0, 0, 0, 0, ZOrderFlags);
            SetWindowPos(Hwnd, cover, 0, 0, 0, 0, ZOrderFlags);
        }
        finally
        {
            _allowZOrder = false;
        }
    }

    public void EnterMoveMode()
    {
        if (_moveMode || _closing)
            return;

        (_moveOriginX, _moveOriginY) = AnchorPosition;
        _moveMode = true;
        var material = EnsureMaterial();
        if (!MoveOverlay.Resources.MergedDictionaries.Contains(material))
            MoveOverlay.Resources.MergedDictionaries.Add(material);
        MoveOverlay.Visibility = Visibility.Visible;
        MoveModeEntered?.Invoke();
    }

    public void CancelMoveMode() => EndMoveMode(save: false);

    public void ConfirmMoveMode() => EndMoveMode(save: true);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = Hwnd;
        var exStyle = GetWindowLong(hwnd, GwlExStyle);
        exStyle &= ~WsExAppWindow;
        exStyle |= WsExToolWindow;
        SetWindowLong(hwnd, GwlExStyle, exStyle);
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        base.OnClosing(e);
    }

    private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsInsideButton(source))
            return;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => EndMoveMode(save: true);

    private void Cancel_Click(object sender, RoutedEventArgs e) => EndMoveMode(save: false);

    private void EndMoveMode(bool save)
    {
        if (!_moveMode)
            return;

        _moveMode = false;
        MoveOverlay.Visibility = Visibility.Collapsed;
        if (!save)
        {
            // By the anchor, not by the window: the card may have changed its size meanwhile.
            SetAnchorPosition(_moveOriginX, _moveOriginY);
            PlacementCancelled?.Invoke();
            return;
        }

        // Dragging leaves fractions; the card settles on whole units, as the settings screen enters them.
        var (x, y) = RoundedAnchorPosition();
        SetAnchorPosition(x, y);
        PlacementConfirmed?.Invoke(x, y);
    }

    /// <summary>
    /// Loaded on first use: most wrappers never show the frame and do not inherit the theme.
    /// </summary>
    private ResourceDictionary EnsureMaterial()
    {
        if (_material != null)
            return _material;
        _material = new ResourceDictionary();
        _material.MergedDictionaries.Add(CreateColorTheme(_dark));
        _material.MergedDictionaries.Add(new ResourceDictionary { Source = MaterialDefaults });
        return _material;
    }

    private static CustomColorTheme CreateColorTheme(bool dark) => new()
    {
        BaseTheme = dark ? BaseTheme.Dark : BaseTheme.Light,
        PrimaryColor = PrimaryColor,
        SecondaryColor = SecondaryColor
    };

    private static bool IsInsideButton(DependencyObject source)
    {
        while (source != null)
        {
            if (source is System.Windows.Controls.Button)
                return true;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmWindowPosChanging || _closing)
            return IntPtr.Zero;

        var position = Marshal.PtrToStructure<WindowPos>(lParam);
        KeepAnchor(hwnd, ref position);
        if (!_allowZOrder)
            position.flags |= SwpNoZOrder;
        if ((position.flags & SwpHideWindow) != 0)
            position.flags &= ~SwpHideWindow;
        if ((position.flags & SwpNoMove) == 0 && (position.x <= -32000 || position.y <= -32000))
            position.flags |= SwpNoMove;
        Marshal.StructureToPtr(position, lParam, false);
        return IntPtr.Zero;
    }

    /// <summary>
    /// A change of size alone, as <c>SizeToContent</c> makes it when the card grows or shrinks, would keep the top left
    /// corner of the window. For another anchor the same call moves the window too, so the anchor corner stays put
    /// in the same frame, without a frame at the wrong place in between. Moves of their own (the position, the drag,
    /// a change of DPI) are left as they are. In device pixels, as the message is.
    /// </summary>
    private void KeepAnchor(IntPtr hwnd, ref WindowPos position)
    {
        if (!_anchorKept || Anchor == CardCorner.TopLeft || (position.flags & SwpNoSize) != 0 || !GetWindowRect(hwnd, out var rect))
            return;
        var moves = (position.flags & SwpNoMove) == 0 && (position.x != rect.Left || position.y != rect.Top);
        if (moves || (position.cx == rect.Right - rect.Left && position.cy == rect.Bottom - rect.Top))
            return;
        position.x = Anchor.IsRight() ? rect.Right - position.cx : rect.Left;
        position.y = Anchor.IsBottom() ? rect.Bottom - position.cy : rect.Top;
        position.flags &= ~SwpNoMove;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
