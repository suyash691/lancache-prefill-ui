# lancache-prefill

Web UI for managing a [Lancache](https://lancache.net/) Steam game cache. Checks for game updates, downloads content through the cache, and runs on a schedule.

Built with [SteamKit2](https://github.com/SteamRE/SteamKit) and ASP.NET.

## Prerequisites

This tool warms an existing Lancache by requesting Steam depot chunks *through* it. For that to work end-to-end:

1. **A running Lancache that this host resolves to.** The host running this container must use your Lancache's DNS (lancache-dns) so that Steam CDN domains resolve to the cache's LAN IP. Because the container uses `network_mode: host`, it inherits the host's resolver — point the host's DNS at your Lancache. Verify:
   ```bash
   nslookup lancache.steamcontent.com   # must return your Lancache LAN IP, not a public IP
   ```
   If this returns a public IP, DNS isn't set up and prefill will fail with "No Lancache detected".
2. **`lancachenet/monolithic`** is the supported/tested Lancache image. Downloads work with any Lancache that proxies Steam over HTTP, but the cache **scan/verify** feature assumes the monolithic layout (`cacheidentifier=steam`, `slice 1m`, `levels=2:2`). On other images, prefill still works but scan may report games as "not cached".
3. **A Steam account that owns the games** you want to prefill (login supports Steam Guard / 2FA). You are only prompted once; the refresh token is stored (encrypted) under `CONFIG_DIR`.
4. **`LANCACHE_CACHE_DIR`** (optional) must point at the monolithic cache data dir (the `.../cache/cache` folder, mounted read-only) if you want the Scan and Cache Browser tabs to work. Prefill does not need it.

## Setup

```bash
cp .env.example .env
# edit .env if needed (defaults work out of the box)
docker compose up -d --build
```

Open `https://<ip>:28542`, log in with Steam, add games, done.

## Config

| Variable | Default | |
|----------|---------|---|
| `CONFIG_DIR` | `./config` | Auth tokens, app lists, download history |
| `PREFILL_SCHEDULE` | `0 4 * * *` | Prefill cron (daily 4am) |
| `SCAN_SCHEDULE` | `0 3 */3 * *` | Lancache scan cron (every 3 days 3am) |
| `PORT` | `28542` | HTTPS port |
| `TZ` | `Europe/London` | |

`LANCACHE_CACHE_DIR` (set in `.env`, mounted read-only) enables the Scan and Cache Browser tabs — point it at the monolithic cache data dir (`.../cache/cache`).

### Throughput settings (Settings tab)

| Setting | Default | |
|----------|---------|---|
| Prefill Concurrency | `6` | Parallel chunk downloads (1–30) |
| Prefill Bandwidth Limit | `0` (unlimited) | Global cap in Mbps across all prefill downloads |
| Scan Concurrency | `4` | Parallel app verifications during scan |

Defaults are deliberately conservative: the lancache is usually serving real clients at the same time. Lancache's nginx runs with `proxy_cache_lock` and `proxy_ignore_client_abort` enabled, so an overly aggressive prefill doesn't just compete for bandwidth — abandoned/timed-out requests keep downloading *inside nginx* while holding per-slice cache locks, which can stall LAN clients (even on cached content, via disk contention) both during and after a prefill run. The downloader therefore uses patient idle-based timeouts instead of aborting slow transfers, and skips chunks that are already on disk (when `LANCACHE_CACHE_DIR` is mounted). If clients still lag while prefill runs, set a bandwidth limit and/or lower the concurrency.

Note: the scheduled scan no longer chains an immediate prefill — evicted apps found by the scan are picked up by the next scheduled prefill instead.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| **LAN clients stall at 0 B/s on a previously-prefilled game (WAN idle too), works when bypassing the cache** | A poisoned cache entry: an upstream error page got cached with HTTP 200 under a chunk key during prefill. Steam clients read it from disk (no WAN traffic), fail SHA validation, and retry forever. | Run a **Force prefill** for the affected game — force mode re-pulls every chunk through the cache, validates its size against the manifest, and re-fetches mismatches with `?nocache=1`, overwriting the bad entry in place. To confirm poisoning first: `curl -s -A 'Valve/Steam HTTP Client 1.0' -H 'Host: lancache.steamcontent.com' http://<cache-ip>/depot/<id>/chunk/<sha> \| head -c 300` — HTML/text output means the entry is junk. |
| **Login page shows "No Lancache detected"** / prefill errors immediately | Host DNS isn't pointed at lancache-dns, so `lancache.steamcontent.com` resolves to a public IP | Point the host's resolver at your Lancache and confirm with `nslookup lancache.steamcontent.com` → cache LAN IP. Then restart the container. |
| **Browser TLS warning on `https://<ip>:28542`** | Self-signed cert generated on first run | Expected — accept the warning. The cert now includes the host's private LAN IPs in its SAN, so the hostname/IP mismatch is minimized; it's still self-signed. |
| **Prefill succeeds but Scan shows games as "not cached"** | Non-monolithic Lancache, or a monolithic config with a non-default `cacheidentifier`/slice size — the on-disk cache key differs from what the scan expects | Use `lancachenet/monolithic` with defaults. To confirm the key format, inspect a cache file: `grep -rl "KEY: steam/depot/" <cache>/cache \| head` — the scan expects keys of the form `steam/depot/<id>/chunk/<sha>bytes=0-1048575`. |
| **Scan/Cache Browser tabs are empty or error** | `LANCACHE_CACHE_DIR` unset or pointing at the wrong directory | Set it to the monolithic `.../cache/cache` folder (mounted read-only). Prefill does not require this. |
| **"credentials_required" after a container rebuild** | Stored Steam token couldn't be decrypted | The token key is derived per-install and persisted in `CONFIG_DIR/.machine-id`, so it survives `docker compose up --build`. If you wiped `CONFIG_DIR`, just log in again. |
| **`too_many_attempts` on login** | 5 failed logins within 5 minutes (brute-force guard) | Wait 5 minutes and retry. |
| **A game shows "no depots" / manifest errors** | The account doesn't own the app, or it's Linux/tool/DLC-only | Only Windows depots for owned games are prefilled; unowned/non-game depots are skipped by design. |

**Quick end-to-end sanity check** (run against your Lancache, no app needed):
```bash
# 1. DNS poisoning works?
nslookup lancache.steamcontent.com                 # → cache LAN IP
# 2. warm one chunk the way the app does (run twice → expect 200,200)
curl -s -o /dev/null -w "%{http_code}\n" \
  -H "Host: lancache.steamcontent.com" \
  "http://<cache-ip>/depot/<depotid>/chunk/<sha>"
# 3. confirm it landed on disk with the KEY line the scan expects
grep -rl "KEY: steam/depot/<depotid>/chunk/" <cache>/cache | head
```
If all three pass, the app's real paths will work on your setup.

## Tests

```bash
docker build -f Dockerfile.test .
```

## License

MIT
