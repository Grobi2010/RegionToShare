using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Threading;
using RegionToShare.Properties;
using static RegionToShare.NativeMethods;

namespace RegionToShare;

/// <summary>
/// Highlights the mouse cursor with a ring, on the screen and/or in the shared image, while sharing is active.
/// Clicks change the ring color for a while and play a sound.
/// </summary>
internal sealed class MouseHighlighter : IDisposable
{
    private readonly Settings _settings = Settings.Default;
    private readonly ButtonState _leftButton = new();
    private readonly ButtonState _rightButton = new();
    private readonly DispatcherTimer _clickTimer = new(DispatcherPriority.Normal);

    private MouseHook? _hook;
    private HighlightOverlay? _overlay;
    private bool _isSharing;

    public MouseHighlighter()
    {
        _clickTimer.Tick += ClickTimer_Tick;
        _settings.PropertyChanged += Settings_PropertyChanged;
    }

    public bool IsSharing
    {
        get => _isSharing;
        set
        {
            _isSharing = value;
            Update();
        }
    }

    public bool IsVisibleInShare => IsActive && _settings.HighlighterInShare;

    private bool IsActive => _isSharing && _settings.HighlighterEnabled;

    private int Diameter => Clamp(_settings.HighlighterDiameter, 4, 500);

    private int Thickness => Clamp(_settings.HighlighterThickness, 1, Diameter / 2);

    /// <summary>
    /// Draws the ring centered at the given point.
    /// </summary>
    public void DrawRing(Graphics graphics, float centerX, float centerY)
    {
        var thickness = Thickness;
        var diameter = Diameter - thickness;

        using var pen = new Pen(CurrentColor, thickness);

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.DrawEllipse(pen, centerX - diameter / 2f, centerY - diameter / 2f, diameter, diameter);
    }

    public void Dispose()
    {
        _settings.PropertyChanged -= Settings_PropertyChanged;
        _isSharing = false;
        Update();
    }

    private System.Drawing.Color CurrentColor
    {
        get
        {
            var now = Stopwatch.GetTimestamp();
            var duration = Stopwatch.Frequency * Clamp(_settings.HighlighterClickDuration, 0, 10000) / 1000;

            var leftActive = _leftButton.IsActive(now, duration);
            var rightActive = _rightButton.IsActive(now, duration);

            // If both buttons are active, the most recent click wins.
            var colorName = _settings.HighlighterColor;

            if (leftActive && (!rightActive || _leftButton.PressedAt >= _rightButton.PressedAt))
            {
                colorName = _settings.HighlighterLeftClickColor;
            }
            else if (rightActive)
            {
                colorName = _settings.HighlighterRightClickColor;
            }

            if (!ColorBrushConverter.TryParseColor(colorName, out var color))
            {
                color = System.Windows.Media.Colors.Yellow;
            }

            var alpha = color.A * Clamp(_settings.HighlighterOpacity, 0, 100) / 100;

            return System.Drawing.Color.FromArgb(alpha, color.R, color.G, color.B);
        }
    }

    private void Update()
    {
        if (IsActive)
        {
            if (_hook == null)
            {
                _hook = new MouseHook();
                _hook.Moved += Hook_Moved;
                _hook.ButtonChanged += Hook_ButtonChanged;
            }
        }
        else
        {
            _hook?.Dispose();
            _hook = null;
            _leftButton.Reset();
            _rightButton.Reset();
            _clickTimer.Stop();
        }

        if (IsActive && _settings.HighlighterOnScreen)
        {
            _overlay ??= new HighlightOverlay();
            RenderOverlay();
        }
        else
        {
            _overlay?.Dispose();
            _overlay = null;
        }
    }

    private void RenderOverlay()
    {
        if (_overlay == null)
            return;

        GetCursorPos(out var position);

        // One extra pixel on each side for anti aliasing.
        _overlay.Render(position, Diameter + 2, DrawRing);
    }

    private void Hook_Moved(POINT position)
    {
        _overlay?.MoveTo(position);
    }

    private void Hook_ButtonChanged(ClickButton button, bool isPressed)
    {
        var state = button == ClickButton.Left ? _leftButton : _rightButton;

        state.IsPressed = isPressed;

        if (isPressed)
        {
            state.PressedAt = Stopwatch.GetTimestamp();

            ClickSounds.Play(button == ClickButton.Left ? _settings.HighlighterLeftClickSound : _settings.HighlighterRightClickSound, _settings.HighlighterVolume);

            _clickTimer.Stop();
            _clickTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, _settings.HighlighterClickDuration));
            _clickTimer.Start();
        }

        RenderOverlay();
    }

    private void ClickTimer_Tick(object? sender, EventArgs e)
    {
        _clickTimer.Stop();

        // The timer may fire slightly early; check again shortly until the click color has expired.
        var now = Stopwatch.GetTimestamp();
        var duration = Stopwatch.Frequency * Clamp(_settings.HighlighterClickDuration, 0, 10000) / 1000;
        if ((!_leftButton.IsPressed && _leftButton.IsActive(now, duration)) || (!_rightButton.IsPressed && _rightButton.IsActive(now, duration)))
        {
            _clickTimer.Interval = TimeSpan.FromMilliseconds(15);
            _clickTimer.Start();
        }

        RenderOverlay();
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName?.StartsWith("Highlighter", StringComparison.Ordinal) == true)
        {
            Update();
        }
    }

    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

    private sealed class ButtonState
    {
        public bool IsPressed { get; set; }

        public long PressedAt { get; set; }

        public bool IsActive(long now, long duration) => IsPressed || (PressedAt != 0 && now - PressedAt < duration);

        public void Reset()
        {
            IsPressed = false;
            PressedAt = 0;
        }
    }
}
