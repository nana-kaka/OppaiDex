using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.OppaiDex.Models;

public sealed class R18Movie
{
    [JsonPropertyName("actors")]
    public IReadOnlyList<R18Person> Actors { get; init; } = [];

    [JsonPropertyName("actresses")]
    public IReadOnlyList<R18Person> Actresses { get; init; } = [];

    [JsonPropertyName("authors")]
    public IReadOnlyList<R18Person> Authors { get; init; } = [];

    [JsonPropertyName("categories")]
    public IReadOnlyList<R18Category> Categories { get; init; } = [];

    [JsonPropertyName("comment_en")]
    public string? CommentEnglish { get; init; }

    [JsonPropertyName("content_id")]
    public string? ContentId { get; init; }

    [JsonPropertyName("directors")]
    public IReadOnlyList<R18Person> Directors { get; init; } = [];

    [JsonPropertyName("dvd_id")]
    public string? DvdId { get; init; }

    [JsonPropertyName("histrions")]
    public IReadOnlyList<R18Person> Histrions { get; init; } = [];

    [JsonPropertyName("jacket_full_url")]
    public string? JacketFullUrl { get; init; }

    [JsonPropertyName("jacket_thumb_url")]
    public string? JacketThumbUrl { get; init; }

    [JsonPropertyName("label_name_en")]
    public string? LabelNameEnglish { get; init; }

    [JsonPropertyName("label_name_ja")]
    public string? LabelNameJapanese { get; init; }

    [JsonPropertyName("maker_name_en")]
    public string? MakerNameEnglish { get; init; }

    [JsonPropertyName("maker_name_ja")]
    public string? MakerNameJapanese { get; init; }

    [JsonPropertyName("release_date")]
    public DateTime? ReleaseDate { get; init; }

    [JsonPropertyName("runtime_mins")]
    public int? RuntimeMinutes { get; init; }

    [JsonPropertyName("title_en")]
    public string? TitleEnglish { get; init; }

    [JsonPropertyName("title_en_uncensored")]
    public string? TitleEnglishUncensored { get; init; }
}

public sealed class R18Person
{
    [JsonPropertyName("name_kanji")]
    public string? NameKanji { get; init; }

    [JsonPropertyName("name_romaji")]
    public string? NameRomaji { get; init; }

    [JsonIgnore]
    public string? DisplayName => NameRomaji ?? NameKanji;
}

public sealed class R18Category
{
    [JsonPropertyName("name_en")]
    public string? NameEnglish { get; init; }

    [JsonPropertyName("name_ja")]
    public string? NameJapanese { get; init; }

    [JsonIgnore]
    public string? DisplayName => NameEnglish ?? NameJapanese;
}
