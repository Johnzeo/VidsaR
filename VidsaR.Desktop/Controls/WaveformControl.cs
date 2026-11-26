
using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace VidsaR.Desktop.Controls;

public class WaveformControl : Control
{
    public WaveformControl()
    {
        IsHitTestVisible = true;
    }

    public static readonly StyledProperty<float[]> PeaksProperty =
        AvaloniaProperty.Register<WaveformControl, float[]>(
            nameof(Peaks),
            []);

    public float[] Peaks
    {
        get => GetValue(PeaksProperty);
        set => SetValue(PeaksProperty, value);
    }

    public static readonly StyledProperty<IBrush> WaveformBrushProperty =
        AvaloniaProperty.Register<WaveformControl, IBrush>(
            nameof(WaveformBrush),
            Brushes.DeepSkyBlue);

    public IBrush WaveformBrush
    {
        get => GetValue(WaveformBrushProperty);
        set => SetValue(WaveformBrushProperty, value);
    }

    public static readonly StyledProperty<double> SelectionStartProperty =
        AvaloniaProperty.Register<WaveformControl, double>(
            nameof(SelectionStart),
            0);

    public double SelectionStart
    {
        get => GetValue(SelectionStartProperty);
        set => SetValue(SelectionStartProperty, value);
    }

    public static readonly StyledProperty<double> SelectionEndProperty =
        AvaloniaProperty.Register<WaveformControl, double>(
            nameof(SelectionEnd),
            0);

    public double SelectionEnd
    {
        get => GetValue(SelectionEndProperty);
        set => SetValue(SelectionEndProperty, value);
    }

    public static readonly StyledProperty<double> PlayheadPositionProperty =
        AvaloniaProperty.Register<WaveformControl, double>(
            nameof(PlayheadPosition),
            0);

    public double PlayheadPosition
    {
        get => GetValue(PlayheadPositionProperty);
        set => SetValue(PlayheadPositionProperty, value);
    }

    public static readonly StyledProperty<double> DurationSecondsProperty =
        AvaloniaProperty.Register<WaveformControl, double>(
            nameof(DurationSeconds),
            0);

    public double DurationSeconds
    {
        get => GetValue(DurationSecondsProperty);
        set => SetValue(DurationSecondsProperty, value);
    }

    public static readonly StyledProperty<bool> DiscordModeProperty =
        AvaloniaProperty.Register<WaveformControl, bool>(
            nameof(DiscordMode),
            false);

    public bool DiscordMode
    {
        get => GetValue(DiscordModeProperty);
        set => SetValue(DiscordModeProperty, value);
    }

    private const double DragThreshold = 4;
    private const double SelectionHandleThreshold = 12;
    private const double DiscordDurationSeconds = 5;
    private const double MinimumSelectionLength = 0.001;

    private bool _isSelecting;
    private bool _isPointerOver;
    private bool _dragged;
    private bool _discordFrameMoved;

    private bool _draggingSelectionStart;
    private bool _draggingSelectionEnd;

    private double _hoverPosition;
    private double _pointerDownX;
    private double _pointerDownPosition;
    private double _discordDragOffset;

    public event Action? SelectionCompleted;
    public event Action<double>? SeekRequested;

    static WaveformControl()
    {
        AffectsRender<WaveformControl>(
            PeaksProperty,
            WaveformBrushProperty,
            SelectionStartProperty,
            SelectionEndProperty,
            PlayheadPositionProperty,
            DurationSecondsProperty,
            DiscordModeProperty);

        DiscordModeProperty.Changed.AddClassHandler<WaveformControl>(
            (control, args) =>
            {
                if (args.NewValue is bool enabled && enabled)
                {
                    control.InitializeDiscordFrame();
                }
                else
                {
                    control.SelectionStart = 0;
                    control.SelectionEnd = 0;
                }

                control.InvalidateVisual();
            });

        DurationSecondsProperty.Changed.AddClassHandler<WaveformControl>(
            (control, args) =>
            {
                if (control.DiscordMode &&
                    control.DurationSeconds > 0)
                {
                    control.InitializeDiscordFrame();
                }
            });
    }

