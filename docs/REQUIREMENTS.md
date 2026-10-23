# Jellyfin plugin requirements

Status: **implemented in plugin 0.2.0.0; Jellyfin 12 smoke and full
mock-backend library refresh verification passed; live metaX search and image
endpoint verification passed. A full library refresh against the live backend
still needs to be repeated in Jellyfin.**

Backend project: [strange-dragon/metaX](https://github.com/strange-dragon/metaX).
This repository is the plugin-side implementation project; it must not add a
parallel scraper runtime to the metaX backend.

This document records the plugin-side requirements. The backend contract is
implemented in metaX; plugin code lives in this repository and must not be
added to metaX as a parallel runtime or a custom plugin.

Pinned plugin reference for this record:
`metatube-community/jellyfin-plugin-metatube` at
`f7c1f336fc2bd3b35b82af7c6e69da09a196e3d2`. That revision only stores the
MetaTube server URL and token; it has no provider-cookie fields or
pass-through transport. The metaX fork intentionally removes the token
setting because the metaX backend is unauthenticated.

## 1. Goal

Let a Jellyfin administrator keep provider cookies in the plugin
configuration and have every metadata request use those cookies for that
request only.

The plugin is the **single persistent source of truth** for provider cookies.
The server must not save them, replay them at startup, or expose a second
configuration API.

## 2. Configuration model

The plugin configuration should add one dictionary:

```text
ProviderCookies: map<string, string>
```

- Key: provider name.
- Value: the HTTP `Cookie` header value for that provider.
- Example:

```json
{
  "JavDB": "session=...; cf_clearance=...",
  "Freejavbt": "login=..."
}
```

Provider names should come from the server's `GET /v1/providers` response.
The backend matches names case-insensitively and also accepts the documented
route aliases (`DMM` → `FANZA`, `MGStage` → `MGS`, `ThePornDB` →
`ThePornDBMovie`/`ThePornDBScene`).

Suggested UI behavior:

- Render one row per registered movie/actor provider that supports request
  cookies, with a masked input and a clear button.
- Do not provide one global cookie field; different providers need different
  cookies.
- Persist the dictionary in the existing plugin XML configuration. Do not
  write cookies to logs, Jellyfin metadata, or URLs.
- Treat an empty value as "no cookie". Omitting a provider is also valid.
- Warn when the serialized dictionary approaches the server's 16 KiB header
  limit.

No migration of existing users is required because the fields are additive.

### 2.1 Library category and cookie scope

The plugin must also maintain the Jellyfin-library-to-category mapping used for
scraping:

```text
LibraryCategories: map<Jellyfin library id, category>
```

The supported category values are `censored`, `uncensored`, `chinese`,
`amateur`, `western`, and `hentai`; `fc2` is accepted as an input alias for
`uncensored`. For each scrape, the plugin:

1. resolves the item's Jellyfin library to a category;
2. reads the category -> available-provider list from
   `GET /v1/metax/status` (`data.route_providers`);
3. builds `X-MetaX-Provider-Cookies` with only the configured cookies whose
   provider is in that list;
4. sends the category in the existing `category` query parameter.

The plugin must not send every configured cookie with every request. If the
manifest is unavailable, omit the cookie header rather than sending unrelated
provider cookies, and surface a configuration warning. The backend should also
defensively ignore cookie entries outside the resolved category for
category-routed movie requests.

The manifest is non-secret: provider names and category membership only. It
must never contain cookie values, tokens, proxies, or provider configuration.
Manifest names are canonical registered provider names; the backend accepts the
known route aliases (`DMM`, `MGStage`, `ThePornDB`) in the cookie header, so the
plugin should map a configured alias to the corresponding manifest name before
filtering.

## 3. Transport

Every plugin API request that can fetch provider metadata or images should
carry the dictionary for the resolved library category in one header:

```http
X-MetaX-Provider-Cookies: {"JavDB":"session=...; cf_clearance=..."}
```

Rules:

- The value is a JSON object with string values.
- Send the header only when at least one non-empty cookie exists.
- Send it on all relevant requests, not only detail requests:
  - movie search and detail;
  - actor search and detail;
  - reviews;
  - primary/thumb/backdrop images;
  - any future endpoint that may call a provider.
- Send no `Authorization` header. The plugin targets the unauthenticated metaX
  backend and no longer exposes a token setting.
- Do not put cookies in the query string, URL path, or image URL.
- Do not forward the header to arbitrary image URLs; it is only for requests
  to the MetaTube server.
- The plugin should JSON-escape values normally. The backend rejects CR/LF in
  cookie values and malformed or trailing JSON.
- The backend accepts at most 16 KiB for the raw header value.

Example request against metaX:

```http
GET /v1/movies/JavDB/ABC-123?lazy=false HTTP/1.1
X-MetaX-Provider-Cookies: {"JavDB":"session=abc; cf_clearance=def"}
```

## 4. Server behavior the plugin can rely on

- Cookies are applied only to the current request. Concurrent requests with
  different dictionaries cannot leak into each other.
- A matching provider is recreated for that request with the request cookie
  overlaid on its startup configuration.
- Unknown provider names are ignored for forward compatibility.
- A known provider that cannot accept request cookies makes the request fail
  with HTTP 400. The plugin should surface a configuration error rather than
  silently retrying without the header.
- If the header is absent, the server falls back to environment-variable
  cookies for backward compatibility.
- The server does not persist, cache, log, or return cookie values.

## 5. Security and operations

- Require HTTPS between Jellyfin and the MetaTube server; the cookie header is
  as sensitive as a session credential.
- Mask cookie fields in the plugin UI and never include them in exception
  messages.
- Do not send the header to third-party URLs.
- Disable automatic HTTP redirects and follow them manually with a bounded hop
  count. Re-check every redirect target against the configured metaX origin and
  remove `X-MetaX-Provider-Cookies` (and any authorization header) before any
  cross-origin request.
- Clearing a cookie in the plugin takes effect on the next request; there is no
  server-side state to invalidate.
- Server restart does not lose plugin-managed cookies because the plugin
  remains the source of truth.
- Environment cookies remain a fallback for deployments that cannot update the
  plugin yet.

## 6. Compatibility and rollout

- The change is additive: an older metaX server ignores the unknown header.
- An older plugin continues to work with environment cookies.
- A newer plugin with a newer server gets per-request cookies.
- The backend contract is covered by deterministic engine/HTTP tests in the
  [metaX repository](https://github.com/strange-dragon/metaX); see its
  `docs/EXECUTION_CHECKLIST.md` for CI evidence.
- This document does not claim the plugin change exists or that any blocked
  site works from a particular network.

## 7. Plugin implementation checklist

- [x] Add `ProviderCookies` and `LibraryCategories` to plugin configuration and
      settings UI.
- [x] Load provider names from `/v1/providers` and render per-provider fields.
- [x] Load the non-secret category -> provider manifest and filter cookies by
      the current library category before serializing.
- [x] Serialize the non-empty category-scoped dictionary as JSON.
- [x] Add the header to movie/actor search, detail, and image requests.
- [x] Enforce the 16 KiB limit client-side with a clear error.
- [x] Mask values in logs, exceptions, and diagnostics.
- [x] Add integration tests with a mock MetaTube server that asserts the
      header on search, detail, actor, and image requests.
- [x] Add a concurrency test proving two plugin instances/requests do not share
      cookie state.
- [x] Update the plugin README and implementation notes.
- [x] Verify plugin loading, plugin catalog, settings page, provider catalog,
      and remote movie search on Jellyfin `12.2`.
- [x] Verify a full library metadata/image refresh against a mock metaX
      endpoint on Jellyfin `12.2`, including category-scoped cookie headers.
- [x] Proxy Jellyfin Person images through a signed local endpoint so actor
      image downloads carry provider cookies.
- [x] Disable automatic redirects and strip provider cookies (and any
      authorization header) when a redirect leaves the configured metaX origin.
- [x] Remove the token setting from configuration, API client, settings page,
      tests, and documentation.
- [x] Reduce the settings page to server, library categories, and provider
      cookies.
- [x] Filter catalog-number searches to exact matches and deduplicate rows
      within each provider while keeping provider alternatives.
- [x] Add a series metadata provider and Series image support for Jellyfin
      `Shows` libraries.
- [x] Proxy browser-visible search images through the same-origin plugin image
      endpoint.
- [ ] Repeat the full refresh against a live metaX server.

## 8. Implementation notes

- Plugin identity: `Jellyfin.Plugin.MetaX`, assembly/provider name `MetaX`,
  plugin id `5597841f-fe55-4d59-991e-c3bdbf7d8d1d`.
- Configuration stores provider cookies and library categories as entry lists
  (`ProviderCookieEntry` / `LibraryCategoryEntry`) because Jellyfin's XML
  configuration path cannot reliably round-trip `Dictionary<string, string>`.
  The runtime converts them to case-insensitive dictionaries.
- `MetaXApiClient` builds the cookie header per request. `BaseProvider`
  extracts the `category` query parameter from movie image URLs so image
  downloads use the same scope.
- `SafeRedirectHandler` wraps the plugin HTTP handler with
  `AllowAutoRedirect = false`, follows redirects itself, caps them at five
  hops, and removes provider cookies (and any authorization header) on any hop
  outside the configured metaX origin.
- `MetaXController` exposes the non-secret provider catalog to the settings
  page at `Plugins/MetaX/Providers`; the controller requires an elevated
  Jellyfin user.
- Unit and mocked-HTTP tests are in `Jellyfin.Plugin.MetaX.Tests`. A Jellyfin
  `12.2` smoke test validated plugin loading, the plugin catalog, the settings
  page, the provider-catalog endpoint, remote movie search, and a full library
  metadata/image refresh against a local mock metaX endpoint. A live metaX
  server is still required for final end-to-end verification.
- Jellyfin's Person image downloader fetches `PersonInfo.ImageUrl` directly
  instead of calling the plugin's `GetImageResponse`. `MovieProvider` therefore
  points actor images at `/Plugins/MetaX/Images?token=...`; the token is
  protected with ASP.NET Core Data Protection, the endpoint is limited to the
  configured metaX origin, and the proxy uses the normal cookie-building path.
  The endpoint is anonymous because Jellyfin's internal downloader has no user
  context. The live Jellyfin `12.2` check confirmed that the resulting
  `Gfriends` actor-image request carried `X-MetaX-Provider-Cookies`.
- `SearchResultHelper` normalizes catalog numbers, keeps only exact matches
  when a catalog-like query is used, and collapses duplicate rows only within
  the same provider. Exact matches from JavDB, JavBus, FANZA, and other
  providers remain selectable in Jellyfin.
- `SeriesProvider` exposes the same MetaX movie record as a Jellyfin `Series`
  item so a `Shows` library can use MetaX. `MovieImageProvider` supports both
  `Movie` and `Series`.
- `ImageProxyService.CreateDisplayUrl` builds search-thumbnail proxy URLs from
  the active Jellyfin request origin; `CreateUrl` keeps the loopback URL used by
  server-side Person-image downloads.
- The plugin targets `net10.0` and Jellyfin `12.x`. Jellyfin `10.11` uses
  `net9.0`; supporting both in one package would require a separate build and
  is not part of this change.
- The release archive includes `meta.json` with `targetAbi` `12.0.0.0` and the
  plugin GUID, so it is installable through the Jellyfin plugin installer.
- Each `v*` release publishes `manifest.json` alongside the plugin zip. The
  stable repository URL is
  `https://github.com/strange-dragon/metaX-jellyfin/releases/latest/download/manifest.json`.
