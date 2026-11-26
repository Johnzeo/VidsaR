
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xabe.FFmpeg;
using VidsaR.Core.Models;

namespace VidsaR.Infrastructure.MediaProcessing;

public class FFmpegService
{
    private readonly FFmpegInstaller _installer = new();

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            await _installer.EnsureInstalledAsync();

            string ffmpegPath = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "VidsaR",
                "FFmpeg");

            string ffmpegFile = Path.Combine(ffmpegPath, "ffmpeg.exe");
            string ffprobeFile = Path.Combine(ffmpegPath, "ffprobe.exe");

            return File.Exists(ffmpegFile) &&
                   File.Exists(ffprobeFile);
        }
        catch
        {
            return false;
        }
    }

    public async Task<TimeSpan> GetDurationAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException(
                "Путь к файлу не указан.",
                nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException(
                "Медиафайл не найден.",
                filePath);

        await _installer.EnsureInstalledAsync();

        var mediaInfo = await FFmpeg.GetMediaInfo(filePath);

        return mediaInfo.Duration;
    }

    public async Task ExportAudioSegmentAsync(
        string inputPath,
        string outputPath,
        TimeSpan start,
        TimeSpan end,
        IProgress<double>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            throw new ArgumentException(
                "Путь к исходному файлу не указан.",
                nameof(inputPath));

        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException(
                "Путь для сохранения не указан.",
                nameof(outputPath));

        if (!File.Exists(inputPath))
            throw new FileNotFoundException(
                "Исходный аудиофайл не найден.",
                inputPath);

        if (start < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(start));

        if (end <= start)
            throw new ArgumentException(
                "Конец фрагмента должен быть позже его начала.");

        if (string.Equals(
            Path.GetFullPath(inputPath),
            Path.GetFullPath(outputPath),
            StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Нельзя перезаписывать исходный файл.");
        }

        await _installer.EnsureInstalledAsync();

        string ffmpegPath = GetFFmpegPath();

        if (!File.Exists(ffmpegPath))
            throw new FileNotFoundException(
                "FFmpeg не найден.",
                ffmpegPath);

        TimeSpan duration = end - start;

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        startInfo.ArgumentList.Add("-y");

        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(inputPath);

        startInfo.ArgumentList.Add("-ss");
        startInfo.ArgumentList.Add(
            start.TotalSeconds.ToString(
                CultureInfo.InvariantCulture));

        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add(
            duration.TotalSeconds.ToString(
                CultureInfo.InvariantCulture));

        startInfo.ArgumentList.Add("-vn");
        startInfo.ArgumentList.Add("-codec:a");
        startInfo.ArgumentList.Add("libmp3lame");
        startInfo.ArgumentList.Add("-q:a");
        startInfo.ArgumentList.Add("2");

        startInfo.ArgumentList.Add("-progress");
        startInfo.ArgumentList.Add("pipe:1");
        startInfo.ArgumentList.Add("-nostats");

        startInfo.ArgumentList.Add(outputPath);

        await RunFFmpegAsync(
            startInfo,
            duration,
            progress);
    }

    // Экспорт всей Timeline в один MP3.
    public async Task ExportTimelineAsync(
        IEnumerable<AudioClip> clips,
        string outputPath,
        IProgress<double>? progress = null)
    {
        if (clips == null)
            throw new ArgumentNullException(nameof(clips));

        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException(
                "Путь для сохранения не указан.",
                nameof(outputPath));

        var clipList = clips
            .OrderBy(clip => clip.TimelineStart)
            .ToList();

        if (clipList.Count == 0)
            throw new InvalidOperationException(
                "На Timeline нет клипов для экспорта.");

        foreach (var clip in clipList)
        {
            if (string.IsNullOrWhiteSpace(clip.SourceFilePath))
                throw new InvalidOperationException(
                    "У одного из клипов не указан исходный файл.");

            if (!File.Exists(clip.SourceFilePath))
                throw new FileNotFoundException(
                    "Исходный файл одного из клипов не найден.",
                    clip.SourceFilePath);

            if (clip.SourceStart < TimeSpan.Zero ||
                clip.SourceEnd <= clip.SourceStart)
            {
                throw new InvalidOperationException(
                    "У одного из клипов некорректные границы.");
            }

            if (clip.TimelineStart < TimeSpan.Zero)
                throw new InvalidOperationException(
                    "Клип не может находиться до начала Timeline.");
        }

        string fullOutputPath = Path.GetFullPath(outputPath);

        foreach (var clip in clipList)
        {
            if (string.Equals(
                Path.GetFullPath(clip.SourceFilePath),
                fullOutputPath,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Нельзя сохранять результат поверх исходного файла.");
            }
        }

        await _installer.EnsureInstalledAsync();

        string ffmpegPath = GetFFmpegPath();

        if (!File.Exists(ffmpegPath))
            throw new FileNotFoundException(
                "FFmpeg не найден.",
                ffmpegPath);

        TimeSpan timelineDuration = clipList.Max(
            clip => clip.TimelineEnd);

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        startInfo.ArgumentList.Add("-y");

        // Добавляем все исходные файлы.
        foreach (var clip in clipList)
        {
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(clip.SourceFilePath);
        }

        // Создаём фильтры для каждого клипа.
        var filters = new List<string>();

        for (int i = 0; i < clipList.Count; i++)
        {
            var clip = clipList[i];

            string sourceStart = clip.SourceStart.TotalSeconds
                .ToString("0.######", CultureInfo.InvariantCulture);

            string sourceEnd = clip.SourceEnd.TotalSeconds
                .ToString("0.######", CultureInfo.InvariantCulture);

            long delayMilliseconds = (long)Math.Round(
                clip.TimelineStart.TotalMilliseconds);

            filters.Add(
                $"[{i}:a]" +
                $"atrim=start={sourceStart}:end={sourceEnd}," +
                "asetpts=PTS-STARTPTS," +
                $"adelay=delays={delayMilliseconds}:all=1" +
                $"[a{i}]");
        }

        // Смешиваем клипы с учётом их положения на Timeline.
        string inputs = string.Concat(
            Enumerable.Range(0, clipList.Count)
                .Select(i => $"[a{i}]"));

        filters.Add(
            $"{inputs}amix=inputs={clipList.Count}:" +
            "duration=longest:" +
            "dropout_transition=0:" +
            "normalize=0[out]");

        string filterComplex = string.Join(";", filters);

        startInfo.ArgumentList.Add("-filter_complex");
        startInfo.ArgumentList.Add(filterComplex);

        startInfo.ArgumentList.Add("-map");
        startInfo.ArgumentList.Add("[out]");

        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add(
            timelineDuration.TotalSeconds.ToString(
                "0.######",
                CultureInfo.InvariantCulture));

        startInfo.ArgumentList.Add("-codec:a");
        startInfo.ArgumentList.Add("libmp3lame");

        startInfo.ArgumentList.Add("-q:a");
        startInfo.ArgumentList.Add("2");

        startInfo.ArgumentList.Add("-progress");
        startInfo.ArgumentList.Add("pipe:1");

        startInfo.ArgumentList.Add("-nostats");

        startInfo.ArgumentList.Add(outputPath);

        await RunFFmpegAsync(
            startInfo,
            timelineDuration,
            progress);
    }

    private static string GetFFmpegPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "VidsaR",
            "FFmpeg",
            "ffmpeg.exe");
    }

    // Общий запуск FFmpeg с отслеживанием прогресса.
    private static async Task RunFFmpegAsync(
        ProcessStartInfo startInfo,
        TimeSpan duration,
        IProgress<double>? progress)
    {
        using var process = new Process
        {
            StartInfo = startInfo
        };

        process.Start();

        progress?.Report(0);

        Task<string> errorTask =
            process.StandardError.ReadToEndAsync();

        while (true)
        {
            string? line =
                await process.StandardOutput.ReadLineAsync();

            if (line == null)
                break;

            if (line.StartsWith(
                "out_time_us=",
                StringComparison.Ordinal))
            {
                string value = line["out_time_us=".Length..];

                if (long.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long microseconds))
                {
                    double percentage =
                        microseconds / (duration.TotalMilliseconds * 10);

                    percentage = Math.Clamp(percentage, 0, 100);

                    progress?.Report(percentage);
                }
            }
        }

        await process.WaitForExitAsync();

        string error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Ошибка экспорта аудио: {error}");
        }

        string outputPath = startInfo.ArgumentList[^1];

        if (!File.Exists(outputPath) ||
            new FileInfo(outputPath).Length == 0)
        {
            throw new InvalidOperationException(
                "FFmpeg не создал выходной аудиофайл.");
        }

        progress?.Report(100);
    }
}
