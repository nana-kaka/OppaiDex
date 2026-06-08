using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.OppaiDex.Metadata;

public interface IPersonMetadataEnricher
{
    int Order { get; }

    Task<MoviePersonMetadata> EnrichAsync(
        MoviePersonMetadata person,
        CancellationToken cancellationToken);
}
