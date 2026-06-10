using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.OppaiDex.Metadata;

public sealed class MovieMetadata
{
    public required string Name { get; init; }

    public string? CatalogueId { get; init; }

    public string? OriginalTitle { get; init; }

    public string? Overview { get; init; }

    public DateTime? ReleaseDate { get; init; }

    public int? RuntimeMinutes { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = [];

    public IReadOnlyList<string> Studios { get; init; } = [];

    public IReadOnlyList<string> Tags { get; init; } = [];

    public IReadOnlyDictionary<string, string> ProviderIds { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<MoviePersonMetadata> People { get; init; } = [];

    public RemoteImageMetadata? PrimaryImage { get; init; }

    public IReadOnlyList<RemoteImageMetadata> Backdrops { get; init; } = [];
}

public sealed class MoviePersonMetadata
{
    public required string Name { get; init; }

    public required PersonKind Kind { get; init; }

    public string? ImageUrl { get; init; }

    public IReadOnlyDictionary<string, string> ProviderIds { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class RemoteImageMetadata
{
    public required string Url { get; init; }

    public string? ThumbnailUrl { get; init; }
}
