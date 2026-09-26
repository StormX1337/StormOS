using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using StormOS.Core.Settings;
using static StormOS.Overlay.OverlayNative;

namespace StormOS.Overlay;

/// <summary>
/// The in-game overlay: a topmost, click-through, non-activating layered Win32 window rendered with GDI on its own
/// thread. It uses only standard windowing (no injection, no hooks into game processes). It is visible over windowed
/// and borderless games; exclusive fullscreen games draw above it by design.
/// </summary>
public sealed class OverlayWindow : IDisposable
{
    private const uint UpdateMessage = WmApp + 1;
    private const uint SettingsMessage = WmApp + 2;
    private const int HotkeyId = 0x5701;
    private const string ClassName = "StormOS.Overlay";

    private readonly ILogger<OverlayWindow> _logger;
    private readonly Lock _gate = new();
    private readonly ManualResetEventSlim _ready = new();
    private readonly WndProc _wndProc;
    private Thread? _thread;
    private IntPtr _hwnd;
    private IReadOnlyList<OverlayLine> _lines = [];
    private OverlaySettings _settings = new();
    private bool _visible = true;

    /// <summary>Initializes a new instance of the <see cref="OverlayWindow"/> class.</summary>
    /// <param name="logger">Logger.</param>
    public OverlayWindow(ILogger<OverlayWindow> logger)
    {
        _logger = logger;
        _wndProc = WindowProcedure;
    }

    /// <summary>Raised on the overlay thread when the hotkey toggles visibility.</summary>
    public event EventHandler<bool>? VisibilityToggled;

    /// <summary>Gets a value indicating whether the window thread is running.</summary>
    public bool IsRunning => _thread is { IsAlive: true };

