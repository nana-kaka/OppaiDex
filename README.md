# OppaiDex

OppaiDex is a metadata plugin for Jellyfin 10.11 that retrieves JAV movie and
performer information from multiple sources.

## Metadata sources

### R18.dev

[R18.dev](https://r18.dev/) is the primary movie metadata source.

Imported data includes:

- English and Japanese titles
- Release dates and runtimes
- Genres, studios, labels, cast, and crew
- Cover art and gallery backdrops
- Catalogue tags and external IDs
- Performer names and fallback portraits

English titles are used as Jellyfin display titles, including machine
translations when no uncensored English title is available. Japanese titles
are retained as original titles. Machine-translated categories fall back to
their Japanese originals when available.

### WAPdB

[WAPdB](https://warashi-asian-pornstars.fr/) enriches performer entries with:

- Higher-resolution portrait images
- Multiple selectable profile images
- Birth dates and birthplaces
- Stable WAPdB person IDs

Performer matching is restricted to exact normalized names and aliases to
reduce false associations. During movie imports, a matching WAPdB portrait
and person ID take precedence over the lower-resolution R18.dev portrait.

## Usage

### File names

Create a Jellyfin library with the content type **Movies**, then enable the
`OppaiDex` metadata and image providers.

OppaiDex extracts catalogue IDs from movie file names. Examples:

- `MIDA-444.mp4`
- `mida444.mkv`
- `MIDA_444 - title.mp4`

## Configuration

- The R18.dev API URL template can be changed when necessary.
- WAPdB performer enrichment can be enabled or disabled independently.
- Source base URLs are configurable to accommodate future endpoint changes.

## Development

```bash
dotnet build Jellyfin.Plugin.OppaiDex.sln
```

## Releases

Releases use Jellyfin's integer plugin build versions. The version in
`build.yaml` must match the major component in `Directory.Build.props`:

```yaml
# build.yaml
version: 1
```

```xml
<!-- Directory.Build.props -->
<Version>1.0.0.0</Version>
```

To publish a release:

1. Merge the generated release preparation pull request, or update
   `build.yaml` and `Directory.Build.props` manually.
2. Create and publish a GitHub release using the matching tag, for example
   `v1`.
3. The publish workflow builds the plugin, attaches the ZIP and checksum
   files to the release, and updates `manifest.json` on the `gh-pages`
   branch.

After the first release, enable GitHub Pages for the `gh-pages` branch. Add
the following repository URL in Jellyfin:

```text
https://nana-kaka.github.io/OppaiDex/manifest.json
```

For automatically generated release preparation pull requests, enable
**Settings > Actions > General > Allow GitHub Actions to create and approve
pull requests** once for the repository.

A pushed tag alone does not publish the plugin. The corresponding GitHub
release must be published so that the release workflow receives an upload
URL.

## Adding a metadata source

OppaiDex exposes one stable Jellyfin metadata and image provider. Individual
websites are implemented as internal metadata sources and are discovered
automatically at startup.

To add another source:

1. Implement `IMovieMetadataSource`.
2. Parse the source response into the provider-neutral `MovieMetadata` model.
3. Give the source a unique `Key` and provider priority through `Order`.
4. Add source-specific configuration fields and UI controls when needed.
5. Optionally add `IExternalId` implementations for IDs that should be visible
   in Jellyfin.

No changes to `OppaiDexMovieProvider`, `OppaiDexImageProvider`, or
`PluginServiceRegistrator` are required for additional source classes.

## Credits and references

The WAPdB integration was informed by the open-source
[Kyuhaku/JellyfinJAV](https://github.com/Kyuhaku/JellyfinJAV) plugin and its
Warashi person-provider implementation.

JellyfinJAV did not work reliably with the Jellyfin version targeted by this
project. OppaiDex therefore implements the integration independently using the
current Jellyfin 10.11 provider interfaces, dependency injection model, and
plugin packaging requirements.
