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
and person ID take precedence over the lower-resolution R18.dev portrait when
WAPdB has a full performer profile. Low-resolution WAPdB mini-profile images
do not replace a larger R18.dev portrait.

### JavDatabase

[JavDatabase](https://www.javdatabase.com/) is an optional movie metadata
fallback. It is queried only when R18.dev has no result for the catalogue ID.
Matches require an exact normalized DVD ID.

Fallback metadata can include:

- English titles, release dates, and runtimes
- Studios, genres, directors, and performers
- Full covers and gallery backdrops
- JavDatabase external IDs

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
- R18.dev requests are serialized, cached for 30 minutes, and delayed by one
  second by default. Increase the configurable delay if the service still
  responds with HTTP 429.
- JavDatabase fallback metadata and its request delay can be configured
  independently.
- WAPdB performer enrichment can be enabled or disabled independently.
- Source base URLs are configurable to accommodate future endpoint changes.
- Movie titles can optionally be prefixed with their catalogue ID, for example
  `[SONE-444] Movie Title`.

## Development

```bash
dotnet build Jellyfin.Plugin.OppaiDex.sln
```

## Releases

Releases use semantic versioning (`MAJOR.MINOR.PATCH`). Before publishing,
keep `build.yaml` and `Directory.Build.props` on the same version:

```yaml
# build.yaml
version: "0.1.0"
```

```xml
<!-- Directory.Build.props -->
<Version>0.1.0</Version>
<AssemblyVersion>0.1.0.0</AssemblyVersion>
<FileVersion>0.1.0.0</FileVersion>
```

To publish a release:

1. Update `build.yaml` and `Directory.Build.props` to the intended version.
2. Update the changelog in `build.yaml`.
3. Publish the matching GitHub release draft, for example `v0.1.0`.
4. The publish workflow builds the plugin, attaches the ZIP and checksum
   files to the release, and updates `manifest.json` on the `gh-pages`
   branch.

The publish workflow validates the release tag before building. A tag such as
`v0.5.0` is rejected unless `build.yaml` contains `0.5.0` and
`Directory.Build.props` contains the matching project, assembly, and file
versions. A failed validation leaves the GitHub release without plugin
artifacts or a manifest update.

The first manifest run creates the `gh-pages` branch automatically. The
**Update Plugin Manifest** workflow can also be started manually to rebuild or
repair the manifest without publishing another release.

After the first release, enable GitHub Pages for the `gh-pages` branch. Add
the following repository URL in Jellyfin:

```text
https://nana-kaka.github.io/OppaiDex/manifest.json
```

A pushed tag alone does not publish the plugin. The corresponding GitHub
release must be published so that the release workflow receives an upload
URL.

Version increments follow the usual semantic-versioning rules:

- Patch releases such as `0.1.1` contain compatible fixes.
- Minor releases such as `0.2.0` add compatible features.
- Major releases such as `1.0.0` may contain breaking changes.

The release drafter chooses the next version from pull-request labels.
`breaking` increments the major version, `feature` or `enhancement` increments
the minor version, and fixes or maintenance increment the patch version.
Pull requests without a recognized label default to a patch release. Verify
the generated draft version before publishing it.

Pull requests from branches named `release-*` or `release/*` are automatically
labelled `skip-changelog` and excluded from the generated release notes. Apply
the same label manually to other pull requests that only update release
metadata. The `skip-changelog` label must exist in the GitHub repository.

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

The JavDatabase fallback strategy was informed by
[alxpnt2/PlexJav18.bundle](https://github.com/alxpnt2/PlexJav18.bundle).
OppaiDex uses an independent implementation based on the current JavDatabase
HTML structure and does not copy the Plex agent code.

JellyfinJAV did not work reliably with the Jellyfin version targeted by this
project. OppaiDex therefore implements the integration independently using the
current Jellyfin 10.11 provider interfaces, dependency injection model, and
plugin packaging requirements.
