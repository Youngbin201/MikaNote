using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MikaNote.App;

public partial class SettingsPanelWindow : Window
{
    private bool _isClosingPermanently;
    private bool _isDesktopAttached;
    private bool _hasBeenShown;
    private bool _isPaneVisible;
    private bool _keepVisibleOnShowDesktop = true;
    private IntPtr _windowHandle;
    private nint _originalStyle;
    private nint _originalExStyle;

    public bool IsPaneVisible => _isPaneVisible;

    public SettingsPanelWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        HwndSource? source = PresentationSource.FromVisual(this) as HwndSource;
        if (source is null)
        {
            return;
        }

        _windowHandle = source.Handle;
        _originalStyle = DesktopWindowHost.GetWindowStyle(_windowHandle);
        _originalExStyle = DesktopWindowHost.GetWindowExStyle(_windowHandle);
        SetDesktopAttachment(_keepVisibleOnShowDesktop);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isClosingPermanently)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    public void ClosePermanently()
    {
        _isClosingPermanently = true;
        Close();
    }

    public void ShowAt(double left, double top)
    {
        if (!_hasBeenShown)
        {
            Show();
            _hasBeenShown = true;
        }

        _isPaneVisible = true;
        UpdatePlacement(left, top);
    }

    public void HidePane()
    {
        if (!_hasBeenShown || !_isPaneVisible)
        {
            return;
        }

        _isPaneVisible = false;
        MoveOffscreen();
    }

    public void SetCornerRadius(double cornerRadius)
    {
        SettingsRootBorder.CornerRadius = new CornerRadius(Math.Max(0, cornerRadius));
    }

    public bool ContainsScreenPoint(System.Windows.Point screenPoint)
    {
        if (!_isPaneVisible)
        {
            return false;
        }

        if (_windowHandle != IntPtr.Zero)
        {
            return DesktopWindowHost.ContainsScreenPoint(_windowHandle, screenPoint.X, screenPoint.Y);
        }

        return screenPoint.X >= Left
            && screenPoint.X < Left + ActualWidth
            && screenPoint.Y >= Top
            && screenPoint.Y < Top + ActualHeight;
    }

    public void SetDesktopAttachment(bool keepVisibleOnShowDesktop)
    {
        _keepVisibleOnShowDesktop = keepVisibleOnShowDesktop;

        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        Matrix transform = PresentationSource.FromVisual(this) is HwndSource source
            ? source.CompositionTarget?.TransformToDevice ?? Matrix.Identity
            : Matrix.Identity;

        int x = _isPaneVisible ? (int)Math.Round(Left * transform.M11) : -32000;
        int y = _isPaneVisible ? (int)Math.Round(Top * transform.M22) : -32000;
        int width = Math.Max(1, (int)Math.Round(ActualWidth * transform.M11));
        int height = Math.Max(1, (int)Math.Round(ActualHeight * transform.M22));

        if (keepVisibleOnShowDesktop && !_isDesktopAttached)
        {
            _isDesktopAttached = DesktopWindowHost.TryAttachToDesktop(_windowHandle, _originalStyle, _originalExStyle, x, y, width, height);
        }
        else if (!keepVisibleOnShowDesktop && _isDesktopAttached)
        {
            DesktopWindowHost.DetachFromDesktop(_windowHandle, _originalStyle, _originalExStyle, x, y, width, height);
            _isDesktopAttached = false;
        }
    }

    public void UpdatePlacement(double left, double top)
    {
        UpdateLayout();

        if (_windowHandle == IntPtr.Zero)
        {
            Left = left;
            Top = top;
            return;
        }

        Matrix transform = PresentationSource.FromVisual(this) is HwndSource source
            ? source.CompositionTarget?.TransformToDevice ?? Matrix.Identity
            : Matrix.Identity;

        int x = (int)Math.Round(left * transform.M11);
        int y = (int)Math.Round(top * transform.M22);
        int width = Math.Max(1, (int)Math.Round(ActualWidth * transform.M11));
        int height = Math.Max(1, (int)Math.Round(ActualHeight * transform.M22));

        if (_isDesktopAttached)
        {
            DesktopWindowHost.UpdateBounds(_windowHandle, x, y, width, height);
        }
    }

    private void MoveOffscreen()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            Left = -32000;
            Top = -32000;
            return;
        }

        Matrix transform = PresentationSource.FromVisual(this) is HwndSource source
            ? source.CompositionTarget?.TransformToDevice ?? Matrix.Identity
            : Matrix.Identity;

        int width = Math.Max(1, (int)Math.Round(ActualWidth * transform.M11));
        int height = Math.Max(1, (int)Math.Round(ActualHeight * transform.M22));
        DesktopWindowHost.UpdateBounds(_windowHandle, -32000, -32000, width, height);
    }
}
