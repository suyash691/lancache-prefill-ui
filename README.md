# lancache-prefill

Web UI for managing a [Lancache](https://lancache.net/) Steam game cache. Checks for game updates, downloads content through the cache, and runs on a schedule.

Built with [SteamKit2](https://github.com/SteamRE/SteamKit) and ASP.NET.

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

## Tests

```bash
docker build -f Dockerfile.test .
```

## License

MIT