    /// <summary>Starts the overlay thread and window.</summary>
    /// <param name="settings">Initial settings.</param>
    public void Start(OverlaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (IsRunning)
        {
            ApplySettings(settings);
            return;
        }

        _settings = settings;
        _visible = true;
        _ready.Reset();
        _thread = new Thread(Run) { IsBackground = true, Name = "StormOS overlay" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    /// <summary>Updates the displayed lines.</summary>
    /// <param name="lines">Lines.</param>
    public void Update(IReadOnlyList<OverlayLine> lines)
    {
        lock (_gate)
        {
            _lines = lines;
        }

        if (_hwnd != IntPtr.Zero)
        {
            PostMessage(_hwnd, UpdateMessage, IntPtr.Zero, IntPtr.Zero);
        }
    }

    /// <summary>Applies new settings (position, size, opacity, hotkey).</summary>
    /// <param name="settings">Settings.</param>
    public void ApplySettings(OverlaySettings settings)
    {
        lock (_gate)
        {
            _settings = settings;
        }

        if (_hwnd != IntPtr.Zero)
        {
            PostMessage(_hwnd, SettingsMessage, IntPtr.Zero, IntPtr.Zero);
        }
    }

    /// <summary>Stops the overlay.</summary>
    public void Stop()
    {
        if (_hwnd != IntPtr.Zero)
        {
            PostMessage(_hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
        }

        _thread?.Join(TimeSpan.FromSeconds(3));
        _thread = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        _ready.Dispose();
    }

    private void Run()
    {
        try
        {
            var instance = GetModuleHandle(null);
            var windowClass = new WndClassEx
            {
                Size = (uint)Marshal.SizeOf<WndClassEx>(),
                WndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                Instance = instance,
                ClassName = ClassName,
            };
            RegisterClassEx(ref windowClass);
            _hwnd = CreateWindowEx(WsExLayered | WsExTransparent | WsExTopmost | WsExToolWindow | WsExNoActivate, ClassName, "STORM OS Overlay", WsPopup, 0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
            {
                _logger.LogError("The overlay window could not be created (error {Error})", Marshal.GetLastPInvokeError());
                return;
            }

            RegisterConfiguredHotkey();
            Layout();
            _ready.Set();
            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(message);
                DispatchMessage(message);
            }
        }
        finally
        {
            _hwnd = IntPtr.Zero;
            _ready.Set();
        }
    }

    private void RegisterConfiguredHotkey()
    {
        UnregisterHotKey(_hwnd, HotkeyId);
        OverlaySettings settings;
        lock (_gate)
        {
            settings = _settings;
        }

        if (OverlayContent.TryParseHotkey(settings.Hotkey, out var modifiers, out var key) && !RegisterHotKey(_hwnd, HotkeyId, modifiers, key))
        {
            _logger.LogWarning("Overlay hotkey {Hotkey} is already in use by another application", settings.Hotkey);
        }
    }

    private static (int Width, int Height, int Row, int Padding) Measure(OverlaySettings settings, int lineCount)
    {
        var row = (int)Math.Round(Math.Clamp(settings.FontSize, 8, 32) * 1.9);
        var padding = row / 2;
        var width = (int)Math.Round(Math.Clamp(settings.FontSize, 8, 32) * 15.0);
        return (width, (Math.Max(1, lineCount) * row) + (padding * 2), row, padding);
    }

    private void Layout()
    {
        OverlaySettings settings;
        int lineCount;
        lock (_gate)
        {
            settings = _settings;
            lineCount = _lines.Count;
        }

        var (width, height, _, _) = Measure(settings, lineCount);
        var monitor = MonitorFromWindow(GetForegroundWindow(), MonitorDefaultToPrimary);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(monitor, ref info);
        var area = info.Monitor;
        var x = settings.Position is OverlayPosition.TopLeft or OverlayPosition.BottomLeft ? area.Left + settings.OffsetX : area.Right - width - settings.OffsetX;
        var y = settings.Position is OverlayPosition.TopLeft or OverlayPosition.TopRight ? area.Top + settings.OffsetY : area.Bottom - height - settings.OffsetY;
        SetLayeredWindowAttributes(_hwnd, 0, (byte)Math.Round(Math.Clamp(settings.Opacity, 0.2, 1.0) * 255), LwaAlpha);
        SetWindowPos(_hwnd, HwndTopmost, x, y, width, height, SwpNoActivate | (_visible ? SwpShowWindow : 0));
        if (_visible)
        {
            ShowWindow(_hwnd, SwShowNoActivate);
        }
    }

    private void Paint(IntPtr hwnd)
    {
        var hdc = BeginPaint(hwnd, out var paint);
        try
        {
            IReadOnlyList<OverlayLine> lines;
            OverlaySettings settings;
            lock (_gate)
            {
                lines = _lines;
                settings = _settings;
            }

            var (width, height, row, padding) = Measure(settings, lines.Count);
            var background = CreateSolidBrush(Rgb(14, 16, 22));
            var font = CreateFont(-(int)Math.Round(Math.Clamp(settings.FontSize, 8, 32) * 1.33), 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
            try
            {
                FillRect(hdc, new Rect { Right = width, Bottom = height }, background);
                var previousFont = SelectObject(hdc, font);
                _ = SetBkMode(hdc, Transparent);
                for (var i = 0; i < lines.Count; i++)
                {
                    var top = padding + (i * row);
                    var labelRect = new Rect { Left = padding, Top = top, Right = width - padding, Bottom = top + row };
                    _ = SetTextColor(hdc, Rgb(150, 158, 175));
                    _ = DrawText(hdc, lines[i].Label, -1, ref labelRect, DtLeft | DtSingleLine | DtVCenter | DtNoPrefix);
                    var valueRect = labelRect;
                    _ = SetTextColor(hdc, lines[i].Accent ? Rgb(64, 196, 255) : Rgb(240, 243, 248));
                    _ = DrawText(hdc, lines[i].Value, -1, ref valueRect, DtRight | DtSingleLine | DtVCenter | DtNoPrefix);
                }

                SelectObject(hdc, previousFont);
            }
            finally
            {
                DeleteObject(font);
                DeleteObject(background);
            }
        }
        finally
        {
            EndPaint(hwnd, ref paint);
        }
    }

    private IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WmPaint:
                Paint(hwnd);
                return IntPtr.Zero;
            case UpdateMessage:
                Layout();
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return IntPtr.Zero;
            case SettingsMessage:
                RegisterConfiguredHotkey();
                Layout();
                InvalidateRect(hwnd, IntPtr.Zero, false);
                return IntPtr.Zero;
            case WmHotkey when wParam == HotkeyId:
                _visible = !_visible;
                ShowWindow(hwnd, _visible ? SwShowNoActivate : SwHide);
                VisibilityToggled?.Invoke(this, _visible);
                return IntPtr.Zero;
            case WmClose:
                UnregisterHotKey(hwnd, HotkeyId);
                DestroyWindow(hwnd);
                return IntPtr.Zero;
            case WmDestroy:
                PostQuitMessage(0);
                return IntPtr.Zero;
            default:
                return DefWindowProc(hwnd, message, wParam, lParam);
        }
    }
}
