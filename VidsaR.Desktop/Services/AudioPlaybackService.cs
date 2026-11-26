
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VidsaR.Core.Models;

namespace VidsaR.Desktop.Services;

public class AudioPlaybackService : IDisposable
{
    private AudioFileReader? _audioFile;
    private WaveOutEvent? _outputDevice;
    private Timer? _positionTimer;

    private TimeSpan _loopStart;
    private TimeSpan? _selectionEnd;

    private readonly object _syncLock = new();

    private readonly List<AudioFileReader> _timelineReaders = [];
    private List<AudioClip> _timelineClips = [];

    private Stopwatch? _timelineStopwatch;
    private bool _isTimelinePlayback;
    private TimeSpan _timelineDuration;
    private TimeSpan _timelinePosition;
    private TimeSpan _timelineStartPosition;

    private const int TimelineSampleRate = 44100;
    private const int TimelineChannels = 2;

    private static readonly TimeSpan BoundaryTolerance =
        TimeSpan.FromMilliseconds(100);

    public bool IsPlaying =>
        _outputDevice?.PlaybackState == PlaybackState.Playing;

    public bool IsTimelinePlayback => _isTimelinePlayback;

    public TimeSpan CurrentTime =>
        _isTimelinePlayback
            ? _timelinePosition
            : _audioFile?.CurrentTime ?? TimeSpan.Zero;

    public event Action<TimeSpan>? PositionChanged;

