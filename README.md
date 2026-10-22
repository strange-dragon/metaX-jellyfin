# MetaX for Jellyfin

MetaX is a Jellyfin `12.x` metadata and image plugin for the
[metaX backend](https://github.com/strange-dragon/metaX). It provides movie
search and detail metadata, actor metadata, primary/thumb/backdrop images, and
Person images from a MetaTube-compatible backend.

The plugin runs inside Jellyfin and talks to a separately deployed metaX
backend. Provider cookies remain in the plugin and are sent only to the
configured backend for the duration of a request.

```text
Jellyfin library
  └─ MetaX plugin
       └─ metaX backend
            └─ MetaTube SDK + native providers
```

## Compatibility

| Component | Version |
| --- | --- |
| Jellyfin | `12.x` |
| Plugin | `0.1.0.0` |
| Plugin target ABI | `12.0.0.0` |
| Target framework | `net10.0` |

This plugin does not target Jellyfin `10.11`. The current build is smoke-tested
on Jellyfin `12.2`.

## Features

- Movie search, movie detail, and primary/thumb/backdrop image providers.
- Actor search/detail and Person images.
- Category-aware provider routing for Jellyfin libraries.
- Request-scoped provider cookies through the
  `X-MetaX-Provider-Cookies` header.
- Provider catalog and settings page loaded from the backend.
- Signed local image proxy for Jellyfin Person images, so actor image downloads
  can carry provider cookies without exposing them in URLs.
- Redirect hardening: credentials are removed when a redirect leaves the
  configured MetaX origin.
- Provider alias handling for `DMM → FANZA`, `MGStage → MGS`, and
  `ThePornDB → ThePornDBMovie` / `ThePornDBScene`.

## Requirements

- Jellyfin `12.x`.
- A reachable metaX backend exposing the MetaTube-compatible API and
  `/v1/metax/status`.
- HTTPS between Jellyfin and the backend whenever provider cookies are used.
- .NET SDK `10.0` only if you build the plugin from source.

A backend token is optional but strongly recommended. If the backend has a
token, configure the same token in the plugin.

## Installation

### Option 1: Build from source and install manually

The repository does not include a Jellyfin plugin repository manifest. Build
the plugin, then copy the generated files into Jellyfin's plugins directory.
Operators who publish their own plugin repository can distribute the generated
zip and manifest through it.

```bash
git clone https://github.com/strange-dragon/metaX-jellyfin.git
cd metaX-jellyfin

dotnet restore metaX-jellyfin.sln
dotnet test metaX-jellyfin.sln -c Debug
dotnet build Jellyfin.Plugin.MetaX/Jellyfin.Plugin.MetaX.csproj -c Release
```

The Release build produces:

```text
Jellyfin.Plugin.MetaX/bin/Jellyfin.MetaX@v0.1.0.0.zip
```

The archive contains `MetaX.dll` and `meta.json`. Stop Jellyfin, create a
versioned plugin directory, and copy both files into it. The plugin directory
name can vary, but a versioned name makes upgrades and rollbacks easier.

Common default locations:

| Installation | Plugin directory |
| --- | --- |
| Linux package | `/var/lib/jellyfin/plugins/MetaX_0.1.0.0/` |
| Windows | `%ProgramData%\Jellyfin\Server\plugins\MetaX_0.1.0.0\` |
| Docker | `/config/plugins/MetaX_0.1.0.0/` |

Example for a Linux package installation:

```bash
sudo mkdir -p /var/lib/jellyfin/plugins/MetaX_0.1.0.0
sudo unzip -o Jellyfin.Plugin.MetaX/bin/Jellyfin.MetaX@v0.1.0.0.zip \
  -d /var/lib/jellyfin/plugins/MetaX_0.1.0.0
sudo chown -R jellyfin:jellyfin /var/lib/jellyfin/plugins/MetaX_0.1.0.0
sudo systemctl restart jellyfin
```

If your Jellyfin service uses a different user or data directory, adjust the
paths and ownership accordingly. The plugin is loaded after Jellyfin restarts.

### Option 2: Dockerized Jellyfin

The plugin itself is not a separate service and does not have its own Docker
image. It runs inside the Jellyfin container. Mount or copy the plugin files
into Jellyfin's `/config/plugins` directory.

With a host directory mounted at `/config/plugins`:

```bash
mkdir -p ./jellyfin/config/plugins/MetaX_0.1.0.0

cp Jellyfin.Plugin.MetaX/bin/Release/net10.0/MetaX.dll \
  ./jellyfin/config/plugins/MetaX_0.1.0.0/
cp Jellyfin.Plugin.MetaX/meta.json \
  ./jellyfin/config/plugins/MetaX_0.1.0.0/

docker compose restart jellyfin
```

If Jellyfin is not managed by Compose, restart its container with
`docker restart <jellyfin-container>` instead.

If the backend runs in another container on the same Docker network, set the
plugin **Server** URL to the backend service name, for example:

```text
http://metax:18080
```

If the backend is on the host, use the host address and port instead. In both
cases, prefer an HTTPS reverse proxy for production. Backend deployment
instructions are in the
[metaX Docker documentation](https://github.com/strange-dragon/metaX/blob/main/docs/DOCKER.md).

## Configuration

Open **Dashboard → Plugins → MetaX** after Jellyfin has loaded the plugin.

### Server

- **Server**: full base URL of the metaX backend, such as
  `https://metax.example.com` or `http://127.0.0.1:18080`.
- **Token**: the backend's bearer token, or blank if the backend has no token.

Use the backend's `/v1/metax/status` endpoint to verify connectivity:

```bash
curl http://127.0.0.1:18080/v1/metax/status
```

### Provider cookies

Provider cookies are configured per provider. A configured value is sent as
that provider's `Cookie` header. Leave a provider blank to disable its cookie
entry.

The settings page loads the provider catalog from the backend. Provider names
are normalized through the following aliases:

- `DMM` → `FANZA`
- `MGStage` → `MGS`
- `ThePornDB` → `ThePornDBMovie` / `ThePornDBScene`

The backend receives the selected cookies in one request header:

```http
X-MetaX-Provider-Cookies: {"JavDB":"session=...; cf_clearance=..."}
```

The header is sent only to the configured MetaX origin. It is not sent to
arbitrary image hosts, and it is removed when a redirect leaves that origin.

### Library categories

Map each Jellyfin library to one of the metaX categories:

- `censored`
- `uncensored`
- `chinese`
- `amateur`
- `western`
- `hentai`

`fc2` is accepted as an alias for `uncensored`; `animation` is accepted as an
alias for `hentai`. Libraries without a mapping do not receive
category-scoped provider cookies.

For movie search, movie detail, and movie image requests, the plugin:

1. resolves the library category;
2. reads the category-to-provider mapping from `/v1/metax/status`;
3. sends only the cookies for providers in that category;
4. passes the category to the backend;
5. applies the API token in the normal `Authorization` header.

### Enable the metadata provider

In each Jellyfin library:

1. enable **MetaX** under metadata downloaders;
2. enable the desired MetaX image fetchers;
3. save the library settings;
4. run **Scan library** or **Refresh metadata**.

## Security

- Provider cookies and backend tokens are credentials. Do not put them in URLs,
  screenshots, issue reports, or repository files.
- The plugin stores provider cookies in Jellyfin's plugin XML configuration in
  plaintext. Protect the Jellyfin configuration directory with filesystem
  permissions.
- Use HTTPS between Jellyfin and the backend. A bearer token alone does not
  encrypt traffic.
- Provider cookie headers are limited to 16 KiB. CR/LF characters are rejected.
- Cross-origin redirects strip both provider cookies and the bearer token.
- The signed Person-image proxy is anonymous because Jellyfin's image downloader
  has no user context; the signature prevents it from becoming an open proxy.

## Troubleshooting

### The provider catalog does not load

Check that Jellyfin can reach the backend:

```bash
curl -i http://backend-host:18080/v1/providers
curl -i http://backend-host:18080/v1/metax/status
```

Then verify the **Server** URL and token in the plugin settings. Existing
provider names remain editable if the catalog cannot be fetched.

### Requests return 401

The backend has a token configured, but the plugin's **Token** is blank or
different. Set the same value in both places.

### Metadata works but images do not

- Confirm the backend image routes are reachable from Jellyfin.
- If Person images fail, verify that Jellyfin's local HTTP API is reachable from
  the Jellyfin process.
- Check the Jellyfin log for `MetaX` image-proxy errors.
- Do not configure external image hosts as the backend URL.

### Provider cookies are not applied

- Confirm the provider is present in the backend route for the library category.
- Confirm the library is mapped to the correct category.
- Check the backend startup log for unavailable providers or route gaps.
- A provider may still reject the request because of region, login, or anti-bot
  controls; see the backend provider documentation.

## Build and test

```bash
dotnet restore metaX-jellyfin.sln
dotnet test metaX-jellyfin.sln -c Debug
dotnet format metaX-jellyfin.sln --verify-no-changes --no-restore
dotnet build Jellyfin.Plugin.MetaX/Jellyfin.Plugin.MetaX.csproj -c Release
```

The test suite uses mocked HTTP responses and does not require a live metaX
server. It covers category filtering, provider aliases, cookie-header limits,
configuration normalization, image requests, signed image-proxy tokens, and
redirect credential scoping.

## Attribution and license

This project is MIT-licensed. It is derived from the MIT-licensed
[metatube-community/jellyfin-plugin-metatube](https://github.com/metatube-community/jellyfin-plugin-metatube)
plugin. See [LICENSE](LICENSE) and [NOTICE.md](NOTICE.md) for attribution.

The metaX backend remains in the separate
[metaX repository](https://github.com/strange-dragon/metaX). Amane GPL-3.0
source and fixtures are not copied into this repository.
