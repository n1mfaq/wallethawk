# 🦅 WalletHawk

> Telegram bot that watches your TRC20 (Tron) wallets and pings you the second USDT moves in or out.

**Stack:** .NET 9 · ASP.NET Core hosted services · EF Core · PostgreSQL · Telegram.Bot · TronGrid · Docker

```
WalletHawk.sln
├── src/
│   ├── WalletHawk.Domain          // entities, abstractions, pure logic
│   ├── WalletHawk.Data            // EF Core DbContext + migrations (PostgreSQL)
│   ├── WalletHawk.Infrastructure  // TronGrid HTTP client, Telegram notifier
│   ├── WalletHawk.Bot             // Telegram update handler (long-polling)
│   └── WalletHawk.Worker          // periodic poller of tracked wallets
└── docker-compose.yml             // Postgres + Bot + Worker
```

## Quick start

### 1. Get a bot token
Open [@BotFather](https://t.me/BotFather) → `/newbot` → copy the token.

### 2. Configure
```bash
cp .env.example .env
# fill in BOT_TOKEN, optional TRONGRID_API_KEY
```

### 3. Run with Docker
```bash
docker compose up --build -d
docker compose logs -f bot
```

The bot will:
- spin up Postgres
- run EF Core migrations on startup
- start polling Telegram and TRC20 explorers

### 4. Try it
In Telegram open your bot and send:
```
/start
/add TKasX7gvbdvg4nnJ8BzQ2TNWrKjXpieHau coffee fund
/list
/me
```

## Local development (without Docker)

```bash
docker compose up -d postgres
dotnet run --project src/WalletHawk.Bot
dotnet run --project src/WalletHawk.Worker   # in another terminal
```

Set `Bot:Token` either via `appsettings.json` (don't commit) or environment variable:
```bash
export Bot__Token="123:abc..."
```

## Commands

| Command | What it does |
|---|---|
| `/start`, `/help` | Welcome + commands list |
| `/add <addr> [label]` | Track a TRC20 wallet |
| `/list` | Show tracked wallets |
| `/remove <id>` | Stop tracking |
| `/me` | Show plan + counters |
| `/upgrade` | Pro plan info |

## Roadmap

- [x] TRC20-USDT tracking
- [ ] Multi-token (TRX native, JST, WIN, etc.)
- [ ] BTC + EVM (BSC, Ethereum, Polygon)
- [ ] PnL tracking with cost basis
- [ ] Web dashboard
- [ ] Telegram Stars / CryptoBot Pay integration

## License

MIT
