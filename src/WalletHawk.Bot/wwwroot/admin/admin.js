/* WalletHawk admin panel.
 * Auth: either Telegram WebApp initData header, or ?token=… in URL (saved to localStorage).
 */

const API = '/api/admin';
const tg = window.Telegram?.WebApp;
if (tg) { tg.ready(); tg.expand(); }

const url = new URL(location.href);
const tokenFromUrl = url.searchParams.get('token');
if (tokenFromUrl) {
    localStorage.setItem('admin_token', tokenFromUrl);
    url.searchParams.delete('token');
    history.replaceState({}, '', url);
}
const token = localStorage.getItem('admin_token') || '';

function authHeaders() {
    const h = { 'Accept': 'application/json' };
    if (token) h['X-Admin-Token'] = token;
    if (tg?.initData) h['X-Telegram-Init-Data'] = tg.initData;
    return h;
}

async function api(path, opts = {}) {
    const res = await fetch(API + path, {
        ...opts,
        headers: { ...authHeaders(), ...(opts.headers || {}) },
        cache: 'no-store',
    });
    if (res.status === 401) throw new Error('unauthorized');
    if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        throw new Error(body.error || `HTTP ${res.status}`);
    }
    if (res.status === 204) return null;
    return res.json();
}

// ── helpers ────────────────────────────────────────────────────────────
const $ = (sel) => document.querySelector(sel);
const fmtN = (n) => new Intl.NumberFormat('en-US').format(Number(n) || 0);
const fmt2 = (n) => new Intl.NumberFormat('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(Number(n) || 0);
const fmtDate = (iso) => iso ? new Date(iso).toISOString().slice(0, 10) : '—';
const escHtml = (s) => String(s ?? '').replace(/[&<>"']/g, c => (
    { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]
));
function mask(addr) {
    if (!addr) return '';
    return addr.length <= 12 ? addr : `${addr.slice(0, 6)}…${addr.slice(-4)}`;
}

function showError(msg) {
    const el = $('#error');
    el.textContent = msg;
    el.hidden = false;
}
function toast(msg, error = false) {
    const t = $('#toast');
    t.textContent = msg;
    t.classList.toggle('err', error);
    t.hidden = false;
    setTimeout(() => { t.hidden = true; }, 2200);
}

// ── chart (mini) ───────────────────────────────────────────────────────
/**
 * Fill missing days with zeros so a single datapoint doesn't render as a giant bar.
 * @param {{date:string,count:number}[]} byDay — server data
 * @param {number} days — chart window in days
 */
function fillDays(byDay, days) {
    const map = new Map(byDay.map(d => [d.date, Number(d.count) || 0]));
    const out = [];
    const today = new Date();
    today.setUTCHours(0, 0, 0, 0);
    for (let i = days - 1; i >= 0; i--) {
        const d = new Date(today);
        d.setUTCDate(today.getUTCDate() - i);
        const key = d.toISOString().slice(0, 10);
        out.push({ date: key, count: map.get(key) || 0 });
    }
    return out;
}

function drawBars(svgEl, byDay, color, days = 30) {
    svgEl.innerHTML = '';
    const series = fillDays(byDay, days);
    const total = series.reduce((s, d) => s + d.count, 0);

    if (total === 0) {
        const t = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        t.setAttribute('x', '50%');
        t.setAttribute('y', '60');
        t.setAttribute('fill', 'rgba(255,255,255,0.3)');
        t.setAttribute('text-anchor', 'middle');
        t.setAttribute('font-size', '11');
        t.textContent = 'no data yet';
        svgEl.appendChild(t);
        return;
    }

    const W = 600, H = 120, pad = 6;
    const inner = { x: pad, y: pad, w: W - pad * 2, h: H - pad * 2 };
    const max = Math.max(...series.map(d => d.count), 1);
    const barW = inner.w / series.length;
    const ns = 'http://www.w3.org/2000/svg';

    // baseline
    const base = document.createElementNS(ns, 'line');
    base.setAttribute('x1', inner.x);
    base.setAttribute('x2', inner.x + inner.w);
    base.setAttribute('y1', inner.y + inner.h);
    base.setAttribute('y2', inner.y + inner.h);
    base.setAttribute('stroke', 'rgba(255,255,255,0.08)');
    svgEl.appendChild(base);

    series.forEach((d, i) => {
        if (d.count === 0) return;
        const h = (d.count / max) * inner.h;
        const r = document.createElementNS(ns, 'rect');
        r.setAttribute('x', inner.x + i * barW + 1);
        r.setAttribute('y', inner.y + inner.h - h);
        r.setAttribute('width', Math.max(2, barW - 2));
        r.setAttribute('height', h);
        r.setAttribute('rx', 2);
        r.setAttribute('fill', color);
        svgEl.appendChild(r);
    });
}

// ── overview ───────────────────────────────────────────────────────────
async function loadOverview() {
    const o = await api('/overview');
    document.querySelector('[data-kpi="users"]').textContent = fmtN(o.users);
    document.querySelector('[data-kpi="pro"]').textContent = fmtN(o.pro);
    document.querySelector('[data-kpi="wallets"]').textContent = fmtN(o.wallets);
    document.querySelector('[data-kpi="totalTx"]').textContent = fmtN(o.totalTx);
    document.querySelector('[data-kpi="mrr"]').textContent = '$' + fmt2(o.mrr);
    document.querySelector('[data-kpi="newUsers24h"]').textContent = fmtN(o.newUsers24h);
    document.querySelector('[data-kpi="newWallets24h"]').textContent = fmtN(o.newWallets24h);
    drawBars($('#chart-users'), o.newUsersByDay, '#00f0ff');
    drawBars($('#chart-tx'),    o.txByDay,       '#9b5cff');
}

// ── users table ────────────────────────────────────────────────────────
async function loadUsers(q = '') {
    const tbody = $('#users-table tbody');
    tbody.innerHTML = '<tr><td colspan="7" class="empty">loading…</td></tr>';
    try {
        const rows = await api('/users' + (q ? `?q=${encodeURIComponent(q)}` : ''));
        if (!rows.length) {
            tbody.innerHTML = '<tr><td colspan="7" class="empty">no users</td></tr>';
            return;
        }
        tbody.innerHTML = rows.map(u => `
            <tr data-id="${u.id}">
                <td>${u.id}</td>
                <td>${u.username ? '@' + escHtml(u.username) : '<i style="color:#666">—</i>'}</td>
                <td>${escHtml(u.firstName || '—')}</td>
                <td>${u.isPro
                    ? `<span class="pro">PRO${u.proUntil ? ' · ' + fmtDate(u.proUntil) : ''}</span>`
                    : '<span class="free">free</span>'}</td>
                <td>${u.walletCount}</td>
                <td>${fmtDate(u.createdAt)}</td>
                <td class="actions">
                    <button class="ghost" data-action="open" data-id="${u.id}">open</button>
                </td>
            </tr>`).join('');
        tbody.querySelectorAll('tr').forEach(tr => {
            tr.addEventListener('click', (e) => {
                if (e.target.closest('button[data-action="open"]') || e.target === tr || e.target.tagName === 'TD') {
                    openDrawer(tr.dataset.id);
                }
            });
        });
    } catch (e) {
        tbody.innerHTML = `<tr><td colspan="7" class="empty">${escHtml(e.message)}</td></tr>`;
    }
}

let searchTimer = 0;
$('#users-search').addEventListener('input', e => {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(() => loadUsers(e.target.value.trim()), 250);
});

// ── drawer (user detail) ───────────────────────────────────────────────
async function openDrawer(id) {
    const drawer = $('#drawer');
    const backdrop = $('#drawer-backdrop');
    drawer.hidden = false;
    backdrop.hidden = false;
    $('#drawer-title').textContent = `user #${id}`;
    $('#drawer-body').innerHTML = '<p style="color:#888;padding:20px;text-align:center">loading…</p>';

    try {
        const data = await api(`/users/${id}`);
        const u = data.user;
        const handle = u.username ? '@' + u.username : `id ${u.telegramUserId}`;

        const info = `
            <div class="field"><span>tg id</span><b>${u.telegramUserId}</b></div>
            <div class="field"><span>username</span><b>${u.username ? '@' + escHtml(u.username) : '—'}</b></div>
            <div class="field"><span>name</span><b>${escHtml(u.firstName || '—')}</b></div>
            <div class="field"><span>plan</span><b>${u.isPro
                ? `<span class="pro">PRO</span>${u.proUntil ? ' until ' + fmtDate(u.proUntil) : ''}`
                : '<span class="free">free</span>'}</b></div>
            <div class="field"><span>joined</span><b>${fmtDate(u.createdAt)}</b></div>
        `;

        const wallets = data.wallets.length
            ? data.wallets.map(w => `
                <li>
                    <b>${escHtml(w.label || 'unlabeled')}</b><br>
                    ${escHtml(w.address)}<br>
                    <i>${escHtml(w.network)} · added ${fmtDate(w.createdAt)} · last checked ${w.lastCheckedAt ? new Date(w.lastCheckedAt).toLocaleString() : 'never'}</i>
                </li>`).join('')
            : '<li><i>no wallets</i></li>';

        const txs = data.transactions.length
            ? data.transactions.map(t => {
                const sign = t.direction === 'in' ? '+' : '−';
                const arrow = t.direction === 'in' ? '↓' : '↑';
                return `
                <li>
                    <span class="ico ${t.direction}">${arrow}</span>
                    <div class="meta">
                        <b>${escHtml(mask(t.counterparty))}</b>
                        <i>${new Date(t.blockTime).toLocaleString()} · ${escHtml(mask(t.txHash))}</i>
                    </div>
                    <div class="amount ${t.direction}">${sign}${fmt2(t.amount)} ${escHtml(t.token)}</div>
                </li>`;
            }).join('')
            : '<li><i>no transactions yet</i></li>';

        $('#drawer-title').textContent = handle;
        $('#drawer-body').innerHTML = `
            <h4>profile</h4>
            ${info}

            <div class="actions">
                ${u.isPro
                    ? `<button class="danger" data-action="revoke" data-id="${u.id}">revoke pro</button>`
                    : `<button data-action="grant" data-id="${u.id}" data-days="30">grant pro · 30d</button>
                       <button class="ghost" data-action="grant" data-id="${u.id}" data-days="365">grant pro · 365d</button>`}
            </div>

            <h4>wallets (${data.wallets.length})</h4>
            <ul class="subwallets">${wallets}</ul>

            <h4>recent transactions</h4>
            <ul class="subtxs">${txs}</ul>
        `;

        $('#drawer-body').querySelectorAll('button[data-action]').forEach(btn => {
            btn.addEventListener('click', () => userAction(btn.dataset.action, btn.dataset.id, btn.dataset.days));
        });
    } catch (e) {
        $('#drawer-body').innerHTML = `<div class="error" style="display:block">${escHtml(e.message)}</div>`;
    }
}

function closeDrawer() {
    $('#drawer').hidden = true;
    $('#drawer-backdrop').hidden = true;
}
$('#drawer-close').addEventListener('click', closeDrawer);
$('#drawer-backdrop').addEventListener('click', closeDrawer);
document.addEventListener('keydown', (e) => { if (e.key === 'Escape') closeDrawer(); });

async function userAction(action, id, days) {
    const path = action === 'grant'
        ? `/users/${id}/grant?days=${days || 30}`
        : `/users/${id}/revoke`;
    try {
        await api(path, { method: 'POST' });
        toast(action === 'grant' ? 'Pro granted ✓' : 'Pro revoked');
        await openDrawer(id);
        await loadUsers($('#users-search').value.trim());
        await loadOverview();
    } catch (e) {
        toast(e.message, true);
    }
}

// ── broadcast ──────────────────────────────────────────────────────────
$('#bc-send').addEventListener('click', async () => {
    const message = $('#bc-msg').value.trim();
    if (!message) return;
    if (!confirm('Send to ALL users?')) return;
    const btn = $('#bc-send');
    const status = $('#bc-status');
    btn.disabled = true;
    status.textContent = 'sending…';
    try {
        const r = await api('/broadcast', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ message }),
        });
        status.textContent = `delivered ${r.ok}/${r.total}, failed ${r.failed}`;
        toast(`Broadcast: ${r.ok} delivered`);
        $('#bc-msg').value = '';
    } catch (e) {
        toast(e.message, true);
        status.textContent = 'failed';
    } finally {
        btn.disabled = false;
    }
});

// ── boot ───────────────────────────────────────────────────────────────
(async () => {
    try {
        await api('/whoami');
        $('#auth-state').textContent = tg?.initData ? 'auth: telegram' : 'auth: token';
        $('#auth-state').classList.add('ok');
        await Promise.all([loadOverview(), loadUsers()]);
    } catch (e) {
        $('#auth-state').textContent = 'unauthorized';
        $('#auth-state').classList.add('err');
        showError(
            'Access denied. Open this page from inside the WalletHawk bot ' +
            '(/panel command), or append ?token=YOUR_ADMIN_TOKEN to the URL.'
        );
    }
})();
