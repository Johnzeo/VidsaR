
namespace VidsaR.Core.Models;

public class MediaMetadata
{
    public string Title { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public string Album { get; set; } = string.Empty;

    public string Genre { get; set; } = string.Empty;

    public int? Year { get; set; }

    public byte[]? CoverArt { get; set; }
}
