
using TagLib;
using VidsaR.Core.Models;

namespace VidsaR.Infrastructure.Metadata;

public class MetadataReader
{
    public MediaMetadata Read(string filePath)
    {
        using var file = TagLib.File.Create(filePath);

        var tag = file.Tag;

        byte[]? coverArt = null;

        if (tag.Pictures.Length > 0)
        {
            coverArt = tag.Pictures[0].Data.Data;
        }

        return new MediaMetadata
        {
            Title = string.IsNullOrWhiteSpace(tag.Title)
                ? Path.GetFileNameWithoutExtension(filePath)
                : tag.Title,

            Artist = tag.Performers.Length > 0
                ? tag.Performers[0]
                : string.Empty,

            Album = tag.Album ?? string.Empty,
            Genre = tag.Genres.Length > 0
                ? tag.Genres[0]
                : string.Empty,

            Year = tag.Year > 0
                ? (int)tag.Year
                : null,

            CoverArt = coverArt
        };
    }
}