    public void Play(
        string filePath,
        TimeSpan start,
        TimeSpan? end = null)
    {
        Stop();

        if (!File.Exists(filePath))
            throw new FileNotFoundException(
                "Аудиофайл не найден.",
                filePath);

        var audioFile = new AudioFileReader(filePath);

        if (start < TimeSpan.Zero || start >= audioFile.TotalTime)
        {
            audioFile.Dispose();
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        if (end.HasValue &&
            (end.Value <= start || end.Value > audioFile.TotalTime))
        {
            audioFile.Dispose();
            throw new ArgumentOutOfRangeException(nameof(end));
        }

        _audioFile = audioFile;
        _loopStart = start;
        _selectionEnd = end;
        _isTimelinePlayback = false;

        _audioFile.CurrentTime = start;

        PositionChanged?.Invoke(start);

        _outputDevice = new WaveOutEvent();
        _outputDevice.Init(_audioFile);
        _outputDevice.Play();

        _positionTimer = new Timer(
            CheckPlaybackPosition,
            null,
            0,
            30);
    }

    public void PlayTimeline(IEnumerable<AudioClip> clips)
    {
        if (clips == null)
            throw new ArgumentNullException(nameof(clips));

        var timelineClips = clips
            .Select(clip => new AudioClip
            {
                Id = clip.Id,
                SourceFilePath = clip.SourceFilePath,
                SourceStart = clip.SourceStart,
                SourceEnd = clip.SourceEnd,
                TimelineStart = clip.TimelineStart
            })
            .ToList();

        StartTimeline(timelineClips, TimeSpan.Zero, false);
    }

    public void SeekTimeline(TimeSpan position)
    {
        if (!_isTimelinePlayback || _timelineClips.Count == 0)
            return;

        position = TimeSpan.FromTicks(
            Math.Clamp(
                position.Ticks,
                0,
                _timelineDuration.Ticks));

        bool wasPaused =
            _outputDevice?.PlaybackState == PlaybackState.Paused;

        var clips = _timelineClips
            .Select(clip => new AudioClip
            {
                Id = clip.Id,
                SourceFilePath = clip.SourceFilePath,
                SourceStart = clip.SourceStart,
                SourceEnd = clip.SourceEnd,
                TimelineStart = clip.TimelineStart
            })
            .ToList();

        StartTimeline(clips, position, wasPaused);
    }

    private void StartTimeline(
        List<AudioClip> timelineClips,
        TimeSpan startPosition,
        bool startPaused)
    {
        Stop();

        timelineClips = timelineClips
            .Where(clip =>
                clip.Duration > TimeSpan.Zero &&
                clip.TimelineStart >= TimeSpan.Zero)
            .OrderBy(clip => clip.TimelineStart)
            .ToList();

        if (timelineClips.Count == 0)
            throw new InvalidOperationException(
                "Timeline не содержит аудиоклипов.");

        _timelineClips = timelineClips;

        var outputFormat = WaveFormat.CreateIeeeFloatWaveFormat(
            TimelineSampleRate,
            TimelineChannels);

        var mixer = new MixingSampleProvider(outputFormat)
        {
            ReadFully = true
        };

        try
        {
            foreach (var clip in timelineClips)
            {
                if (!File.Exists(clip.SourceFilePath))
                    throw new FileNotFoundException(
                        "Исходный аудиофайл не найден.",
                        clip.SourceFilePath);

                var reader = new AudioFileReader(clip.SourceFilePath);

                try
                {
                    TimeSpan sourceStart = clip.SourceStart;
                    TimeSpan sourceEnd = clip.SourceEnd;
                    TimeSpan fileDuration = reader.TotalTime;

                    if (sourceStart < TimeSpan.Zero)
                        throw new InvalidOperationException(
                            $"Начало клипа меньше нуля: {clip.SourceFilePath}");

                    if (sourceStart >= fileDuration)
                        throw new InvalidOperationException(
                            $"Начало клипа выходит за длительность файла: {clip.SourceFilePath}");

                    // Учитываем небольшую погрешность длительности.
                    if (sourceEnd > fileDuration)
                    {
                        if (sourceEnd - fileDuration <= BoundaryTolerance)
                        {
                            sourceEnd = fileDuration;
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                $"Конец клипа выходит за длительность файла. " +
                                $"Клип: {sourceEnd}, файл: {fileDuration}. " +
                                $"Файл: {clip.SourceFilePath}");
                        }
                    }

                    if (sourceEnd <= sourceStart)
                        throw new InvalidOperationException(
                            $"Длительность клипа равна нулю: {clip.SourceFilePath}");

                    // Обновляем границы используемого клипа.
                    clip.SourceStart = sourceStart;
                    clip.SourceEnd = sourceEnd;

                    if (clip.TimelineEnd > _timelineDuration)
                        _timelineDuration = clip.TimelineEnd;

                    if (clip.TimelineEnd <= startPosition)
                    {
                        reader.Dispose();
                        continue;
                    }

                    TimeSpan elapsedInsideClip =
                        startPosition > clip.TimelineStart
                            ? startPosition - clip.TimelineStart
                            : TimeSpan.Zero;

                    TimeSpan readerPosition =
                        sourceStart + elapsedInsideClip;

                    if (readerPosition >= sourceEnd)
                    {
                        reader.Dispose();
                        continue;
                    }

                    reader.CurrentTime = readerPosition;

                    ISampleProvider sampleProvider = reader;

                    if (sampleProvider.WaveFormat.Channels == 1)
                    {
                        sampleProvider =
                            new MonoToStereoSampleProvider(sampleProvider);
                    }
                    else if (sampleProvider.WaveFormat.Channels != 2)
                    {
                        throw new NotSupportedException(
                            "Поддерживаются только моно- и стереофайлы.");
                    }

                    if (sampleProvider.WaveFormat.SampleRate != TimelineSampleRate)
                    {
                        sampleProvider = new WdlResamplingSampleProvider(
                            sampleProvider,
                            TimelineSampleRate);
                    }

                    TimeSpan delay =
                        clip.TimelineStart > startPosition
                            ? clip.TimelineStart - startPosition
                            : TimeSpan.Zero;

                    TimeSpan remainingDuration =
                        sourceEnd - readerPosition;

                    var offsetProvider = new OffsetSampleProvider(
                        sampleProvider)
                    {
                        DelayBy = delay,
                        Take = remainingDuration
                    };

                    mixer.AddMixerInput(offsetProvider);

                    _timelineReaders.Add(reader);
                }
                catch
                {
                    if (!_timelineReaders.Contains(reader))
                        reader.Dispose();

                    throw;
                }
            }

            if (_timelineDuration <= TimeSpan.Zero)
                throw new InvalidOperationException(
                    "Длительность Timeline равна нулю.");

            if (startPosition >= _timelineDuration)
                startPosition = TimeSpan.Zero;

            _timelineStartPosition = startPosition;
            _timelinePosition = startPosition;
            _isTimelinePlayback = true;

            _outputDevice = new WaveOutEvent();
            _outputDevice.Init(mixer);

            _timelineStopwatch = Stopwatch.StartNew();

            if (startPaused)
            {
                _outputDevice.Play();
                _outputDevice.Pause();
                _timelineStopwatch.Stop();
            }
            else
            {
                _outputDevice.Play();
            }

            _positionTimer = new Timer(
                CheckPlaybackPosition,
                null,
                0,
                30);

            PositionChanged?.Invoke(_timelinePosition);
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public void Pause()
    {
        _outputDevice?.Pause();

        if (_isTimelinePlayback)
            _timelineStopwatch?.Stop();
    }

    public void Resume()
    {
        if (_isTimelinePlayback)
            _timelineStopwatch?.Start();

        _outputDevice?.Play();
    }

    public void Stop()
    {
        lock (_syncLock)
        {
            _positionTimer?.Dispose();
            _positionTimer = null;

            _timelineStopwatch?.Stop();
            _timelineStopwatch = null;

            _outputDevice?.Stop();
            _outputDevice?.Dispose();
            _outputDevice = null;

            _audioFile?.Dispose();
            _audioFile = null;

            foreach (var reader in _timelineReaders)
                reader.Dispose();

            _timelineReaders.Clear();

            _timelineClips.Clear();

            _loopStart = TimeSpan.Zero;
            _selectionEnd = null;

            _isTimelinePlayback = false;
            _timelineDuration = TimeSpan.Zero;
            _timelinePosition = TimeSpan.Zero;
            _timelineStartPosition = TimeSpan.Zero;
        }

        PositionChanged?.Invoke(TimeSpan.Zero);
    }

    private void CheckPlaybackPosition(object? state)
    {
        lock (_syncLock)
        {
            if (_outputDevice == null)
                return;

            if (_outputDevice.PlaybackState == PlaybackState.Paused)
                return;

            if (_isTimelinePlayback)
            {
                CheckTimelinePosition();
                return;
            }

            if (_audioFile == null)
                return;

            TimeSpan currentTime = _audioFile.CurrentTime;
            TimeSpan totalTime = _audioFile.TotalTime;

            bool selectionFinished =
                _selectionEnd.HasValue &&
                currentTime >= _selectionEnd.Value;

            bool fileFinished =
                _outputDevice.PlaybackState == PlaybackState.Stopped &&
                currentTime >= totalTime - TimeSpan.FromMilliseconds(100);

            if (selectionFinished || fileFinished)
            {
                _audioFile.CurrentTime = _loopStart;

                if (fileFinished)
                    _outputDevice.Play();

                PositionChanged?.Invoke(_loopStart);
                return;
            }

            PositionChanged?.Invoke(currentTime);
        }
    }

    private void CheckTimelinePosition()
    {
        if (_timelineStopwatch == null)
            return;

        _timelinePosition =
            _timelineStartPosition + _timelineStopwatch.Elapsed;

        if (_timelinePosition >= _timelineDuration)
        {
            _timelinePosition = _timelineDuration;

            PositionChanged?.Invoke(_timelinePosition);

            _positionTimer?.Dispose();
            _positionTimer = null;

            _timelineStopwatch.Stop();

            _outputDevice?.Stop();
            _outputDevice?.Dispose();
            _outputDevice = null;

            foreach (var reader in _timelineReaders)
                reader.Dispose();

            _timelineReaders.Clear();

            _timelineStopwatch = null;
            _isTimelinePlayback = false;

            return;
        }

        PositionChanged?.Invoke(_timelinePosition);
    }

    public void Dispose()
    {
        Stop();
    }
}
