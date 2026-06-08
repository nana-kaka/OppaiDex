using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.OppaiDex.Metadata;

public interface IMovieMetadataSource
{
    string Key { get; }

    string DisplayName { get; }

    int Order { get; }

    bool IsEnabled { get; }

    string? GetLookupId(
        IReadOnlyDictionary<string, string> providerIds,
        string? path,
        string? name);

    Task<MovieMetadata?> GetMetadataAsync(
        string id,
        CancellationToken cancellationToken);
}
