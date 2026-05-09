# 🦅 WalletHawk

> Telegram bot that watches your TRC20 (Tron) wallets and pings you the second USDT moves in or out.

**Stack:** .NET 10 · ASP.NET Core · EF Core · PostgreSQL · Telegram.Bot · TronGrid · CryptoBot Pay · Fly.io · GitHub Pages

Live:

| What | URL |
|---|---|
| Bot | [@wallethawk_bot](https://t.me/wallethawk_bot) |
| Landing | https://n1mfaq.github.io/wallethawk/ |
| Mini App (in Telegram) | https://n1mfaq.github.io/wallethawk/app/ |
| Admin panel | `https://wallethawk-bot.fly.dev/admin/` (token-protected) |
| Public stats API | `https://wallethawk-bot.fly.dev/stats` |

---

## How it works

1. User opens [@wallethawk_bot](https://t.me/wallethawk_bot) in Telegram, runs `/add <TRC20_address> [label]`.
2. The **worker** polls TronGrid every 30 seconds for each tracked wallet, diffs new TRC20 transfers against `Wallet.LastTxHash`, and persists them into the `transactions` table.
3. The **bot** sends an instant Telegram alert (`📥 IN 500 USDT · from TXyz…aBcDef`) with a one-click TronScan link.
4. Free plan: 2 wallets. Pro plan ($9.99/mo or $79.99/year via [@CryptoBot](https://t.me/CryptoBot)): unlimited wallets.
5. Mini App dashboard inside Telegram: KPI block, 7-day in/out chart, wallet list, recent transactions.

```
┌──────────────────────────────────────────────────────────────┐
│  GitHub Pages (docs/)        Fly.io (Dockerfiles)            │
│  ┌─────────────┐             ┌──────────┐    ┌────────────┐  │
│  │  landing    │             │   Bot    │    │   Worker   │  │
│  │  +  app/    │ ── /api ──▶ │ ASP.NET  │    │ HostedSvc  │  │
│  │  + og.svg   │             │  Core    │    │  every 30s │  │
│  └─────────────┘             └────┬─────┘    └─────┬──────┘  │
│                                   │                │          │
│                              wallethawk-db (Postgres)         │
└──────────────────────────────────────────────────────────────┘
```

---

## Repo layout

```
src/
├── WalletHawk.Domain          # entities, abstractions, pure logic
├── WalletHawk.Data            # EF Core DbContext + migrations
├── WalletHawk.Infrastructure  # TronGrid client, Telegram notifier, CryptoBot client
├── WalletHawk.Bot             # ASP.NET Core: long-polling bot, /api/me, /api/admin,
│                              #   webhook, public /stats, static /admin/ UI
└── WalletHawk.Worker          # background polling of tracked wallets

docs/                          # GitHub Pages site (landing + Mini App)
├── index.html  styles.css
├── og.svg                     # OG/Twitter preview image
└── app/                       # Mini App (Telegram WebApp)
    ├── index.html  app.css  app.js

src/WalletHawk.Bot/wwwroot/admin/   # Web admin panel (served by the bot)
├── index.html  admin.css  admin.js

scripts/deploy.sh              # convenience deploy wrapper
fly.bot.toml  fly.worker.toml  # Fly.io app configs
docker-compose.yml             # local Postgres + bot + worker
```

---

## Run locally (Docker) — for contributors / self-hosters

```bash
git clone https://github.com/n1mfaq/wallethawk.git
cd wallethawk
cp .env.example .env
# edit .env: at minimum BOT_TOKEN; the rest are optional for local dev
docker compose up --build -d
docker compose logs -f bot
```

The compose file spins up Postgres, runs EF migrations on bot startup, and starts long-polling Telegram. CryptoBot, admin panel and Mini App are not strictly required for local development — you can develop against the bot commands alone.

### .env reference

```bash
BOT_TOKEN=                  # required — from @BotFather
BOT_OWNER=                  # optional — your @username for /upgrade fallback
TRONGRID_API_KEY=           # optional — without it, public TronGrid limits apply
CRYPTOBOT_API_KEY=          # optional — needed only to test paid Pro flow
```

> **Why bother running it locally if the live bot already works?**
> You only need a local copy if you want to **contribute** (fix bugs, add new chains, etc.) or self-host your own private instance. End users just use [@wallethawk_bot](https://t.me/wallethawk_bot) in Telegram — no installation needed.

---

## Run locally (without Docker)

```bash
docker compose up -d postgres
export Bot__Token="123:abc..."
export ConnectionStrings__Postgres="Host=localhost;Username=wallethawk;Password=wallethawk;Database=wallethawk"

dotnet run --project src/WalletHawk.Bot       # one terminal
dotnet run --project src/WalletHawk.Worker    # another terminal
```

---

## Bot commands

### Public

| Command | What it does |
|---|---|
| `/start`, `/help` | welcome + commands list |
| `/add <addr> [label]` | track a TRC20 wallet |
| `/list` | show tracked wallets |
| `/remove <id>` | stop tracking |
| `/me` | show plan + counters |
| `/dashboard`, `/app` | open the Mini App |
| `/upgrade` | go Pro via CryptoBot Pay |

### Admin (only the configured `Bot:OwnerTelegramId`)

| Command | What it does |
|---|---|
| `/admin` | admin help |
| `/panel` | open the web admin panel |
| `/stats` | usage counters (HTML monospace block) |
| `/user <@name\|tg_id>` | show user info |
| `/wallets <@name\|tg_id>` | list a user's wallets |
| `/grant_pro <@name\|tg_id> [days=30]` | grant Pro |
| `/revoke_pro <@name\|tg_id>` | revoke Pro |
| `/broadcast <message>` | send to ALL users (rate-limited) |

---

## Public API

| Endpoint | Auth | Description |
|---|---|---|
| `GET /healthz` | none | health check |
| `GET /stats` | CORS-open | `{users, wallets, pro, updatedAt}` for the landing page |
| `POST /webhooks/cryptobot` | HMAC-SHA256 (CryptoBot signature) | activates Pro on `invoice_paid` |
| `GET /api/me`, `/api/me/wallets`, `/api/me/transactions`, `/api/me/stats` | Telegram WebApp `initData` HMAC | Mini App data for the calling user |
| `GET /api/admin/*` | `X-Admin-Token` header **or** Telegram WebApp `initData` + `IsAdmin(userId)` | Admin overview / users / drill-down / actions |

---

## Production deploy (Fly.io)

One-time setup:

```bash
fly auth login
fly apps create wallethawk-bot
fly apps create wallethawk-worker
fly postgres create --name wallethawk-db --region fra
fly postgres attach wallethawk-db --app wallethawk-bot
fly postgres attach wallethawk-db --app wallethawk-worker \
    --database-name wallethawk_bot --database-user wallethawk_worker

# Bot secrets — note the SSL fix for Fly's flycast tunnel
fly secrets set --app wallethawk-bot \
  ConnectionStrings__Postgres='Host=wallethawk-db.flycast;Port=5432;Database=wallethawk_bot;Username=wallethawk_bot;Password=...;SSL Mode=Disable;Trust Server Certificate=true' \
  Bot__Token='123:abc...' \
  Bot__OwnerTelegramId=123456789 \
  Bot__AdminToken="$(openssl rand -hex 32)" \
  TronGrid__ApiKey='...' \
  CryptoBot__ApiKey='...'

# Worker — same connection string but with the worker DB user
fly secrets set --app wallethawk-worker \
  ConnectionStrings__Postgres='...;SSL Mode=Disable;...' \
  Bot__Token='123:abc...' \
  TronGrid__ApiKey='...'

# Deploy both
./scripts/deploy.sh
```

In [@CryptoBot](https://t.me/CryptoBot) → **Crypto Pay → Apps → Webhooks**, point the webhook URL at:

```
https://wallethawk-bot.fly.dev/webhooks/cryptobot
```

### Gotchas worth knowing

- Fly's `*.flycast` tunnel is already encrypted (WireGuard). Npgsql tries TLS by default and fails — you **must** set `SSL Mode=Disable` in the connection string.
- The Worker must explicitly reference `Microsoft.EntityFrameworkCore.Relational` (it's already in `WalletHawk.Data.csproj`); .NET 10 NuGet pruning otherwise drops the runtime assembly.
- Two bot instances on the same token = `409 Conflict` on `getUpdates`. If you run `docker compose` locally **and** Fly at the same time, only one will receive updates.

---

## Roadmap

- [x] TRC20-USDT tracking
- [x] CryptoBot Pay integration (Pro plan, end-to-end)
- [x] Telegram Mini App dashboard (Telegram WebApp `initData` auth)
- [x] Web admin panel (`/admin/`) with KPIs, charts, drill-down, broadcast
- [x] Landing page on GitHub Pages with live `/stats`
- [ ] Multi-token (TRX native, JST, WIN…)
- [ ] BTC + EVM (BSC, Ethereum, Polygon)
- [ ] Per-wallet balance + USD price in alerts
- [ ] Weekly digest

---

## License

MIT
