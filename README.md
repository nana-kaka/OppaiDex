# OppaiDex

Jellyfin metadata provider for JAV movies using [R18.dev](https://r18.dev/).

## File names

The provider extracts catalogue IDs from movie file names. Examples:

- `MIDA-444.mp4`
- `mida444.mkv`
- `MIDA_444 - title.mp4`

Use a Jellyfin library with the content type **Movies** and enable the
`OppaiDex` metadata and image providers.

Imported metadata includes titles, release dates, runtimes, genres, studios,
cast and crew, person images, cover art, gallery backdrops, catalogue tags,
and R18.dev external IDs. Human translations are preferred; machine-translated
titles and categories fall back to their Japanese originals when available.

## Development

```bash
dotnet build Jellyfin.Plugin.OppaiDex.sln
```

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
