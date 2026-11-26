
using System;
using System.IO;
using VidsaR.Core.Models;

namespace VidsaR.Infrastructure.Metadata;

public class MetadataWriter
{
    public void SaveCopy(
        string sourcePath,
        string destinationPath,
        MediaMetadata metadata)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException(
                "Исходный файл не найден.",
                sourcePath);
        }

        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException(
                "Не указан путь для сохранения.",
                nameof(destinationPath));
        }

        if (string.Equals(
            Path.GetFullPath(sourcePath),
            Path.GetFullPath(destinationPath),
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Нельзя сохранять изменения поверх исходного файла.");
        }

        if (!string.Equals(
            Path.GetExtension(sourcePath),
            ".mp3",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                "Поддерживается только MP3.");
        }

        var directory = Path.GetDirectoryName(destinationPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Copy(sourcePath, destinationPath, overwrite: false);

        try
        {
            // Используем ID3v2.3.0 для совместимости с Windows.
            TagLib.Id3v2.Tag.DefaultVersion = 3;
            TagLib.Id3v2.Tag.ForceDefaultVersion = true;

            using var file = TagLib.File.Create(destinationPath);

            var tag = file.GetTag(TagLib.TagTypes.Id3v2, true);

            // Название.
            tag.Title = metadata.Title;

            // Исполнитель.
            tag.Performers = string.IsNullOrWhiteSpace(metadata.Artist)
                ? Array.Empty<string>()
                : new[] { metadata.Artist };

            // Альбом.
            tag.Album = metadata.Album;

            // Жанр.
            tag.Genres = string.IsNullOrWhiteSpace(metadata.Genre)
                ? Array.Empty<string>()
                : new[] { metadata.Genre };

            // Год выпуска.
            tag.Year = metadata.Year.HasValue && metadata.Year.Value > 0
                ? (uint)metadata.Year.Value
                : 0;

            // Удаляем старые обложки.
            tag.Pictures = Array.Empty<TagLib.IPicture>();

            // Добавляем новую обложку.
            if (metadata.CoverArt != null &&
                metadata.CoverArt.Length > 0)
            {
                var picture = new TagLib.Picture(
                    new TagLib.ByteVector(metadata.CoverArt));

                picture.Type = TagLib.PictureType.FrontCover;
                picture.MimeType = GetImageMimeType(metadata.CoverArt);
                picture.Description = "Front Cover";

                tag.Pictures = new TagLib.IPicture[]
                {
                    picture
                };
            }

            file.Save();
        }
        catch
        {
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            throw;
        }
    }

    private static string GetImageMimeType(byte[] imageData)
    {
        if (imageData.Length >= 3 &&
            imageData[0] == 0xFF &&
            imageData[1] == 0xD8 &&
            imageData[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (imageData.Length >= 8 &&
            imageData[0] == 0x89 &&
            imageData[1] == 0x50 &&
            imageData[2] == 0x4E &&
            imageData[3] == 0x47)
        {
            return "image/png";
        }

        if (imageData.Length >= 12 &&
            imageData[0] == 0x52 &&
            imageData[1] == 0x49 &&
            imageData[2] == 0x46 &&
            imageData[3] == 0x46 &&
            imageData[8] == 0x57 &&
            imageData[9] == 0x45 &&
            imageData[10] == 0x42 &&
            imageData[11] == 0x50)
        {
            return "image/webp";
        }

        if (imageData.Length >= 2 &&
            imageData[0] == 0x42 &&
            imageData[1] == 0x4D)
        {
            return "image/bmp";
        }

        return "application/octet-stream";
    }
}
