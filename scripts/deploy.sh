#!/usr/bin/env bash
# Deploy WalletHawk to Fly.io.
# Prerequisites:
#   1. fly auth login
#   2. fly apps create wallethawk-bot
#   3. fly apps create wallethawk-worker
#   4. fly postgres create --name wallethawk-db (or use external PG)
#   5. fly postgres attach wallethawk-db --app wallethawk-bot
#   6. fly secrets set --app wallethawk-bot   Bot__Token=... TronGrid__ApiKey=... CryptoBot__ApiKey=...
#   7. fly secrets set --app wallethawk-worker Bot__Token=... TronGrid__ApiKey=...
#      and DATABASE_URL = $(fly postgres connect ...) — see README

set -euo pipefail

cd "$(dirname "$0")/.."

echo "→ Deploying bot..."
fly deploy --config fly.bot.toml --remote-only

echo "→ Deploying worker..."
fly deploy --config fly.worker.toml --remote-only

echo "✓ All deployed"
echo "   bot:    https://wallethawk-bot.fly.dev"
echo "   worker: (no HTTP, runs in background)"