    public void InitializeDiscordFrame()
    {
        if (DurationSeconds <= 0)
            return;

        double frameLength = Math.Min(
            DiscordDurationSeconds / DurationSeconds,
            1);

        if (frameLength >= 1)
        {
            SelectionStart = 0;
            SelectionEnd = 1;
            InvalidateVisual();
            return;
        }

        double currentStart = Math.Min(SelectionStart, SelectionEnd);
        double currentEnd = Math.Max(SelectionStart, SelectionEnd);

        bool hasCorrectFrame =
            Math.Abs((currentEnd - currentStart) - frameLength) < 0.001;

        double start;

        if (hasCorrectFrame)
        {
            start = currentStart;
        }
        else
        {
            start = PlayheadPosition - frameLength / 2;
        }

        SetDiscordFrame(start);
        InvalidateVisual();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);

        _isPointerOver = true;
        _hoverPosition = GetNormalizedPosition(e);

        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        _pointerDownX = e.GetPosition(this).X;
        _pointerDownPosition = GetNormalizedPosition(e);

        _hoverPosition = _pointerDownPosition;
        _dragged = false;
        _isSelecting = true;
        _isPointerOver = true;
        _discordFrameMoved = false;

        _draggingSelectionStart = false;
        _draggingSelectionEnd = false;

        if (DiscordMode)
        {
            PrepareDiscordFrame(_pointerDownPosition);
        }
        else
        {
            DetectSelectionHandle(_pointerDownX);
        }

        e.Pointer.Capture(this);
        e.Handled = true;

