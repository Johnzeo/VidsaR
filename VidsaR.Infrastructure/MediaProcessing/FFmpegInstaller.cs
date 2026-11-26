
using System;
using System.IO;
using System.Threading.Tasks;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace VidsaR.Infrastructure.MediaProcessing;

public class FFmpegInstaller
{
    public async Task EnsureInstalledAsync()
    {
        // Папка для хранения FFmpeg в профиле пользователя.
        string ffmpegPath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "VidsaR",
            "FFmpeg");

        // Создаём папку, если её ещё нет.
        Directory.CreateDirectory(ffmpegPath);

        // Указываем библиотеке, где искать FFmpeg.
        FFmpeg.SetExecutablesPath(ffmpegPath);

        string ffmpegFile = Path.Combine(ffmpegPath, "ffmpeg.exe");
        string ffprobeFile = Path.Combine(ffmpegPath, "ffprobe.exe");

        // Если исполняемых файлов нет, загружаем их.
        if (!File.Exists(ffmpegFile) || !File.Exists(ffprobeFile))
        {
            await FFmpegDownloader.GetLatestVersion(
                FFmpegVersion.Official,
                ffmpegPath);
        }

        // Повторно устанавливаем путь после загрузки.
        FFmpeg.SetExecutablesPath(ffmpegPath);
    }
}
