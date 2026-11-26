
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VidsaR.Core.Models;
using VidsaR.Desktop.Controls;
using VidsaR.Desktop.Services;
using VidsaR.Desktop.ViewModels;
using VidsaR.Infrastructure.Metadata;
using VidsaR.Infrastructure.MediaProcessing;

namespace VidsaR.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly MetadataWriter _metadataWriter = new();
    private readonly FFmpegService _ffmpegService = new();
    private readonly WaveformService _waveformService = new();
    private readonly AudioPlaybackService _audioPlaybackService = new();

    private TimeSpan _currentFileDuration;
    private bool _isPaused;
    private bool _isTimelineMode;

    public MainWindow()
    {
        InitializeComponent();

        var waveform = this.FindControl<WaveformControl>("WaveformEditor");

        if (waveform != null)
        {
            waveform.SelectionCompleted += Waveform_SelectionCompleted;
            waveform.SeekRequested += Waveform_SeekRequested;
        }

        _audioPlaybackService.PositionChanged += Playback_PositionChanged;

        UpdatePlayPauseButton();
        UpdateTimelinePlayPauseButton();

        Opened += MainWindow_Opened;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        var waveform = this.FindControl<WaveformControl>("WaveformEditor");

        if (waveform != null)
        {
            waveform.SelectionCompleted -= Waveform_SelectionCompleted;
            waveform.SeekRequested -= Waveform_SeekRequested;
        }

        _audioPlaybackService.PositionChanged -= Playback_PositionChanged;
        _audioPlaybackService.Dispose();
    }

    private void UpdatePlayPauseButton()
    {
        var button = this.FindControl<Button>("PlayPauseButton");

        if (button == null)
            return;

        button.Content = !_isTimelineMode && _audioPlaybackService.IsPlaying
            ? "⏸ Пауза"
            : "▶ Воспроизвести";
    }

    private void UpdateTimelinePlayPauseButton()
    {
        var button = this.FindControl<Button>("TimelinePlayPauseButton");

        if (button == null)
            return;

        if (_isTimelineMode && _audioPlaybackService.IsPlaying)
        {
            button.Content = "⏸ Пауза Timeline";
        }
        else if (_isTimelineMode && _isPaused)
        {
            button.Content = "▶ Продолжить Timeline";
        }
        else
        {
            button.Content = "▶ Воспроизвести Timeline";
        }
    }

    private void UpdatePlaybackButtons()
    {
        UpdatePlayPauseButton();
        UpdateTimelinePlayPauseButton();
    }

    private void StopTimelinePlayback()
    {
        if (!_isTimelineMode)
            return;

        _audioPlaybackService.Stop();

        _isTimelineMode = false;
        _isPaused = false;

        UpdatePlaybackButtons();
    }

    private void DiscordMode_Click(object? sender, RoutedEventArgs e)
    {
        var waveform = this.FindControl<WaveformControl>("WaveformEditor");

        if (waveform == null || _currentFileDuration <= TimeSpan.Zero)
            return;

        _audioPlaybackService.Stop();

        _isTimelineMode = false;
        _isPaused = false;

        waveform.DiscordMode = !waveform.DiscordMode;

        double selectionStart = Math.Min(
            waveform.SelectionStart,
            waveform.SelectionEnd);

        waveform.PlayheadPosition = selectionStart;

        TimeSpan position = TimeSpan.FromTicks(
            (long)(_currentFileDuration.Ticks * selectionStart));

        UpdatePlaybackTime(position);
        UpdatePlaybackButtons();
    }

    private void Playback_PositionChanged(TimeSpan position)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var timeText = this.FindControl<TextBlock>("PlaybackTimeText");

            if (_isTimelineMode)
            {
                double timelineDuration = 0;

                if (DataContext is MainViewModel viewModel)
                {
                    timelineDuration = viewModel.TimelineDurationSeconds;
                }

                var duration = TimeSpan.FromSeconds(
                    Math.Max(0, timelineDuration));

                var timeline = this.GetVisualDescendants()
                    .OfType<TimelineControl>()
                    .FirstOrDefault();

                if (timeline != null)
                {
                    timeline.PlayheadSeconds = Math.Clamp(
                        position.TotalSeconds,
                        0,
                        timelineDuration);
                }

                if (timeText != null)
                {
                    timeText.Text =
                        $"{FormatTime(position)} / {FormatTime(duration)}";
                }

                UpdatePlaybackButtons();
                return;
            }

            var waveform = this.FindControl<WaveformControl>("WaveformEditor");

            if (waveform != null && _currentFileDuration > TimeSpan.Zero)
            {
                waveform.PlayheadPosition = Math.Clamp(
                    position.TotalSeconds / _currentFileDuration.TotalSeconds,
                    0,
                    1);
            }

            if (timeText != null)
            {
                timeText.Text =
                    $"{FormatTime(position)} / {FormatTime(_currentFileDuration)}";
            }

            UpdatePlaybackButtons();
        });
    }

    private static string FormatTime(TimeSpan time)
    {
        return $"{(int)time.TotalMinutes:D2}:{time.Seconds:D2}.{time.Milliseconds:D3}";
    }

    private async void MainWindow_Opened(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        viewModel.IsInitializing = true;
        viewModel.FFmpegStatus = "Проверка FFmpeg...";

        var loadingStarted = DateTime.UtcNow;

        try
        {
            var isAvailable = await _ffmpegService.IsAvailableAsync();

            viewModel.FFmpegStatus = isAvailable
                ? "● FFmpeg готов к работе"
                : "● Не удалось установить FFmpeg";
        }
        catch (Exception)
        {
            viewModel.FFmpegStatus = "● Ошибка установки FFmpeg";
        }
        finally
        {
            var elapsed = DateTime.UtcNow - loadingStarted;
            var remaining = TimeSpan.FromMilliseconds(1500) - elapsed;

            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining);

            viewModel.IsInitializing = false;
        }
    }

    private async void AddMp3ToTimeline_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        if (!viewModel.IsAudioFile)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Добавить MP3 в Timeline",
                AllowMultiple = true,
                FileTypeFilter =
                [
                    new FilePickerFileType("MP3")
                    {
                        Patterns = ["*.mp3"]
                    }
                ]
            });

        if (files.Count == 0)
            return;

        double appendPosition = viewModel.AudioClips.Count > 0
            ? viewModel.AudioClips.Max(
                clip => clip.TimelineEnd.TotalSeconds)
            : 0;

        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();

            if (string.IsNullOrWhiteSpace(path))
                continue;

            try
            {
                var duration = await _ffmpegService.GetDurationAsync(path);

                if (duration <= TimeSpan.Zero)
                    continue;

                var clip = new AudioClip
                {
                    SourceFilePath = path,
                    SourceStart = TimeSpan.Zero,
                    SourceEnd = duration,
                    TimelineStart = TimeSpan.FromSeconds(appendPosition)
                };

                viewModel.AudioClips.Add(clip);

                appendPosition += duration.TotalSeconds;
            }
            catch (Exception)
            {
                // Пропускаем файл, который не удалось прочитать.
            }
        }
    }

    private async void SelectFile_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Выберите аудио или видео",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Аудио и видео")
                    {
                        Patterns =
                        [
                            "*.mp3", "*.wav", "*.flac", "*.aac",
                            "*.ogg", "*.m4a", "*.wma",
                            "*.mp4", "*.mkv", "*.avi", "*.mov",
                            "*.wmv", "*.webm"
                        ]
                    }
                ]
            });

        if (files.Count == 0)
            return;

        var file = files[0];
        var path = file.TryGetLocalPath();

        if (string.IsNullOrWhiteSpace(path))
            return;

        if (DataContext is not MainViewModel viewModel)
            return;

        _audioPlaybackService.Stop();

        _isTimelineMode = false;
        _isPaused = false;

        UpdatePlaybackButtons();

        _currentFileDuration = TimeSpan.Zero;
        viewModel.FileDurationSeconds = 0;

        viewModel.AudioClips.Clear();

        var waveform = this.FindControl<WaveformControl>("WaveformEditor");

        if (waveform != null)
        {
            waveform.DiscordMode = false;
            waveform.SelectionStart = 0;
            waveform.SelectionEnd = 0;
            waveform.PlayheadPosition = 0;
            waveform.DurationSeconds = 0;
        }

        var timeline = this.GetVisualDescendants()
            .OfType<TimelineControl>()
            .FirstOrDefault();

        if (timeline != null)
            timeline.PlayheadSeconds = 0;

        var extension = Path.GetExtension(path).ToLowerInvariant();

        var audioExtensions = new[]
        {
            ".mp3", ".wav", ".flac", ".aac",
            ".ogg", ".m4a", ".wma"
        };

        var isAudio = audioExtensions.Contains(extension);

        viewModel.SelectedFileName = file.Name;
        viewModel.SelectedFilePath = path;
        viewModel.FileType = isAudio ? "Аудио" : "Видео";
        viewModel.IsAudioFile = isAudio;
        viewModel.HasSelectedFile = true;
        viewModel.FileDuration = "Определение...";
        viewModel.WaveformPeaks = [];

        viewModel.ClearMetadata();

        try
        {
            var duration = await _ffmpegService.GetDurationAsync(path);

            _currentFileDuration = duration;
            viewModel.FileDurationSeconds = duration.TotalSeconds;

            if (waveform != null)
                waveform.DurationSeconds = duration.TotalSeconds;

            viewModel.FileDuration = duration.ToString(@"hh\:mm\:ss\.fff");

            if (duration.TotalHours >= 24)
            {
                viewModel.FileDuration =
                    $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}.{duration.Milliseconds:D3}";
            }

            if (isAudio && duration > TimeSpan.Zero)
            {
                viewModel.AudioClips.Add(new AudioClip
                {
                    SourceFilePath = path,
                    SourceStart = TimeSpan.Zero,
                    SourceEnd = duration,
                    TimelineStart = TimeSpan.Zero
                });
            }
        }
        catch (Exception)
        {
            viewModel.FileDuration = "Не удалось определить";
            _currentFileDuration = TimeSpan.Zero;
            viewModel.FileDurationSeconds = 0;
            viewModel.AudioClips.Clear();
        }

        if (isAudio)
        {
            try
            {
                viewModel.WaveformPeaks =
                    await _waveformService.GetWaveformAsync(path);
            }
            catch (Exception)
            {
                viewModel.WaveformPeaks = [];
            }

            try
            {
                viewModel.LoadMetadata(path);
            }
            catch (Exception)
            {
                viewModel.ClearMetadata();
            }
        }

        UpdatePlaybackTime(TimeSpan.Zero);
    }

    private void UpdatePlaybackTime(TimeSpan position)
    {
        var timeText = this.FindControl<TextBlock>("PlaybackTimeText");

        if (timeText != null)
        {
            timeText.Text =
                $"{FormatTime(position)} / {FormatTime(_currentFileDuration)}";
        }
    }

    private void Play_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        if (!viewModel.HasSelectedFile || !viewModel.IsAudioFile)
            return;

        if (_isTimelineMode)
        {
            _audioPlaybackService.Stop();

            _isTimelineMode = false;
            _isPaused = false;
        }

        if (_audioPlaybackService.IsPlaying)
        {
            _audioPlaybackService.Pause();
            _isPaused = true;

            UpdatePlaybackButtons();
            return;
        }

        if (_isPaused)
        {
            _audioPlaybackService.Resume();
            _isPaused = false;

            UpdatePlaybackButtons();
            return;
        }

        PlaySelectedArea(viewModel);
    }

    private void TimelinePlay_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        if (viewModel.AudioClips.Count == 0)
            return;

        if (_isTimelineMode && _audioPlaybackService.IsPlaying)
        {
            _audioPlaybackService.Pause();
            _isPaused = true;

            UpdatePlaybackButtons();
            return;
        }

        if (_isTimelineMode && _isPaused)
        {
            _audioPlaybackService.Resume();
            _isPaused = false;

            UpdatePlaybackButtons();
            return;
        }

        _audioPlaybackService.Stop();

        _isTimelineMode = true;
        _isPaused = false;

        try
        {
            _audioPlaybackService.PlayTimeline(viewModel.AudioClips);

            var timeline = this.GetVisualDescendants()
                .OfType<TimelineControl>()
                .FirstOrDefault();

            if (timeline != null)
                timeline.PlayheadSeconds = 0;

            UpdatePlaybackTime(TimeSpan.Zero);
        }
        catch (Exception ex)
        {
            _audioPlaybackService.Stop();

            _isTimelineMode = false;
            _isPaused = false;

            viewModel.FFmpegStatus = $"Ошибка Timeline: {ex.Message}";
        }

        UpdatePlaybackButtons();
    }

    private void PlaySelectedArea(MainViewModel viewModel)
    {
        if (_currentFileDuration <= TimeSpan.Zero)
            return;

        var start = TimeSpan.Zero;
        TimeSpan? end = null;

        var waveform = this.FindControl<WaveformControl>("WaveformEditor");

        if (waveform != null)
        {
            double selectionStart = Math.Min(
                waveform.SelectionStart,
                waveform.SelectionEnd);

            double selectionEnd = Math.Max(
                waveform.SelectionStart,
                waveform.SelectionEnd);

            if (selectionEnd - selectionStart > 0.001)
            {
                start = TimeSpan.FromTicks(
                    (long)(_currentFileDuration.Ticks * selectionStart));

                end = TimeSpan.FromTicks(
                    (long)(_currentFileDuration.Ticks * selectionEnd));
            }
        }

        try
        {
            _audioPlaybackService.Play(
                viewModel.SelectedFilePath,
                start,
                end);

            _isTimelineMode = false;
            _isPaused = false;
        }
        catch (Exception)
        {
            _isTimelineMode = false;
            _isPaused = false;
        }

        UpdatePlaybackButtons();
    }

    private void Waveform_SeekRequested(double position)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        if (!viewModel.HasSelectedFile ||
            !viewModel.IsAudioFile ||
            _currentFileDuration <= TimeSpan.Zero)
        {
            return;
        }

        if (_isTimelineMode)
        {
            _audioPlaybackService.Stop();

            _isTimelineMode = false;
            _isPaused = false;
        }

        position = Math.Clamp(position, 0, 1);

        var waveform = this.FindControl<WaveformControl>("WaveformEditor");

        if (waveform == null)
            return;

        double selectionStart = Math.Min(
            waveform.SelectionStart,
            waveform.SelectionEnd);

        double selectionEnd = Math.Max(
            waveform.SelectionStart,
            waveform.SelectionEnd);

        bool hasSelection =
            selectionEnd - selectionStart > 0.001;

        TimeSpan start = TimeSpan.FromTicks(
            (long)(_currentFileDuration.Ticks * position));

        TimeSpan? end = null;

        if (hasSelection)
        {
            if (position >= selectionStart && position < selectionEnd)
            {
                end = TimeSpan.FromTicks(
                    (long)(_currentFileDuration.Ticks * selectionEnd));
            }
            else
            {
                waveform.SelectionStart = 0;
                waveform.SelectionEnd = 0;
            }
        }

        try
        {
            _audioPlaybackService.Play(
                viewModel.SelectedFilePath,
                start,
                end);

            _isPaused = false;

            waveform.PlayheadPosition = position;
            UpdatePlaybackTime(start);
        }
        catch (Exception)
        {
            _isPaused = false;
        }

        UpdatePlaybackButtons();
    }

    private void Waveform_SelectionCompleted()
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        if (!viewModel.HasSelectedFile || !viewModel.IsAudioFile)
            return;

        if (_isTimelineMode)
        {
            _audioPlaybackService.Stop();

            _isTimelineMode = false;
            _isPaused = false;
        }

        if (_audioPlaybackService.IsPlaying)
        {
            PlaySelectedArea(viewModel);
            return;
        }

        if (_isPaused)
        {
            _audioPlaybackService.Stop();
            _isPaused = false;

            var waveform = this.FindControl<WaveformControl>("WaveformEditor");

            if (waveform != null)
            {
                double selectionStart = Math.Min(
                    waveform.SelectionStart,
                    waveform.SelectionEnd);

                UpdatePlaybackTime(
                    TimeSpan.FromTicks(
                        (long)(_currentFileDuration.Ticks * selectionStart)));

                waveform.PlayheadPosition = selectionStart;
            }

            UpdatePlaybackButtons();
        }
    }

    private void Stop_Click(object? sender, RoutedEventArgs e)
    {
        _audioPlaybackService.Stop();

        _isTimelineMode = false;
        _isPaused = false;

        var timeline = this.GetVisualDescendants()
            .OfType<TimelineControl>()
            .FirstOrDefault();

        if (timeline != null)
            timeline.PlayheadSeconds = 0;

        UpdatePlaybackTime(TimeSpan.Zero);
        UpdatePlaybackButtons();
    }

    private async void SelectCover_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Выберите обложку",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Изображения")
                    {
                        Patterns =
                        [
                            "*.jpg", "*.jpeg", "*.png",
                            "*.bmp", "*.webp"
                        ]
                    }
                ]
            });

        if (files.Count == 0)
            return;

        var path = files[0].TryGetLocalPath();

        if (string.IsNullOrWhiteSpace(path))
            return;

        if (DataContext is not MainViewModel viewModel)
            return;

        try
        {
            var imageData = await File.ReadAllBytesAsync(path);
            viewModel.SetCoverImage(imageData);
        }
        catch (Exception)
        {
            // Предыдущая обложка остаётся без изменений.
        }
    }

    private async void SaveCopy_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        if (!viewModel.HasSelectedFile || !viewModel.IsAudioFile)
            return;

        if (!string.Equals(
            Path.GetExtension(viewModel.SelectedFilePath),
            ".mp3",
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var suggestedName = string.IsNullOrWhiteSpace(viewModel.Title)
            ? Path.GetFileNameWithoutExtension(viewModel.SelectedFilePath)
            : viewModel.Title;

        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            suggestedName = suggestedName.Replace(invalidCharacter, '_');
        }

        var destination = await StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Сохранить MP3-копию",
                SuggestedFileName = suggestedName + ".mp3",
                DefaultExtension = "mp3",
                FileTypeChoices =
                [
                    new FilePickerFileType("MP3")
                    {
                        Patterns = ["*.mp3"]
                    }
                ]
            });

        if (destination == null)
            return;

        var destinationPath = destination.TryGetLocalPath();

        if (string.IsNullOrWhiteSpace(destinationPath))
            return;

        int? year = null;

        if (int.TryParse(viewModel.Year, out var parsedYear) &&
            parsedYear > 0 &&
            parsedYear <= 9999)
        {
            year = parsedYear;
        }

        var metadata = new MediaMetadata
        {
            Title = viewModel.Title,
            Artist = viewModel.Artist,
            Album = viewModel.Album,
            Genre = viewModel.Genre,
            Year = year,
            CoverArt = viewModel.CoverArt
        };

        var waveform = this.FindControl<WaveformControl>("WaveformEditor");

        double selectionStart = 0;
        double selectionEnd = 0;

        if (waveform != null)
        {
            selectionStart = Math.Min(
                waveform.SelectionStart,
                waveform.SelectionEnd);

            selectionEnd = Math.Max(
                waveform.SelectionStart,
                waveform.SelectionEnd);
        }

        bool hasSelection = selectionEnd - selectionStart > 0.001;

        string? temporaryPath = null;

        SaveStatusOverlay.IsVisible = true;
        SavingState.IsVisible = true;
        SaveSuccessState.IsVisible = false;
        SaveErrorState.IsVisible = false;
        SaveStatusOkButton.IsVisible = false;

        SaveProgressBar.Value = 0;
        SaveProgressText.Text = "Сохраняем файл... 0%";
        SaveProgressDescription.Text = hasSelection
            ? "Подготовка аудио..."
            : "Копирование файла и запись метаданных...";

        await Task.Delay(100);

        try
        {
            _audioPlaybackService.Stop();

            _isTimelineMode = false;
            _isPaused = false;

            UpdatePlaybackButtons();

            if (hasSelection)
            {
                if (_currentFileDuration <= TimeSpan.Zero)
                    throw new InvalidOperationException(
                        "Не удалось определить длительность исходного файла.");

                var start = TimeSpan.FromTicks(
                    (long)(_currentFileDuration.Ticks * selectionStart));

                var end = TimeSpan.FromTicks(
                    (long)(_currentFileDuration.Ticks * selectionEnd));

                temporaryPath = Path.Combine(
                    Path.GetTempPath(),
                    $"VidsaR_{Guid.NewGuid():N}.mp3");

                SaveProgressDescription.Text = "Экспорт аудиофрагмента...";

                var progress = new Progress<double>(percentage =>
                {
                    percentage = Math.Clamp(percentage, 0, 100);

                    SaveProgressBar.Value = percentage;
                    SaveProgressText.Text =
                        $"Обработка аудио... {percentage:0}%";
                });

                await _ffmpegService.ExportAudioSegmentAsync(
                    viewModel.SelectedFilePath,
                    temporaryPath,
                    start,
                    end,
                    progress);

                SaveProgressBar.Value = 100;
                SaveProgressText.Text = "Обработка аудио... 100%";
                SaveProgressDescription.Text =
                    "Экспорт завершён. Добавляем метаданные...";

                await Task.Run(() =>
                    _metadataWriter.SaveCopy(
                        temporaryPath,
                        destinationPath,
                        metadata));
            }
            else
            {
                SaveProgressDescription.Text =
                    "Копирование файла и запись метаданных...";

                await Task.Run(() =>
                    _metadataWriter.SaveCopy(
                        viewModel.SelectedFilePath,
                        destinationPath,
                        metadata));

                SaveProgressBar.Value = 100;
                SaveProgressText.Text = "Сохранение завершено... 100%";
            }

            SavingState.IsVisible = false;
            SaveSuccessState.IsVisible = true;
            SaveErrorState.IsVisible = false;
            SaveStatusOkButton.IsVisible = true;
        }
        catch (Exception ex)
        {
            SavingState.IsVisible = false;
            SaveSuccessState.IsVisible = false;
            SaveErrorState.IsVisible = true;
            SaveStatusOkButton.IsVisible = true;

            var errorText = this.FindControl<TextBlock>("SaveErrorText");

            if (errorText != null)
            {
                errorText.Text = ex.Message;
            }
        }
        finally
        {
            if (temporaryPath != null && File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // Не прерываем работу из-за ошибки удаления временного файла.
                }
            }
        }
    }

    // Экспорт всей Timeline в один MP3.
    private async void ExportTimeline_Click(
        object? sender,
        RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        if (!viewModel.IsAudioFile || viewModel.AudioClips.Count == 0)
            return;

        var destination = await StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "Экспорт Timeline в MP3",
                SuggestedFileName = "VidsaR_Timeline.mp3",
                DefaultExtension = "mp3",
                FileTypeChoices =
                [
                    new FilePickerFileType("MP3")
                    {
                        Patterns = ["*.mp3"]
                    }
                ]
            });

        if (destination == null)
            return;

        var destinationPath = destination.TryGetLocalPath();

        if (string.IsNullOrWhiteSpace(destinationPath))
            return;

        if (!string.Equals(
            Path.GetExtension(destinationPath),
            ".mp3",
            StringComparison.OrdinalIgnoreCase))
        {
            destinationPath += ".mp3";
        }

        SaveStatusOverlay.IsVisible = true;
        SavingState.IsVisible = true;
        SaveSuccessState.IsVisible = false;
        SaveErrorState.IsVisible = false;
        SaveStatusOkButton.IsVisible = false;

        SaveProgressBar.Value = 0;
        SaveProgressText.Text = "Экспорт Timeline... 0%";
        SaveProgressDescription.Text = "Подготовка аудиодорожек...";

        await Task.Delay(100);

        try
        {
            _audioPlaybackService.Stop();

            _isTimelineMode = false;
            _isPaused = false;

            UpdatePlaybackButtons();

            var timeline = this.GetVisualDescendants()
                .OfType<TimelineControl>()
                .FirstOrDefault();

            if (timeline != null)
                timeline.PlayheadSeconds = 0;

            UpdatePlaybackTime(TimeSpan.Zero);

            SaveProgressDescription.Text =
                "Объединение аудиоклипов...";

            var progress = new Progress<double>(percentage =>
            {
                percentage = Math.Clamp(percentage, 0, 100);

                SaveProgressBar.Value = percentage;
                SaveProgressText.Text =
                    $"Экспорт Timeline... {percentage:0}%";

                SaveProgressDescription.Text =
                    percentage >= 100
                        ? "Завершение экспорта..."
                        : "Объединение аудиоклипов...";
            });

            await _ffmpegService.ExportTimelineAsync(
                viewModel.AudioClips.ToList(),
                destinationPath,
                progress);

            SaveProgressBar.Value = 100;
            SaveProgressText.Text = "Экспорт завершён... 100%";
            SaveProgressDescription.Text =
                "MP3-файл успешно создан.";

            SavingState.IsVisible = false;
            SaveSuccessState.IsVisible = true;
            SaveErrorState.IsVisible = false;
            SaveStatusOkButton.IsVisible = true;
        }
        catch (Exception ex)
        {
            SavingState.IsVisible = false;
            SaveSuccessState.IsVisible = false;
            SaveErrorState.IsVisible = true;
            SaveStatusOkButton.IsVisible = true;

            var errorText = this.FindControl<TextBlock>("SaveErrorText");

            if (errorText != null)
                errorText.Text = ex.Message;
        }
    }

    private void RemoveFile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        _audioPlaybackService.Stop();

        _isTimelineMode = false;
        _isPaused = false;

        UpdatePlaybackButtons();

        _currentFileDuration = TimeSpan.Zero;
        viewModel.FileDurationSeconds = 0;

        viewModel.AudioClips.Clear();

        var waveform = this.FindControl<WaveformControl>("WaveformEditor");

        if (waveform != null)
        {
            waveform.DiscordMode = false;
            waveform.SelectionStart = 0;
            waveform.SelectionEnd = 0;
            waveform.PlayheadPosition = 0;
            waveform.DurationSeconds = 0;
        }

        var timeline = this.GetVisualDescendants()
            .OfType<TimelineControl>()
            .FirstOrDefault();

        if (timeline != null)
            timeline.PlayheadSeconds = 0;

        viewModel.SelectedFileName = "Файл не выбран";
        viewModel.SelectedFilePath = string.Empty;
        viewModel.FileType = string.Empty;
        viewModel.FileDuration = "00:00:00";
        viewModel.IsAudioFile = false;
        viewModel.HasSelectedFile = false;
        viewModel.WaveformPeaks = [];

        viewModel.ClearMetadata();

        UpdatePlaybackTime(TimeSpan.Zero);
    }

    private void SaveStatusOk_Click(object? sender, RoutedEventArgs e)
    {
        SaveStatusOverlay.IsVisible = false;
    }
}
