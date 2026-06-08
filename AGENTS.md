# AGENTS.md

This file contains repository-specific guidance for AI coding agents.

## Project

OppaiDex is a Jellyfin 10.11 metadata plugin targeting .NET 9.

- Keep source code, comments, configuration text, and documentation in English.
- Preserve the plugin GUID in `Constants.cs`, `configPage.html`, and
  `build.yaml`. Changing it makes Jellyfin treat the plugin as a new plugin.
- Keep changes focused and avoid unrelated refactoring.

## Architecture

- `Providers/` contains the stable Jellyfin-facing providers.
- `Metadata/` contains provider-neutral models and extension interfaces.
- `Sources/` contains website-specific clients, parsers, and mappings.
- Add movie sources by implementing `IMovieMetadataSource`; discovery is
  automatic.
- Add performer enrichment by implementing `IPersonMetadataEnricher`.
- Keep website-specific response models out of the Jellyfin-facing providers.
- Use structured parsers such as `System.Text.Json` and AngleSharp instead of
  parsing JSON or HTML with ad hoc string operations.

## Metadata Rules

- Prefer an uncensored English movie title, then English, then Japanese.
- Preserve the Japanese movie title as `OriginalTitle`.
- WAPdB portraits and person IDs take precedence over R18.dev performer
  portraits after an exact normalized name or alias match.
- Keep R18.dev portraits as fallback data.
- Avoid fuzzy performer matching that could associate metadata with the wrong
  person.

## Validation

Run these checks after code changes:

```bash
dotnet build Jellyfin.Plugin.OppaiDex.sln --no-restore --configuration Release
dotnet format Jellyfin.Plugin.OppaiDex.sln --no-restore --verify-no-changes
```

For workflow or release metadata changes, also validate YAML and run:

```bash
git diff --check
```

The Docker development setup expects a Debug build:

```bash
dotnet build Jellyfin.Plugin.OppaiDex.sln
docker compose up
```

## Releases

- Use semantic versions and matching tags, for example `0.1.0` and `v0.1.0`.
- Keep `build.yaml` and `Directory.Build.props` on the same release version.
- Keep `targetAbi`, `framework`, and packaged artifacts in `build.yaml`
  synchronized with the project.
- Repository workflows target the `main` branch.
- References such as `jellyfin-meta-plugins/...@master` point to an external
  repository and should not be changed merely because this repository uses
  `main`.
- Publishing a GitHub release triggers packaging and regenerates
  `manifest.json` on `gh-pages`.

## External Services

- Treat R18.dev and WAPdB responses as untrusted and potentially incomplete.
- Preserve cancellation tokens and handle HTTP or parsing failures without
  crashing Jellyfin metadata scans.
- Do not add credentials, cookies, downloaded pages, or user library data to
  the repository.
- Keep integrations independent; do not copy implementation code from
  reference plugins.
