
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using VidsaR.Core.Models;

namespace VidsaR.Desktop.Controls;

public class TimelineControl : Control
{
    public static readonly StyledProperty<ObservableCollection<AudioClip>?> ClipsProperty =
        AvaloniaProperty.Register<TimelineControl, ObservableCollection<AudioClip>?>(
            nameof(Clips));

    public ObservableCollection<AudioClip>? Clips
    {
        get => GetValue(ClipsProperty);
        set => SetValue(ClipsProperty, value);
    }

    public static readonly StyledProperty<double> DurationSecondsProperty =
        AvaloniaProperty.Register<TimelineControl, double>(
            nameof(DurationSeconds),
            0);

    public double DurationSeconds
    {
        get => GetValue(DurationSecondsProperty);
        set => SetValue(DurationSecondsProperty, value);
    }

    public static readonly StyledProperty<double> PlayheadSecondsProperty =
        AvaloniaProperty.Register<TimelineControl, double>(
            nameof(PlayheadSeconds),
            0);

    public double PlayheadSeconds
    {
        get => GetValue(PlayheadSecondsProperty);
        set => SetValue(PlayheadSecondsProperty, value);
    }

    public event Action<double>? SeekRequested;

    private const double RulerHeight = 28;
    private const double TrackHeight = 64;
    private const double TrackTop = 38;
    private const double HandleThreshold = 9;
    private const double SnapThreshold = 15;
    private const double MinimumClipDuration = 0.1;

    private static readonly Color[] ClipColors =
    [
        Color.FromRgb(48, 105, 155),
        Color.FromRgb(43, 135, 105),
        Color.FromRgb(126, 83, 170),
        Color.FromRgb(190, 112, 48),
        Color.FromRgb(165, 75, 100),
        Color.FromRgb(65, 130, 145),
        Color.FromRgb(145, 125, 55),
        Color.FromRgb(100, 100, 165)
    ];

    private enum DragMode
    {
        None,
        Move,
        TrimStart,
        TrimEnd
    }

    private DragMode _dragMode;

    private AudioClip? _draggedClip;

    private double _dragStartX;

    private TimeSpan _initialTimelineStart;
    private TimeSpan _initialSourceStart;
    private TimeSpan _initialSourceEnd;

    private double? _snapGuideSeconds;

    static TimelineControl()
    {
        AffectsRender<TimelineControl>(
            ClipsProperty,
            DurationSecondsProperty,
            PlayheadSecondsProperty);

        ClipsProperty.Changed.AddClassHandler<TimelineControl>(
            (control, args) =>
            {
                if (args.OldValue is ObservableCollection<AudioClip> oldClips)
                    oldClips.CollectionChanged -= control.Clips_CollectionChanged;

                if (args.NewValue is ObservableCollection<AudioClip> newClips)
                    newClips.CollectionChanged += control.Clips_CollectionChanged;

                control.InvalidateVisual();
            });
    }

    public TimelineControl()
    {
        IsHitTestVisible = true;
    }

    private void Clips_CollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
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

    private Color GetClipColor(AudioClip clip)
    {
        if (Clips == null)
            return ClipColors[0];

        int index = Clips.IndexOf(clip);

        if (index < 0)
            index = 0;

        return ClipColors[index % ClipColors.Length];
    }

    private static Color LightenColor(Color color)
    {
        return Color.FromRgb(
            (byte)Math.Min(255, color.R + 65),
            (byte)Math.Min(255, color.G + 65),
            (byte)Math.Min(255, color.B + 65));
    }

    private bool TrySnapTime(
        double candidateSeconds,
        AudioClip currentClip,
        double pixelsPerSecond,
        out double snappedSeconds)
    {
        snappedSeconds = candidateSeconds;

        if (Clips == null || pixelsPerSecond <= 0)
            return false;

        double bestPosition = candidateSeconds;
        double smallestDistance = double.MaxValue;
        bool found = false;

        void CheckPoint(double targetSeconds)
        {
            double distancePixels =
                Math.Abs(candidateSeconds - targetSeconds) * pixelsPerSecond;

            if (distancePixels <= SnapThreshold &&
                distancePixels < smallestDistance)
            {
                smallestDistance = distancePixels;
                bestPosition = targetSeconds;
                found = true;
            }
        }

        CheckPoint(0);
        CheckPoint(Math.Round(candidateSeconds));

        foreach (var clip in Clips)
        {
            if (ReferenceEquals(clip, currentClip))
                continue;

            CheckPoint(clip.TimelineStart.TotalSeconds);
            CheckPoint(clip.TimelineEnd.TotalSeconds);
        }

        snappedSeconds = bestPosition;

        return found;
    }

