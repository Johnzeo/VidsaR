
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace VidsaR.Infrastructure.MediaProcessing;

public class WaveformService
{
    public async Task<float[]> GetWaveformAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Путь к файлу не указан.");

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Медиафайл не найден.", filePath);

        string ffmpegPath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "VidsaR",
            "FFmpeg",
            "ffmpeg.exe");

        if (!File.Exists(ffmpegPath))
            throw new FileNotFoundException("FFmpeg не найден.", ffmpegPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(filePath);
        startInfo.ArgumentList.Add("-vn");
        startInfo.ArgumentList.Add("-ac");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("-ar");
        startInfo.ArgumentList.Add("8000");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("s16le");
        startInfo.ArgumentList.Add("pipe:1");

        using var process = new Process
        {
            StartInfo = startInfo
        };

        var peaks = new List<float>();

        const int samplesPerPeak = 200;

        int currentSamples = 0;
        int currentPeak = 0;

        process.Start();

        Task<string> errorTask = process.StandardError.ReadToEndAsync();

        var buffer = new byte[8192];
        int bytesRead;

        byte? previousByte = null;

        while ((bytesRead = await process.StandardOutput.BaseStream
                   .ReadAsync(buffer.AsMemory())) > 0)
        {
            for (int i = 0; i < bytesRead; i++)
            {
                if (previousByte == null)
                {
                    previousByte = buffer[i];
                    continue;
                }

                short sample = BitConverter.ToInt16(
                    new byte[] { previousByte.Value, buffer[i] },
                    0);

                previousByte = null;

                int amplitude = Math.Abs((int)sample);

                if (amplitude > currentPeak)
                    currentPeak = amplitude;

                currentSamples++;

                if (currentSamples >= samplesPerPeak)
                {
                    peaks.Add(currentPeak / 32768f);

                    currentSamples = 0;
                    currentPeak = 0;
                }
            }
        }

        if (currentSamples > 0)
        {
            peaks.Add(currentPeak / 32768f);
        }

        await process.WaitForExitAsync();

        string error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Не удалось построить звуковую волну: {error}");
        }

        return peaks.ToArray();
    }
}
