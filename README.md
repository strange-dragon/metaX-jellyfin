# metaX-jellyfin

Jellyfin metadata plugin for the [metaX](https://github.com/strange-dragon/metaX)
scraping backend. It keeps provider cookies in the plugin, scopes them to the
Jellyfin library being scraped, and sends them to the MetaX server only for the
duration of each request.

The plugin is derived from the MIT-licensed
[metatube-community/jellyfin-plugin-metatube](https://github.com/metatube-community/jellyfin-plugin-metatube)
plugin. See [NOTICE.md](NOTICE.md) for attribution. The metaX scraping backend
remains in the separate metaX repository; this repository does not contain a
second scraper runtime.

## Status

- Jellyfin `10.11.x` / `net9.0`.
- Builds and has unit/integration tests with a mocked HTTP server.
- Not yet verified against a live Jellyfin installation in this workspace.
- Provider live-site coverage is limited by the backend's own access results
  (for example, Prestige and Getchu are blocked from some networks). This
  plugin does not claim those sites work.

## Requirements

- .NET SDK 9.0 or newer 9.0 feature band.
- A reachable metaX server that exposes the MetaTube-compatible API and
  `/v1/metax/status`.
- HTTPS between Jellyfin and the metaX server. Provider cookies are session
  credentials.

## Build and test

```sh
dotnet restore metaX-jellyfin.sln
dotnet test metaX-jellyfin.sln -c Debug
dotnet build Jellyfin.Plugin.MetaX/Jellyfin.Plugin.MetaX.csproj -c Release
```

The Release build also writes `Jellyfin.MetaX@v<version>.zip` under the plugin
project's `bin` directory. Install it as a normal Jellyfin plugin, or copy the
built `MetaX.dll` into the Jellyfin plugin directory.

The local build used for development is .NET SDK `9.0.319`. The repository's
`global.json` accepts the latest .NET 9 feature band.

## Configuration

Open **Dashboard → Plugins → MetaX** in Jellyfin.

### Server

- **Server**: full URL of the metaX server, for example
  `https://metax.example.com`. HTTPS is strongly recommended.
- **Token**: the API token configured on the metaX server, or blank if the
  server has no token.

### Provider cookies

The plugin stores one cookie value per provider in its own XML configuration.
Entering a value is equivalent to the `Cookie` header that provider would
receive. Leave a provider blank to disable it.

The settings page loads provider names from the server's `GET /v1/providers`
and `GET /v1/metax/status` responses through the Jellyfin plugin API. The
catalog endpoint returns only provider names and category routing, not cookie
values. If the catalog cannot be loaded, existing configured names are still
shown and can be edited.

The plugin maps the backend's documented aliases before filtering:

- `DMM` → `FANZA`
- `MGStage` → `MGS`
- `ThePornDB` → `ThePornDBMovie` / `ThePornDBScene`

### Library categories

Map each Jellyfin library to one of the metaX categories:

`censored`, `uncensored`, `chinese`, `amateur`, `western`, `hentai`.

`fc2` is accepted as an input alias for `uncensored`, and `animation` is
normalized to `hentai`. An unmapped library does not receive category-scoped
provider cookies; the server's environment-cookie fallback can still apply.

## Request behavior

For movie search, movie detail, and movie image requests, the plugin:

1. resolves the item's Jellyfin library to a category;
2. reads the category → provider list from `GET /v1/metax/status`
   (`data.route_providers`), cached for five minutes;
3. serializes only the configured cookies whose canonical provider is in that
   category;
4. sends the category in the existing `category` query parameter;
5. sends the cookie dictionary in one request header:

```http
X-MetaX-Provider-Cookies: {"JavDB":"session=...; cf_clearance=..."}
```

Other rules:

- The header is only sent to the configured metaX server, never to arbitrary
  image URLs.
- The API token remains in the normal `Authorization` header.
- Cookie values never appear in URLs, Jellyfin metadata, logs, exception
  messages, or diagnostics. The plugin XML configuration does contain them in
  plaintext, so protect the Jellyfin configuration directory with appropriate
  filesystem permissions.
- CR/LF values and headers over 16 KiB are rejected with a message that names
  the provider but never includes the value.
- If the manifest is unavailable, category-scoped cookies are omitted and a
  warning is logged rather than sending unrelated provider cookies.
- Actor requests are not library-bound. Actor detail uses the explicit actor
  provider's cookie; unscoped actor search uses the configured cookie set so
  actor providers can participate. Image requests without a category also use
  the unscoped set.

The plugin is the sole persistent owner of provider cookies. The metaX server
does not save or replay them; it applies them only to the request that carries
the header.

## Tests

The xUnit project covers:

- category filtering, alias expansion, explicit-provider precedence, the
  16 KiB limit, CR/LF rejection, and secret-safe error messages;
- configuration normalization for provider cookies and library categories;
- mocked HTTP assertions for movie search, movie detail, actor search/detail,
  and image requests, including the category query and cookie header.

Tests use deterministic mocked responses and do not require a live metaX
server or a live Jellyfin instance.

## Attribution and license

This project is MIT-licensed. The original MetaTube plugin source is MIT
licensed; see [LICENSE](LICENSE) and [NOTICE.md](NOTICE.md). Do not copy Amane
GPL-3.0 source or fixtures into this repository.
