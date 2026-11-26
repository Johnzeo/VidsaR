
using System;

namespace VidsaR.Core.Models;

public class AudioClip
{
    /// <summary>
    /// Уникальный идентификатор фрагмента.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Путь к исходному аудиофайлу.
    /// </summary>
    public string SourceFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Начало используемого фрагмента в исходном файле.
    /// </summary>
    public TimeSpan SourceStart { get; set; }

    /// <summary>
    /// Конец используемого фрагмента в исходном файле.
    /// </summary>
    public TimeSpan SourceEnd { get; set; }

    /// <summary>
    /// Позиция фрагмента на общей временной шкале.
    /// </summary>
    public TimeSpan TimelineStart { get; set; }

    /// <summary>
    /// Продолжительность фрагмента.
    /// </summary>
    public TimeSpan Duration => SourceEnd - SourceStart;

    /// <summary>
    /// Конец фрагмента на временной шкале.
    /// </summary>
    public TimeSpan TimelineEnd => TimelineStart + Duration;
}
