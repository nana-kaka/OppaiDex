using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.OppaiDex.Sources.Warashi;

public sealed class WarashiSearchResult
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? ImageUrl { get; init; }

    public IReadOnlyList<string> Aliases { get; init; } = [];
}

public sealed class WarashiPerson
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public DateTime? BirthDate { get; init; }

    public string? BirthPlace { get; init; }

    public IReadOnlyList<string> Aliases { get; init; } = [];

    public IReadOnlyList<string> ImageUrls { get; init; } = [];
}
