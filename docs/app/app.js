/* WalletHawk Mini App — fetch user data and render dashboard. */

const API_BASE = 'https://wallethawk-bot.fly.dev';
const tg = window.Telegram?.WebApp;

// ── boot ────────────────────────────────────────────────────────────
document.getElementById('year').textContent = new Date().getFullYear();
if (tg) {
    tg.ready();
    tg.expand();
    // Match Telegram's color scheme — keeps things native feeling.
    document.body.style.backgroundColor = tg.themeParams.bg_color || '';
}

// ── helpers ─────────────────────────────────────────────────────────
function showError(msg) {
    const el = document.getElementById('error');
    el.textContent = msg;
    el.hidden = false;
}

function fmt(n, digits = 2) {
    const num = Number(n) || 0;
    return new Intl.NumberFormat('en-US', { maximumFractionDigits: digits, minimumFractionDigits: digits }).format(num);
}

function mask(addr) {
    if (!addr) return '';
    return addr.length <= 12 ? addr : `${addr.slice(0, 6)}…${addr.slice(-4)}`;
}

function fmtTime(iso) {
    const d = new Date(iso);
    if (isNaN(d.getTime())) return '';
    const now = new Date();
    const sameDay = d.toDateString() === now.toDateString();
    return sameDay
        ? d.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' })
        : d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
}

async function api(path) {
    const initData = tg?.initData || '';
    if (!initData) {
        throw new Error('Open this page from inside the WalletHawk bot in Telegram.');
    }
    const res = await fetch(`${API_BASE}${path}`, {
        headers: {
            'Accept': 'application/json',
            'X-Telegram-Init-Data': initData,
        },
        cache: 'no-store',
    });
    if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        throw new Error(body.error || `HTTP ${res.status}`);
    }
    return res.json();
}

// ── render ──────────────────────────────────────────────────────────
function renderUser(me) {
    const hello = document.getElementById('hello');
    const name = me.firstName || (me.username ? '@' + me.username : 'there');
    hello.textContent = `Hello, ${name}.`;

    const badge = document.getElementById('plan-badge');
    if (me.isPro) {
        badge.textContent = 'PRO';
        badge.classList.add('pro');
    } else {
        badge.textContent = 'FREE';
        badge.classList.remove('pro');
    }

    document.querySelector('[data-kpi="wallets"]').textContent = me.walletCount ?? 0;
}

function renderStats(stats) {
    document.querySelector('[data-kpi="in"]').textContent = fmt(stats.totalIn, 2);
    document.querySelector('[data-kpi="out"]').textContent = fmt(stats.totalOut, 2);
    drawChart(stats.byDay || []);
}

function renderWallets(list) {
    const ul = document.getElementById('wallets');
    if (!list.length) {
        ul.innerHTML = '<li class="empty">No wallets yet. Use /add &lt;address&gt; in the bot.</li>';
        return;
    }
    ul.innerHTML = list.map(w => `
        <li>
            <div class="lbl">${escapeHtml(w.label || 'unlabeled')}</div>
            <div class="addr">${escapeHtml(w.address)}</div>
        </li>`).join('');
}

function renderTxs(list) {
    const ul = document.getElementById('txs');
    if (!list.length) {
        ul.innerHTML = '<li class="empty">No transactions tracked yet.</li>';
        return;
    }
    ul.innerHTML = list.map(t => {
        const isIn = t.direction === 'in';
        const arrow = isIn ? '↓' : '↑';
        const cls = isIn ? 'in' : 'out';
        const sign = isIn ? '+' : '−';
        const label = t.walletLabel || mask(t.walletAddress);
        return `
            <li>
                <div class="ico ${cls}">${arrow}</div>
                <div class="meta">
                    <div class="top">${escapeHtml(label)}</div>
                    <div class="sub">${escapeHtml(mask(t.counterparty))} · ${fmtTime(t.blockTime)}</div>
                </div>
                <div class="amount ${cls}">${sign}${fmt(t.amount, 2)} ${escapeHtml(t.token)}</div>
            </li>`;
    }).join('');
}

function escapeHtml(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => (
        { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]
    ));
}

// ── chart (SVG, no dependencies) ────────────────────────────────────
function drawChart(byDay) {
    const svg = document.getElementById('chart');
    const empty = document.getElementById('chart-empty');
    svg.innerHTML = '';

    if (!byDay.length) {
        empty.hidden = false;
        return;
    }
    empty.hidden = true;

    const W = 600, H = 160, pad = 12;
    const inner = { x: pad, y: pad, w: W - pad * 2, h: H - pad * 2 };

    const max = Math.max(
        ...byDay.map(d => Number(d.in) || 0),
        ...byDay.map(d => Number(d.out) || 0),
        1
    );
    const barW = inner.w / byDay.length;
    const groupGap = 2;
    const half = (barW - groupGap * 2) / 2;

    const ns = 'http://www.w3.org/2000/svg';

    // baseline
    const base = document.createElementNS(ns, 'line');
    base.setAttribute('x1', inner.x);
    base.setAttribute('x2', inner.x + inner.w);
    base.setAttribute('y1', inner.y + inner.h);
    base.setAttribute('y2', inner.y + inner.h);
    base.setAttribute('stroke', 'rgba(255,255,255,0.08)');
    svg.appendChild(base);

    byDay.forEach((d, i) => {
        const cx = inner.x + i * barW + groupGap;
        const inH = ((Number(d.in)  || 0) / max) * inner.h;
        const outH = ((Number(d.out) || 0) / max) * inner.h;

        const inBar = document.createElementNS(ns, 'rect');
        inBar.setAttribute('x', cx);
        inBar.setAttribute('y', inner.y + inner.h - inH);
        inBar.setAttribute('width', half);
        inBar.setAttribute('height', inH);
        inBar.setAttribute('fill', '#29d97e');
        inBar.setAttribute('rx', 2);
        svg.appendChild(inBar);

        const outBar = document.createElementNS(ns, 'rect');
        outBar.setAttribute('x', cx + half + groupGap);
        outBar.setAttribute('y', inner.y + inner.h - outH);
        outBar.setAttribute('width', half);
        outBar.setAttribute('height', outH);
        outBar.setAttribute('fill', '#ff5f7a');
        outBar.setAttribute('rx', 2);
        svg.appendChild(outBar);
    });
}

// ── main ─────────────────────────────────────────────────────────────
(async () => {
    if (!tg || !tg.initData) {
        showError('Open this page from inside the @wallethawk_bot in Telegram.');
        return;
    }

    try {
        const [me, stats, wallets, txs] = await Promise.all([
            api('/api/me'),
            api('/api/me/stats?days=7'),
            api('/api/me/wallets'),
            api('/api/me/transactions?days=7'),
        ]);
        renderUser(me);
        renderStats(stats);
        renderWallets(wallets);
        renderTxs(txs);
    } catch (e) {
        showError(e.message || 'Failed to load.');
    }
})();