    private void DrawSnapGuide(
        DrawingContext context,
        double pixelsPerSecond,
        double height)
    {
        if (_snapGuideSeconds == null)
            return;

        double x = _snapGuideSeconds.Value * pixelsPerSecond;

        if (x < 0 || x > Bounds.Width)
            return;

        var guideBrush = new SolidColorBrush(
            Color.FromRgb(255, 220, 70));

        var guidePen = new Pen(guideBrush, 2);

        context.DrawLine(
            guidePen,
            new Point(x, 0),
            new Point(x, height));

        context.FillRectangle(
            guideBrush,
            new Rect(x - 4, RulerHeight - 5, 8, 5));
    }

    private void DrawPlayhead(
        DrawingContext context,
        double pixelsPerSecond,
        double height)
    {
        if (DurationSeconds <= 0)
            return;

        double position = Math.Clamp(
            PlayheadSeconds,
            0,
            DurationSeconds);

        double x = position * pixelsPerSecond;

        if (x < 0 || x > Bounds.Width)
            return;

        var brush = new SolidColorBrush(
            Color.FromRgb(255, 75, 75));

        var pen = new Pen(brush, 2);

        // Вертикальная линия бегунка.
        context.DrawLine(
            pen,
            new Point(x, 0),
            new Point(x, height));

        // Треугольный маркер на линейке.
        var marker = new StreamGeometry();

        using (var geometry = marker.Open())
        {
            geometry.BeginFigure(
                new Point(x - 6, 0),
                true);

            geometry.LineTo(new Point(x + 6, 0));
            geometry.LineTo(new Point(x, 9));
            geometry.EndFigure(true);
        }

        context.DrawGeometry(
            brush,
            null,
            marker);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        double width = Bounds.Width;
        double height = Bounds.Height;

        if (width <= 0 || height <= 0)
            return;

        context.FillRectangle(
            new SolidColorBrush(Color.FromRgb(16, 17, 22)),
            new Rect(0, 0, width, height));

        context.FillRectangle(
            new SolidColorBrush(Color.FromRgb(30, 33, 43)),
            new Rect(0, 0, width, RulerHeight));

        if (DurationSeconds <= 0)
            return;

        double pixelsPerSecond = width / DurationSeconds;

        double interval = DurationSeconds switch
        {
            <= 30 => 1,
            <= 120 => 5,
            <= 600 => 10,
            <= 1800 => 30,
            <= 3600 => 60,
            _ => 300
        };

        var rulerPen = new Pen(
            new SolidColorBrush(Color.FromRgb(90, 95, 112)),
            1);

        for (double seconds = 0;
             seconds <= DurationSeconds;
             seconds += interval)
        {
            double x = seconds * pixelsPerSecond;

            context.DrawLine(
                rulerPen,
                new Point(x, RulerHeight - 8),
                new Point(x, RulerHeight));

            var label = CreateText(
                FormatTime(seconds),
                10,
                Brushes.LightGray);

            context.DrawText(
                label,
                new Point(x + 4, 4));

            context.DrawLine(
                new Pen(
                    new SolidColorBrush(Color.FromArgb(35, 140, 150, 170)),
                    1),
                new Point(x, RulerHeight),
                new Point(x, height));
        }

        context.FillRectangle(
            new SolidColorBrush(Color.FromRgb(27, 30, 39)),
            new Rect(0, TrackTop, width, TrackHeight));

        if (Clips != null)
        {
            foreach (var clip in Clips)
            {
                double clipStart = clip.TimelineStart.TotalSeconds;
                double clipDuration = clip.Duration.TotalSeconds;

                if (clipDuration <= 0)
                    continue;

                double x = clipStart * pixelsPerSecond;
                double clipWidth = clipDuration * pixelsPerSecond;

                if (x + clipWidth < 0 || x > width)
                    continue;

                var clipRect = new Rect(
                    x,
                    TrackTop + 4,
                    Math.Max(clipWidth, 2),
                    TrackHeight - 8);

                bool isDragged = ReferenceEquals(clip, _draggedClip);

                Color clipColor = GetClipColor(clip);

                if (isDragged)
                    clipColor = LightenColor(clipColor);

                context.FillRectangle(
                    new SolidColorBrush(clipColor),
                    clipRect);

                context.FillRectangle(
                    new SolidColorBrush(LightenColor(clipColor)),
                    new Rect(
                        x,
                        TrackTop + 4,
                        Math.Max(clipWidth, 2),
                        4));

                context.DrawRectangle(
                    new Pen(
                        new SolidColorBrush(LightenColor(clipColor)),
                        isDragged ? 2 : 1),
                    clipRect);

                if (clipWidth > 18)
                {
                    var handleBrush = new SolidColorBrush(
                        Color.FromRgb(235, 240, 250));

                    context.DrawLine(
                        new Pen(handleBrush, 2),
                        new Point(x + 4, TrackTop + 13),
                        new Point(x + 4, TrackTop + TrackHeight - 13));

                    context.DrawLine(
                        new Pen(handleBrush, 2),
                        new Point(x + clipWidth - 4, TrackTop + 13),
                        new Point(x + clipWidth - 4, TrackTop + TrackHeight - 13));
                }

                string fileName =
                    Path.GetFileNameWithoutExtension(clip.SourceFilePath);

                if (string.IsNullOrWhiteSpace(fileName))
                    fileName = "Аудиоклип";

                var text = CreateText(
                    fileName,
                    12,
                    Brushes.White);

                if (clipWidth > 50)
                {
                    context.DrawText(
                        text,
                        new Point(
                            x + 10,
                            TrackTop + (TrackHeight - text.Height) / 2));
                }
            }
        }

        DrawSnapGuide(context, pixelsPerSecond, height);

        // Бегунок рисуется последним, чтобы он был поверх клипов.
        DrawPlayhead(context, pixelsPerSecond, height);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (DurationSeconds <= 0 || Bounds.Width <= 0)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var point = e.GetPosition(this);

        double pixelsPerSecond = Bounds.Width / DurationSeconds;

        if (pixelsPerSecond <= 0)
            return;

        // Проверяем, попал ли пользователь в клип.
        if (point.Y >= TrackTop &&
            point.Y <= TrackTop + TrackHeight &&
            Clips != null)
        {
            foreach (var clip in Clips)
            {
                double clipStart =
                    clip.TimelineStart.TotalSeconds * pixelsPerSecond;

                double clipWidth =
                    clip.Duration.TotalSeconds * pixelsPerSecond;

                double clipEnd = clipStart + clipWidth;

                if (point.X < clipStart || point.X > clipEnd)
                    continue;

                _draggedClip = clip;
                _dragStartX = point.X;

                _initialTimelineStart = clip.TimelineStart;
                _initialSourceStart = clip.SourceStart;
                _initialSourceEnd = clip.SourceEnd;

                _snapGuideSeconds = null;

                double distanceToLeft = Math.Abs(point.X - clipStart);
                double distanceToRight = Math.Abs(point.X - clipEnd);

                if (distanceToLeft <= HandleThreshold)
                {
                    _dragMode = DragMode.TrimStart;
                }
                else if (distanceToRight <= HandleThreshold)
                {
                    _dragMode = DragMode.TrimEnd;
                }
                else
                {
                    _dragMode = DragMode.Move;
                }

                e.Pointer.Capture(this);
                e.Handled = true;

                InvalidateVisual();
                return;
            }
        }

        // Если кликнули по линейке или свободному месту дорожки,
        // перемещаем бегунок воспроизведения.
        if (point.Y <= RulerHeight ||
            (point.Y >= TrackTop &&
             point.Y <= TrackTop + TrackHeight))
        {
            double newPosition = Math.Clamp(
                point.X / pixelsPerSecond,
                0,
                DurationSeconds);

            PlayheadSeconds = newPosition;

            SeekRequested?.Invoke(newPosition);

            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_dragMode == DragMode.None ||
            _draggedClip == null ||
            DurationSeconds <= 0)
        {
            return;
        }

        double pixelsPerSecond = Bounds.Width / DurationSeconds;

        if (pixelsPerSecond <= 0)
            return;

        double deltaSeconds =
            (e.GetPosition(this).X - _dragStartX) / pixelsPerSecond;

        var clip = _draggedClip;

        _snapGuideSeconds = null;

        switch (_dragMode)
        {
            case DragMode.Move:
                {
                    double clipDuration =
                        (_initialSourceEnd - _initialSourceStart).TotalSeconds;

                    double maximumStart =
                        Math.Max(0, DurationSeconds - clipDuration);

                    double newStart = Math.Clamp(
                        _initialTimelineStart.TotalSeconds + deltaSeconds,
                        0,
                        maximumStart);

                    double newEnd = newStart + clipDuration;

                    bool snappedStart = TrySnapTime(
                        newStart,
                        clip,
                        pixelsPerSecond,
                        out double snappedStartPosition);

                    bool snappedEnd = TrySnapTime(
                        newEnd,
                        clip,
                        pixelsPerSecond,
                        out double snappedEndPosition);

                    double startDistance = snappedStart
                        ? Math.Abs(snappedStartPosition - newStart) * pixelsPerSecond
                        : double.MaxValue;

                    double endDistance = snappedEnd
                        ? Math.Abs(snappedEndPosition - newEnd) * pixelsPerSecond
                        : double.MaxValue;

                    if (startDistance <= endDistance &&
                        startDistance <= SnapThreshold)
                    {
                        newStart = snappedStartPosition;
                        _snapGuideSeconds = snappedStartPosition;
                    }
                    else if (endDistance <= SnapThreshold)
                    {
                        newStart = snappedEndPosition - clipDuration;
                        _snapGuideSeconds = snappedEndPosition;
                    }

                    newStart = Math.Clamp(newStart, 0, maximumStart);

                    clip.TimelineStart = TimeSpan.FromSeconds(newStart);
                    break;
                }

            case DragMode.TrimStart:
                {
                    double originalDuration =
                        (_initialSourceEnd - _initialSourceStart).TotalSeconds;

                    double minimumDelta =
                        -_initialTimelineStart.TotalSeconds;

                    double maximumDelta =
                        originalDuration - MinimumClipDuration;

                    double actualDelta = Math.Clamp(
                        deltaSeconds,
                        minimumDelta,
                        maximumDelta);

                    double newTimelineStart =
                        _initialTimelineStart.TotalSeconds + actualDelta;

                    newTimelineStart = Math.Clamp(
                        newTimelineStart,
                        0,
                        _initialTimelineStart.TotalSeconds + maximumDelta);

                    if (TrySnapTime(
                        newTimelineStart,
                        clip,
                        pixelsPerSecond,
                        out double snappedPosition))
                    {
                        newTimelineStart = Math.Clamp(
                            snappedPosition,
                            0,
                            _initialTimelineStart.TotalSeconds + maximumDelta);

                        _snapGuideSeconds = newTimelineStart;
                    }

                    actualDelta =
                        newTimelineStart - _initialTimelineStart.TotalSeconds;

                    clip.TimelineStart = TimeSpan.FromSeconds(newTimelineStart);

                    clip.SourceStart = TimeSpan.FromSeconds(
                        _initialSourceStart.TotalSeconds + actualDelta);

                    break;
                }

            case DragMode.TrimEnd:
                {
                    double initialTimelineEnd =
                        _initialTimelineStart.TotalSeconds +
                        (_initialSourceEnd - _initialSourceStart).TotalSeconds;

                    double newTimelineEnd =
                        initialTimelineEnd + deltaSeconds;

                    double maximumTimelineEnd =
                        _initialTimelineStart.TotalSeconds +
                        (DurationSeconds - _initialTimelineStart.TotalSeconds);

                    newTimelineEnd = Math.Clamp(
                        newTimelineEnd,
                        _initialTimelineStart.TotalSeconds + MinimumClipDuration,
                        maximumTimelineEnd);

                    if (TrySnapTime(
                        newTimelineEnd,
                        clip,
                        pixelsPerSecond,
                        out double snappedPosition))
                    {
                        newTimelineEnd = Math.Clamp(
                            snappedPosition,
                            _initialTimelineStart.TotalSeconds + MinimumClipDuration,
                            maximumTimelineEnd);

                        _snapGuideSeconds = newTimelineEnd;
                    }

                    double snappedDelta =
                        newTimelineEnd - initialTimelineEnd;

                    double newSourceEnd =
                        _initialSourceEnd.TotalSeconds + snappedDelta;

                    clip.SourceEnd = TimeSpan.FromSeconds(
                        Math.Max(
                            _initialSourceStart.TotalSeconds + MinimumClipDuration,
                            newSourceEnd));

                    break;
                }
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_dragMode == DragMode.None)
            return;

        e.Pointer.Capture(null);

        _dragMode = DragMode.None;
        _draggedClip = null;
        _snapGuideSeconds = null;

        InvalidateVisual();
        e.Handled = true;
    }

    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));

        return $"{(int)time.TotalMinutes:D2}:{time.Seconds:D2}";
    }
}
