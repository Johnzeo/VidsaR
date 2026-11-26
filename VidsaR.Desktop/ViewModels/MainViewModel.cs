
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using VidsaR.Core.Models;
using VidsaR.Infrastructure.Metadata;

namespace VidsaR.Desktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly MetadataReader _metadataReader = new();

    public ObservableCollection<AudioClip> AudioClips { get; } = [];

    public double TimelineDurationSeconds =>
        AudioClips.Sum(clip => clip.Duration.TotalSeconds);

    public MainViewModel()
    {
        AudioClips.CollectionChanged += AudioClips_CollectionChanged;
    }

    private void AudioClips_CollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(TimelineDurationSeconds));
    }

    private bool _isInitializing = true;
    public bool IsInitializing
    {
        get => _isInitializing;
        set => SetProperty(ref _isInitializing, value);
    }

    private string _ffmpegStatus = "Подготовка FFmpeg...";
    public string FFmpegStatus
    {
        get => _ffmpegStatus;
        set => SetProperty(ref _ffmpegStatus, value);
    }

    private string _selectedFileName = "Файл не выбран";
    public string SelectedFileName
    {
        get => _selectedFileName;
        set => SetProperty(ref _selectedFileName, value);
    }

    private string _selectedFilePath = string.Empty;
    public string SelectedFilePath
    {
        get => _selectedFilePath;
        set => SetProperty(ref _selectedFilePath, value);
    }

    private string _fileType = string.Empty;
    public string FileType
    {
        get => _fileType;
        set => SetProperty(ref _fileType, value);
    }

    private bool _hasSelectedFile;
    public bool HasSelectedFile
    {
        get => _hasSelectedFile;
        set => SetProperty(ref _hasSelectedFile, value);
    }

    private bool _isAudioFile;
    public bool IsAudioFile
    {
        get => _isAudioFile;
        set => SetProperty(ref _isAudioFile, value);
    }

    private string _fileDuration = "00:00:00";
    public string FileDuration
    {
        get => _fileDuration;
        set => SetProperty(ref _fileDuration, value);
    }

    private double _fileDurationSeconds;
    public double FileDurationSeconds
    {
        get => _fileDurationSeconds;
        set => SetProperty(ref _fileDurationSeconds, value);
    }

    private float[] _waveformPeaks = [];
    public float[] WaveformPeaks
    {
        get => _waveformPeaks;
        set => SetProperty(ref _waveformPeaks, value);
    }

    private string _artist = string.Empty;
    public string Artist
    {
        get => _artist;
        set => SetProperty(ref _artist, value);
    }

    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    private string _album = string.Empty;
    public string Album
    {
        get => _album;
        set => SetProperty(ref _album, value);
    }

    private string _genre = string.Empty;
    public string Genre
    {
        get => _genre;
        set => SetProperty(ref _genre, value);
    }

    private string _year = string.Empty;
    public string Year
    {
        get => _year;
        set => SetProperty(ref _year, value);
    }

    private byte[]? _coverArt;
    public byte[]? CoverArt
    {
        get => _coverArt;
        set
        {
            if (SetProperty(ref _coverArt, value))
            {
                UpdateCoverImage(value);
            }
        }
    }

    private Bitmap? _coverImage;
    public Bitmap? CoverImage
    {
        get => _coverImage;
        private set => SetProperty(ref _coverImage, value);
    }

    private bool _hasCoverImage;
    public bool HasCoverImage
    {
        get => _hasCoverImage;
        private set => SetProperty(ref _hasCoverImage, value);
    }

    private void UpdateCoverImage(byte[]? imageData)
    {
        CoverImage = null;
        HasCoverImage = false;

        if (imageData == null || imageData.Length == 0)
            return;

        using var stream = new MemoryStream(imageData);

        CoverImage = new Bitmap(stream);
        HasCoverImage = true;
    }

    public void SetCoverImage(byte[] imageData)
    {
        CoverArt = imageData;
    }

    public void LoadMetadata(string filePath)
    {
        ClearMetadata();

        var metadata = _metadataReader.Read(filePath);

        Title = metadata.Title;
        Artist = metadata.Artist;
        Album = metadata.Album;
        Genre = metadata.Genre;
        Year = metadata.Year?.ToString() ?? string.Empty;
        CoverArt = metadata.CoverArt;
    }

    public void ClearMetadata()
    {
        Artist = string.Empty;
        Title = string.Empty;
        Album = string.Empty;
        Genre = string.Empty;
        Year = string.Empty;
        CoverArt = null;
    }
}