        InvalidateVisual();
    }

    private void DetectSelectionHandle(double pointerX)
    {
        double width = Bounds.Width;

        if (width <= 0)
            return;

        double selectionLeft =
            Math.Min(SelectionStart, SelectionEnd) * width;

        double selectionRight =
            Math.Max(SelectionStart, SelectionEnd) * width;

        bool hasSelection =
            selectionRight - selectionLeft > 1;

        if (!hasSelection)
            return;

        double distanceToLeft =
            Math.Abs(pointerX - selectionLeft);

        double distanceToRight =
            Math.Abs(pointerX - selectionRight);

        if (distanceToLeft <= SelectionHandleThreshold &&
            distanceToLeft <= distanceToRight)
        {
            _draggingSelectionStart = true;
        }
        else if (distanceToRight <= SelectionHandleThreshold)
        {
            _draggingSelectionEnd = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        _isPointerOver = true;
        _hoverPosition = GetNormalizedPosition(e);

        if (_isSelecting)
        {
            double currentX = e.GetPosition(this).X;

            if (DiscordMode)
            {
                if (Math.Abs(currentX - _pointerDownX) >= DragThreshold)
                {
                    _dragged = true;
                    _discordFrameMoved = true;

                    MoveDiscordFrame(_hoverPosition);
                }
            }
            else if (_draggingSelectionStart)
            {
                _dragged = true;

                SelectionStart = Math.Clamp(
                    Math.Min(
                        _hoverPosition,
                        SelectionEnd - MinimumSelectionLength),
                    0,
                    1);
            }
            else if (_draggingSelectionEnd)
            {
                _dragged = true;

                SelectionEnd = Math.Clamp(
                    Math.Max(
                        _hoverPosition,
                        SelectionStart + MinimumSelectionLength),
                    0,
                    1);
            }
            else
            {
                if (!_dragged &&
                    Math.Abs(currentX - _pointerDownX) >= DragThreshold)
                {
                    _dragged = true;
                    SelectionStart = _pointerDownPosition;
                    SelectionEnd = _hoverPosition;
                }
                else if (_dragged)
                {
                    SelectionEnd = _hoverPosition;
                }
            }
        }

        InvalidateVisual();

        if (_isSelecting)
            e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!_isSelecting)
            return;

        double position = GetNormalizedPosition(e);

        _hoverPosition = position;
        _isSelecting = false;

        e.Pointer.Capture(null);
        e.Handled = true;

        if (DiscordMode)
        {
            if (_discordFrameMoved)
            {
                MoveDiscordFrame(position);
            }

            SelectionCompleted?.Invoke();
        }
        else if (_draggingSelectionStart)
        {
            SelectionStart = Math.Clamp(
                Math.Min(
                    position,
                    SelectionEnd - MinimumSelectionLength),
                0,
                1);

            SelectionCompleted?.Invoke();
        }
        else if (_draggingSelectionEnd)
        {
            SelectionEnd = Math.Clamp(
                Math.Max(
                    position,
                    SelectionStart + MinimumSelectionLength),
                0,
                1);

            SelectionCompleted?.Invoke();
        }
        else if (_dragged)
        {
            SelectionEnd = position;

            if (Math.Abs(SelectionEnd - SelectionStart) > 0.001)
            {
                SelectionCompleted?.Invoke();
            }
            else
            {
                SelectionStart = 0;
                SelectionEnd = 0;
                SeekRequested?.Invoke(position);
            }
        }
        else
        {
            SeekRequested?.Invoke(position);
        }

        _dragged = false;
        _discordFrameMoved = false;
        _draggingSelectionStart = false;
        _draggingSelectionEnd = false;

        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (!_isSelecting)
        {
            _isPointerOver = false;
            InvalidateVisual();
        }
    }

    private void PrepareDiscordFrame(double position)
    {
        if (DurationSeconds <= 0)
        {
            SelectionStart = 0;
            SelectionEnd = 0;
            _discordDragOffset = 0;
            return;
        }

        double frameLength = Math.Min(
            DiscordDurationSeconds / DurationSeconds,
            1);

        double currentStart = Math.Min(SelectionStart, SelectionEnd);
        double currentEnd = Math.Max(SelectionStart, SelectionEnd);

        bool existingFrame =
            Math.Abs((currentEnd - currentStart) - frameLength) < 0.001;

        if (existingFrame &&
            position >= currentStart &&
            position <= currentEnd)
        {
            _discordDragOffset = position - currentStart;
        }
        else
        {
            _discordDragOffset = frameLength / 2;

            SetDiscordFrame(position - _discordDragOffset);
        }
    }

    private void MoveDiscordFrame(double position)
    {
        if (DurationSeconds <= 0)
            return;

        double newStart = position - _discordDragOffset;

        SetDiscordFrame(newStart);
    }

    private void SetDiscordFrame(double start)
    {
        if (DurationSeconds <= 0)
            return;

        double frameLength = Math.Min(
            DiscordDurationSeconds / DurationSeconds,
            1);

        start = Math.Clamp(
            start,
            0,
            1 - frameLength);

        SelectionStart = start;
        SelectionEnd = start + frameLength;
    }

    private double GetNormalizedPosition(PointerEventArgs e)
    {
        double width = Bounds.Width;

        if (width <= 0)
            return 0;

        double x = e.GetPosition(this).X;

        return Math.Clamp(x / width, 0, 1);
    }

    private string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));

        return $"{(int)time.TotalMinutes:D2}:{time.Seconds:D2}.{time.Milliseconds:D3}";
    }

    private FormattedText CreateText(
        string text,
        double fontSize,
        IBrush brush)
    {
        return new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial"),
            fontSize,
            brush);
    }

    private static void DrawFormattedText(
        DrawingContext context,
        FormattedText text,
        double x,
        double y)
    {
        context.DrawText(text, new Point(x, y));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double width = Bounds.Width;
        double height = Bounds.Height;

        if (width <= 0 || height <= 0)
            return;

        const double topArea = 23;

        double selectionLeft = Math.Min(
            SelectionStart,
            SelectionEnd) * width;

        double selectionRight = Math.Max(
            SelectionStart,
            SelectionEnd) * width;

        bool hasSelection =
            selectionRight - selectionLeft > 1;

        double activeLeft = hasSelection ? selectionLeft : 0;
        double activeRight = hasSelection ? selectionRight : width;

        double playheadX = Math.Clamp(
            PlayheadPosition,
            0,
            1) * width;

        if (hasSelection)
        {
            context.FillRectangle(
                new SolidColorBrush(Color.FromArgb(55, 85, 184, 255)),
                new Rect(
                    selectionLeft,
                    topArea,
                    selectionRight - selectionLeft,
                    height - topArea));
        }

        // Рисуем waveform.
        if (Peaks != null && Peaks.Length > 0)
        {
            double availableHeight = height - topArea;
            double centerY = topArea + availableHeight / 2;

            double step = width / Peaks.Length;
            double barWidth = Math.Max(1, step * 0.65);

            for (int i = 0; i < Peaks.Length; i++)
            {
                double amplitude = Math.Clamp(Peaks[i], 0f, 1f);

                double barHeight = Math.Max(
                    2,
                    amplitude * availableHeight * 0.85);

                double x = i * step;
                double y = centerY - barHeight / 2;

                context.FillRectangle(
                    WaveformBrush,
                    new Rect(x, y, barWidth, barHeight));
            }
        }

        // Затемняем всё, что не входит в выделение.
        var darkBrush = new SolidColorBrush(
            Color.FromArgb(175, 12, 14, 20));

        if (hasSelection)
        {
            if (activeLeft > 0)
            {
                context.FillRectangle(
                    darkBrush,
                    new Rect(0, 0, activeLeft, height));
            }

            if (activeRight < width)
            {
                context.FillRectangle(
                    darkBrush,
                    new Rect(
                        activeRight,
                        0,
                        width - activeRight,
                        height));
            }
        }

        // Затемняем уже проигранную часть активной области.
        double playedEnd = Math.Clamp(
            playheadX,
            activeLeft,
            activeRight);

        if (playedEnd > activeLeft)
        {
            context.FillRectangle(
                darkBrush,
                new Rect(
                    activeLeft,
                    0,
                    playedEnd - activeLeft,
                    height));
        }

        // Границы выделения и время начала/конца.
        if (hasSelection)
        {
            var selectionPen = new Pen(
                new SolidColorBrush(Color.FromRgb(120, 190, 255)),
                2);

            context.DrawLine(
                selectionPen,
                new Point(selectionLeft, 0),
                new Point(selectionLeft, height));

            context.DrawLine(
                selectionPen,
                new Point(selectionRight, 0),
                new Point(selectionRight, height));

            double startSeconds =
                Math.Min(SelectionStart, SelectionEnd) * DurationSeconds;

            double endSeconds =
                Math.Max(SelectionStart, SelectionEnd) * DurationSeconds;

            var startText = CreateText(
                FormatTime(startSeconds),
                11,
                Brushes.White);

            var endText = CreateText(
                FormatTime(endSeconds),
                11,
                Brushes.White);

            double startX = Math.Clamp(
                selectionLeft - startText.Width - 6,
                0,
                Math.Max(0, width - startText.Width));

            double endX = Math.Clamp(
                selectionRight + 6,
                0,
                Math.Max(0, width - endText.Width));

            DrawFormattedText(context, startText, startX, 3);
            DrawFormattedText(context, endText, endX, 3);

            double selectedDuration = Math.Abs(
                endSeconds - startSeconds);

            string durationLabel = DiscordMode
                ? $"Discord Mode: {FormatTime(selectedDuration)}"
                : $"Длительность: {FormatTime(selectedDuration)}";

            var durationText = CreateText(
                durationLabel,
                11,
                Brushes.White);

            double durationX = Math.Clamp(
                (selectionLeft + selectionRight - durationText.Width) / 2,
                0,
                Math.Max(0, width - durationText.Width));

            DrawFormattedText(
                context,
                durationText,
                durationX,
                height - 16);
        }

        // Бегунок воспроизведения.
        context.DrawLine(
            new Pen(Brushes.White, 2),
            new Point(playheadX, 0),
            new Point(playheadX, height));

        context.FillRectangle(
            Brushes.White,
            new Rect(playheadX - 4, 0, 8, 8));

        // Линия наведения мыши поверх всех элементов.
        if (_isPointerOver && !_isSelecting)
        {
            double hoverX = _hoverPosition * width;

            context.DrawLine(
                new Pen(Brushes.White, 1.5),
                new Point(hoverX, 0),
                new Point(hoverX, height));

            var hoverText = CreateText(
                FormatTime(_hoverPosition * DurationSeconds),
                12,
                Brushes.White);

            double textX = Math.Clamp(
                hoverX + 6,
                0,
                Math.Max(0, width - hoverText.Width));

            context.FillRectangle(
                new SolidColorBrush(Color.FromArgb(220, 20, 22, 30)),
                new Rect(
                    textX - 3,
                    1,
                    hoverText.Width + 6,
                    hoverText.Height + 4));

            DrawFormattedText(
                context,
                hoverText,
                textX,
                3);
        }
    }
}
