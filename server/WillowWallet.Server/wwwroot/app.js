// WillowWallet Server web panel. Plain JavaScript, no build step.
import { ICONS } from './icons.js';

const $app = document.getElementById('app');
const state = { page: 'overview', overview: null, timer: null, me: null, version: '' };

// ----- Helpers -----

const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

function icon(name, cls = '') {
  const paths = ICONS[name] || ICONS[name.replace('-fill', '')] || [];
  return `<svg class="icon ${cls}" viewBox="0 0 20 20" aria-hidden="true">${paths.map((d) => `<path d="${d}"/>`).join('')}</svg>`;
}

async function api(path, { method = 'GET', body } = {}) {
  const res = await fetch('/admin/api' + path, {
    method,
    headers: { 'Content-Type': 'application/json', 'X-WillowWallet': 'admin' },
    body: body === undefined ? undefined : JSON.stringify(body),
    credentials: 'same-origin',
  });
  if (res.status === 401 && path !== '/login') { showSignIn(); throw new Error('Signed out'); }
  const text = await res.text();
  const data = text ? JSON.parse(text) : {};
  if (!res.ok) throw new Error(data.message || `Error ${res.status}`);
  return data;
}

function toast(message, error = false) {
  const el = document.createElement('div');
  el.className = 'toast' + (error ? ' error' : '');
  el.innerHTML = icon(error ? 'warning' : 'checkmark') + `<span>${esc(message)}</span>`;
  document.getElementById('toasts').appendChild(el);
  setTimeout(() => { el.style.transition = 'opacity .3s'; el.style.opacity = '0'; setTimeout(() => el.remove(), 300); }, 3800);
}

function bytes(n) {
  if (n == null || n < 0) return '—';
  const u = ['B', 'KB', 'MB', 'GB', 'TB']; let i = 0;
  while (n >= 1024 && i < u.length - 1) { n /= 1024; i++; }
  return `${n < 10 && i > 0 ? n.toFixed(1) : Math.round(n)} ${u[i]}`;
}
const num = (n) => (n ?? 0).toLocaleString();
function ago(iso) {
  if (!iso) return 'never';
  const s = (Date.now() - new Date(iso).getTime()) / 1000;
  if (s < 60) return 'just now';
  if (s < 3600) return `${Math.floor(s / 60)} min ago`;
  if (s < 86400) return `${Math.floor(s / 3600)} h ago`;
  if (s < 86400 * 2) return 'yesterday';
  if (s < 86400 * 30) return `${Math.floor(s / 86400)} days ago`;
  return new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
}
const date = (iso) => iso ? new Date(iso).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' }) : '—';
const dateTime = (iso) => new Date(iso).toLocaleString(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });
function uptime(sec) {
  const d = Math.floor(sec / 86400), h = Math.floor(sec % 86400 / 3600), m = Math.floor(sec % 3600 / 60);
  return d ? `${d}d ${h}h` : h ? `${h}h ${m}m` : `${m} min`;
}
const COLORS = ['#1f6b4f', '#2563a8', '#7c3aed', '#b4235a', '#c2410c', '#0e7490', '#4d5b8a', '#9a6b10'];
const avatar = (name, size = 34) => `<span class="avatar" style="background:${COLORS[[...name].reduce((a, c) => a + c.charCodeAt(0), 0) % COLORS.length]};width:${size}px;height:${size}px;font-size:${Math.round(size * 0.42)}px">${esc(name[0])}</span>`;
const platformIcon = (p) => /android|ios|phone/i.test(p || '') ? 'phone' : 'desktop';
const ACT_ICONS = { signup: 'person', signin: 'sign-out', recovery: 'shield-checkmark', password: 'lock-closed', admin: 'settings', invite: 'ticket-diagonal', service: 'server', settings: 'settings' };

async function copy(text) {
  try { await navigator.clipboard.writeText(text); toast('Copied'); }
  catch { toast('Copy it from the box (your browser blocked copying)', true); }
}

function confirmBox({ title, message, confirm = 'OK', danger = false, iconName }) {
  return new Promise((resolve) => {
    const scrim = document.createElement('div'); scrim.className = 'scrim modal-scrim';
    const m = document.createElement('div'); m.className = 'modal'; m.setAttribute('role', 'dialog'); m.setAttribute('aria-modal', 'true');
    m.innerHTML = `<div class="badge-icon ${danger ? 'warn' : ''}">${icon(iconName || (danger ? 'warning' : 'info'), 'lg')}</div>
      <h2>${esc(title)}</h2><p>${esc(message)}</p>
      <div class="actions"><button class="btn" data-no>Cancel</button><button class="btn ${danger ? 'danger-fill' : 'primary'}" data-yes>${esc(confirm)}</button></div>`;
    const done = (v) => { scrim.remove(); m.remove(); document.removeEventListener('keydown', key); resolve(v); };
    const key = (e) => { if (e.key === 'Escape') done(false); };
    scrim.onclick = () => done(false);
    m.querySelector('[data-no]').onclick = () => done(false);
    m.querySelector('[data-yes]').onclick = () => done(true);
    document.addEventListener('keydown', key);
    document.body.append(scrim, m);
    m.querySelector('[data-yes]').focus();
  });
}

function toggleTheme() {
  const dark = document.documentElement.dataset.theme
    ? document.documentElement.dataset.theme === 'dark'
    : matchMedia('(prefers-color-scheme: dark)').matches;
  document.documentElement.dataset.theme = dark ? 'light' : 'dark';
  try { localStorage.setItem('ww-theme', document.documentElement.dataset.theme); } catch {}
  render();
}
const isDark = () => document.documentElement.dataset.theme ? document.documentElement.dataset.theme === 'dark' : matchMedia('(prefers-color-scheme: dark)').matches;

// ----- Sign-in and first-time setup -----

function authPage(setup) {
  clearInterval(state.timer);
  $app.innerHTML = `<div class="auth"><form class="card" autocomplete="on">
    <img class="logo" src="logo.png" alt="">
    <h1>${setup ? 'Set up your server' : 'WillowWallet Server'}</h1>
    <p class="lead">${setup ? 'Create the admin account for this web panel. It\'s separate from the WillowWallet accounts people sync with.' : 'Sign in to manage your sync server.'}</p>
    <div class="field"><label for="u">Admin name</label><input class="input" id="u" name="username" autocomplete="username" required minlength="3" value="${setup ? 'admin' : ''}"></div>
    <div class="field"><label for="p">Password</label><input class="input" id="p" name="password" type="password" autocomplete="${setup ? 'new-password' : 'current-password'}" required ${setup ? 'minlength="10"' : ''}>
      ${setup ? '<span class="hint">At least 10 characters.</span>' : ''}</div>
    <div class="error-text" hidden></div>
    <button class="btn primary" style="width:100%;height:46px">${setup ? 'Create admin and continue' : 'Sign in'}</button>
    <p class="muted small" style="text-align:center;margin:18px 0 0">WillowWallet Server ${esc(state.version)} · Bytehawk Studio</p>
  </form></div>`;
  const form = $app.querySelector('form');
  form.querySelector(setup ? '#p' : '#u').focus();
  form.onsubmit = async (e) => {
    e.preventDefault();
    const err = form.querySelector('.error-text');
    err.hidden = true;
    try {
      await api(setup ? '/setup' : '/login', { method: 'POST', body: { username: form.u.value, password: form.p.value } });
      start();
    } catch (ex) { err.textContent = ex.message; err.hidden = false; }
  };
}
const showSignIn = () => authPage(false);

// ----- Shell -----

const PAGES = [
  ['overview', 'Overview', 'home'],
  ['users', 'Users', 'people'],
  ['invites', 'Invites', 'ticket-diagonal'],
  ['settings', 'Settings', 'settings'],
  ['logs', 'Logs', 'document-text'],
];

function shell(content) {
  const o = state.overview;
  const running = o ? o.running : true;
  return `<div class="shell">
    <nav class="sidebar" aria-label="Main">
      <div class="brand"><img src="logo.png" alt=""><div><b>WillowWallet</b><span>Sync server</span></div></div>
      ${PAGES.map(([id, label, ic]) => `<button class="nav ${state.page === id ? 'active' : ''}" data-page="${id}" ${state.page === id ? 'aria-current="page"' : ''}>
        ${icon(state.page === id ? ic + '-fill' : ic)}<span>${label}</span>${id === 'users' && o ? `<span class="count">${o.users}</span>` : ''}</button>`).join('')}
      <div class="spacer"></div>
      <div class="status-card"><div class="row"><span class="dot ${running ? '' : 'off'}"></span>${running ? 'Syncing' : 'Sync stopped'}</div>
        <div class="detail">${o ? `${num(o.onlineUsers)} online · up ${uptime(o.uptimeSeconds)}` : ''}</div></div>
      <button class="nav" data-theme-toggle>${icon(isDark() ? 'weather-sunny' : 'weather-moon')}<span>${isDark() ? 'Light mode' : 'Dark mode'}</span></button>
      <button class="nav" data-signout>${icon('sign-out')}<span>Sign out ${esc(state.me || '')}</span></button>
    </nav>
    <main class="main" id="page">${content}</main>
  </div>`;
}

function wireShell() {
  $app.querySelectorAll('[data-page]').forEach((b) => b.onclick = () => go(b.dataset.page));
  $app.querySelector('[data-theme-toggle]').onclick = toggleTheme;
  $app.querySelector('[data-signout]').onclick = async () => { await api('/logout', { method: 'POST', body: {} }); showSignIn(); };
}

function go(page) {
  state.page = page;
  history.replaceState(null, '', '#' + page);
  render();
}

async function render() {
  clearInterval(state.timer);
  const pages = { overview: overviewPage, users: usersPage, invites: invitesPage, settings: settingsPage, logs: logsPage };
  await (pages[state.page] || overviewPage)();
}

function paint(html, wire) {
  const scrollY = window.scrollY;
  $app.innerHTML = shell(html);
  wireShell();
  wire?.();
  window.scrollTo(0, scrollY);
}

// ----- Overview -----

function chart(o) {
  const w = 720, h = 170, pad = 28, n = o.requestsPerMinute.length;
  const max = Math.max(4, ...o.requestsPerMinute);
  const bw = (w - pad) / n;
  const y = (v) => h - 22 - (v / max) * (h - 40);
  const bars = o.requestsPerMinute.map((v, i) => {
    const e = o.errorsPerMinute[i];
    return `<rect class="bar" x="${pad + i * bw + 1}" y="${y(v)}" width="${Math.max(1, bw - 2)}" height="${h - 22 - y(v)}" rx="2"><title>${v} requests, ${o.syncsPerMinute[i]} syncs (${60 - i} min ago)</title></rect>` +
      (e ? `<rect class="bar err" x="${pad + i * bw + 1}" y="${y(e)}" width="${Math.max(1, bw - 2)}" height="${h - 22 - y(e)}" rx="2"/>` : '');
  }).join('');
  const grid = [0, .5, 1].map((f) => `<line class="grid-line" x1="${pad}" x2="${w}" y1="${y(max * f)}" y2="${y(max * f)}"/><text x="0" y="${y(max * f) + 4}">${Math.round(max * f)}</text>`).join('');
  return `<svg class="chart" viewBox="0 0 ${w} ${h}" preserveAspectRatio="none" role="img" aria-label="Requests per minute over the last hour">${grid}${bars}
    <text x="${pad}" y="${h - 4}">60 min ago</text><text x="${w - 40}" y="${h - 4}">now</text></svg>`;
}

async function overviewPage() {
  let o;
  try { o = state.overview = await api('/overview'); } catch { return; }
  const address = location.origin;
  paint(`
    <div class="page-head"><div class="grow"><h1>${esc(o.serverName)}</h1><p>Your WillowWallet sync server at a glance.</p></div></div>
    <div class="grid">
      <section class="card hero ${o.running ? '' : 'stopped'}">
        <div class="grow">
          <div class="big"><span class="pulse"></span>${o.running ? 'Syncing' : 'Sync is stopped'}</div>
          <div class="sub">${o.running ? `Up ${uptime(o.uptimeSeconds)} · version ${esc(o.version)} · ${esc(o.runsAs)}` : 'Apps keep working with the data on their devices and catch up when you start it again.'}</div>
        </div>
        ${o.running
          ? `<button class="btn" data-act="stop">${icon('pause')}Stop sync</button>`
          : `<button class="btn primary" data-act="start">${icon('play')}Start sync</button>`}
        <button class="btn" data-act="restart">${icon('arrow-clockwise')}Restart server</button>
      </section>

      <div class="grid cols-4">
        ${stat('people', 'Users', num(o.users), `${num(o.onlineUsers)} online now · ${num(o.activeThisWeek)} this week`)}
        ${stat('phone-laptop', 'Devices', num(o.devices), 'Signed-in phones and PCs')}
        ${stat('arrow-sync', 'Records synced', num(o.records), `${num(o.totalSyncs)} syncs since start`)}
        ${stat('storage', 'Storage', bytes(o.databaseBytes + o.fileBytes), `${num(o.files)} files · ${bytes(o.freeDiskBytes)} free`)}
      </div>

      <div class="grid cols-3">
        <section class="card span-2">
          <div class="card-head"><h2>Requests, last hour</h2><div class="legend"><span><i style="background:var(--accent)"></i>Requests</span><span><i style="background:var(--danger)"></i>Errors</span></div></div>
          ${chart(o)}
        </section>
        <section class="card">
          <div class="card-head"><h2>Connect an app</h2></div>
          <p class="muted small" style="margin:0 0 12px">In WillowWallet on Windows or Android, open <b>Settings → Sync</b> and enter this address:</p>
          <div class="copy-box"><code>${esc(address)}</code><button class="btn sm" data-copy="${esc(address)}">${icon('copy', 'sm')}Copy</button></div>
          <p class="muted small" style="margin:12px 0 0">${o.openSignup ? 'Anyone who can reach this server can create an account.' : 'New people need an <a href="#invites" data-goto="invites">invite code</a>.'}
            On other devices, use this computer's network address instead of “localhost”.</p>
        </section>
      </div>

      <div class="grid cols-3">
        <section class="card span-2">
          <div class="card-head"><h2>Recent activity</h2><button class="btn sm ghost" data-goto="logs">See all</button></div>
          ${activityList(o.recentActivity)}
        </section>
        <section class="card">
          <div class="card-head"><h2>Server</h2></div>
          <dl class="kv">
            <dt>Runs as</dt><dd>${esc(o.runsAs)}</dd>
            <dt>Version</dt><dd>${esc(o.version)}</dd>
            <dt>Started</dt><dd>${esc(dateTime(o.startedAt))}</dd>
            <dt>Port</dt><dd>${o.port}${o.httpsPort ? ` (HTTPS ${o.httpsPort})` : ''}</dd>
            <dt>Memory</dt><dd>${bytes(o.memoryBytes)}</dd>
            <dt>Database</dt><dd>${bytes(o.databaseBytes)}</dd>
            <dt>System</dt><dd>${esc(o.os)}</dd>
            <dt>Data folder</dt><dd class="mono">${esc(o.dataFolder)}</dd>
          </dl>
        </section>
      </div>
    </div>`, () => {
    $app.querySelectorAll('[data-copy]').forEach((b) => b.onclick = () => copy(b.dataset.copy));
    $app.querySelectorAll('[data-goto]').forEach((b) => b.onclick = (e) => { e.preventDefault(); go(b.dataset.goto); });
    const act = (name) => $app.querySelector(`[data-act="${name}"]`);
    act('stop')?.addEventListener('click', async () => {
      if (!await confirmBox({ title: 'Stop syncing?', message: 'Apps keep working with the data on their devices. They sync again when you start it. The web panel stays on.', confirm: 'Stop sync', iconName: 'pause' })) return;
      await api('/service/stop', { method: 'POST', body: {} }); toast('Sync stopped'); render();
    });
    act('start')?.addEventListener('click', async () => { await api('/service/start', { method: 'POST', body: {} }); toast('Sync started'); render(); });
    act('restart').onclick = async () => {
      if (!await confirmBox({ title: 'Restart the server?', message: 'It takes a few seconds. Syncs in progress pick up again on their own.', confirm: 'Restart', iconName: 'arrow-clockwise' })) return;
      await api('/service/restart', { method: 'POST', body: {} });
      toast('Restarting…');
      waitForRestart();
    };
  });
  state.timer = setInterval(async () => {
    if (state.page !== 'overview' || document.querySelector('.modal')) return;
    try { state.overview = await api('/overview'); overviewPage(); } catch {}
  }, 5000);
}

async function waitForRestart() {
  $app.querySelector('#page').insertAdjacentHTML('afterbegin', '<div class="note" style="margin-bottom:16px">' + icon('arrow-clockwise') + '<span>Restarting the server… this page reconnects by itself.</span></div>');
  await new Promise((r) => setTimeout(r, 2500));
  for (let i = 0; i < 40; i++) {
    try { const r = await fetch('/admin/api/status'); if (r.ok) { toast('Server restarted'); return start(); } } catch {}
    await new Promise((r) => setTimeout(r, 1500));
  }
  toast('The server hasn\'t come back. If it runs in a terminal, start it again there.', true);
}

const stat = (ic, label, value, foot) => `<section class="card stat"><div class="label">${icon(ic)}${label}</div><div class="value">${value}</div><div class="foot">${foot}</div></section>`;

function activityList(items) {
  if (!items.length) return `<div class="empty">${icon('history', 'lg')}<p>Nothing yet. Sign-ups, sign-ins and admin changes show up here.</p></div>`;
  return `<div class="list">${items.map((a) => `<div class="item">
    <span class="badge-icon">${icon(ACT_ICONS[a.kind] || 'info', 'sm')}</span>
    <div style="flex:1"><div>${esc(a.detail)}</div><div class="muted small">${a.username ? esc(a.username) + ' · ' : ''}${esc(ago(a.at))}</div></div></div>`).join('')}</div>`;
}

// ----- Users -----

async function usersPage() {
  let users;
  try { users = await api('/users'); } catch { return; }
  const q = (state.userQuery || '').toLowerCase();
  const shown = users.filter((u) => u.username.includes(q));
  paint(`
    <div class="page-head"><div class="grow"><h1>Users</h1><p>People who sync with this server. Their budgets are end-to-end encrypted: you can manage accounts, but you can't see their data.</p></div>
      <button class="btn primary" data-goto="invites">${icon('add')}Invite someone</button></div>
    <section class="card">
      <div class="card-head"><div class="search" style="flex:1;max-width:340px">${icon('search')}<input class="input" id="q" placeholder="Search users" value="${esc(state.userQuery || '')}"></div>
        <span class="muted small">${users.length} ${users.length === 1 ? 'account' : 'accounts'}</span></div>
      ${users.length === 0
        ? `<div class="empty"><div class="badge-icon">${icon('people', 'lg')}</div><b>No users yet</b><p>Create an invite and share the code. People sign up in the app under Settings → Sync.</p></div>`
        : `<div class="table-wrap"><table><thead><tr><th>User</th><th>Status</th><th>Devices</th><th>Records</th><th>Files</th><th>Last seen</th><th>Joined</th></tr></thead><tbody>
          ${shown.map((u) => `<tr class="clickable" data-user="${u.id}" tabindex="0">
            <td><div class="user-cell">${avatar(u.username)}<b>${esc(u.username)}</b></div></td>
            <td>${u.disabled ? '<span class="pill off">Turned off</span>' : '<span class="pill">Active</span>'}</td>
            <td>${u.devices}</td><td>${num(u.records)}</td><td>${u.files ? `${u.files} · ${bytes(u.fileBytes)}` : '—'}</td>
            <td>${esc(ago(u.lastSeenAt))}</td><td>${esc(date(u.createdAt))}</td></tr>`).join('')}
          </tbody></table></div>`}
    </section>`, () => {
    $app.querySelector('[data-goto]').onclick = () => go('invites');
    const qi = $app.querySelector('#q');
    if (qi) qi.oninput = () => { state.userQuery = qi.value; usersPage().then(() => { const n = $app.querySelector('#q'); n.focus(); n.setSelectionRange(n.value.length, n.value.length); }); };
    $app.querySelectorAll('[data-user]').forEach((r) => {
      r.onclick = () => userDrawer(+r.dataset.user);
      r.onkeydown = (e) => { if (e.key === 'Enter') userDrawer(+r.dataset.user); };
    });
  });
}

async function userDrawer(id) {
  const d = await api('/users/' + id);
  const u = d.user;
  const scrim = document.createElement('div'); scrim.className = 'scrim';
  const el = document.createElement('aside'); el.className = 'drawer'; el.setAttribute('role', 'dialog'); el.setAttribute('aria-label', u.username);
  const close = () => { scrim.remove(); el.remove(); document.removeEventListener('keydown', key); };
  const key = (e) => { if (e.key === 'Escape') close(); };
  el.innerHTML = `
    <div style="display:flex;align-items:center;gap:14px;margin-bottom:20px">${avatar(u.username, 48)}
      <div style="flex:1"><h2 style="font-size:24px">${esc(u.username)}</h2><div class="muted small">Joined ${esc(date(u.createdAt))} · last seen ${esc(ago(u.lastSeenAt))}</div></div>
      <button class="btn icon-only ghost" data-close aria-label="Close">${icon('dismiss')}</button></div>
    <div class="grid cols-3" style="margin-bottom:16px">
      ${stat('arrow-sync', 'Records', num(u.records), '')}${stat('storage', 'Files', bytes(u.fileBytes), `${u.files} files`)}${stat('phone-laptop', 'Devices', u.devices, '')}</div>
    <section class="card" style="margin-bottom:16px"><div class="card-head"><h2>Devices</h2></div>
      ${d.devices.length ? `<div class="list">${d.devices.map((v) => `<div class="item"><span class="badge-icon">${icon(platformIcon(v.platform), 'sm')}</span>
        <div style="flex:1"><b>${esc(v.name)}</b><div class="muted small">${esc(v.platform)} ${esc(v.appVersion)} · ${esc(ago(v.lastSeen))}${v.ip ? ' · ' + esc(v.ip) : ''}</div></div>
        <button class="btn sm" data-device="${esc(v.id)}">Sign out</button></div>`).join('')}</div>` : '<p class="muted">Not signed in anywhere.</p>'}
    </section>
    <section class="card" style="margin-bottom:16px"><div class="card-head"><h2>Manage</h2></div>
      <div class="note" style="margin-bottom:14px">${icon('lock-closed')}<span>You can't reset ${esc(u.username)}'s password: their data is encrypted with it. If they forget it, they use their recovery key in the app (Settings → Sync → Forgot password).</span></div>
      <div style="display:flex;flex-wrap:wrap;gap:8px">
        ${u.disabled ? `<button class="btn" data-a="enable">${icon('play', 'sm')}Turn on</button>` : `<button class="btn" data-a="disable">${icon('prohibited', 'sm')}Turn off</button>`}
        <button class="btn" data-a="signout">${icon('sign-out', 'sm')}Sign out everywhere</button>
        <button class="btn danger" data-a="erase">${icon('delete', 'sm')}Erase synced data</button>
        <button class="btn danger" data-a="delete">${icon('delete', 'sm')}Delete account</button>
      </div>
    </section>
    <section class="card"><div class="card-head"><h2>Activity</h2></div>${activityList(d.activity)}</section>`;
  scrim.onclick = close;
  document.addEventListener('keydown', key);
  document.body.append(scrim, el);
  el.querySelector('[data-close]').onclick = close;
  el.querySelectorAll('[data-device]').forEach((b) => b.onclick = async () => {
    await api('/devices/' + encodeURIComponent(b.dataset.device), { method: 'DELETE' });
    toast('Device signed out'); close(); userDrawer(id); usersPage();
  });
  const actions = {
    disable: { title: `Turn off ${u.username}?`, message: 'They\'re signed out everywhere and can\'t sign in or sync until you turn the account back on. Their data stays.', confirm: 'Turn off', danger: true, done: 'Account turned off' },
    enable: { title: `Turn on ${u.username}?`, message: 'They can sign in and sync again.', confirm: 'Turn on', done: 'Account turned on' },
    signout: { title: 'Sign out everywhere?', message: `Every device ${u.username} uses has to sign in again.`, confirm: 'Sign out', done: 'Signed out everywhere' },
    erase: { title: 'Erase synced data?', message: `Deletes ${u.username}'s synced records and files from this server. The data on their devices stays and uploads again the next time they sync.`, confirm: 'Erase', danger: true, done: 'Synced data erased' },
    delete: { title: `Delete ${u.username}?`, message: 'Deletes the account and everything it synced to this server. Their devices keep their own copy. This can\'t be undone.', confirm: 'Delete account', danger: true, done: 'Account deleted' },
  };
  el.querySelectorAll('[data-a]').forEach((b) => b.onclick = async () => {
    const a = actions[b.dataset.a];
    if (!await confirmBox(a)) return;
    try {
      await api(`/users/${id}${b.dataset.a === 'delete' ? '' : '/' + b.dataset.a}`, { method: b.dataset.a === 'delete' ? 'DELETE' : 'POST', body: b.dataset.a === 'delete' ? undefined : {} });
      toast(a.done); close(); if (b.dataset.a !== 'delete') userDrawer(id); usersPage();
    } catch (e) { toast(e.message, true); }
  });
}

// ----- Invites -----

async function invitesPage(fresh) {
  let invites, settings;
  try { [invites, settings] = await Promise.all([api('/invites'), api('/settings')]); } catch { return; }
  const active = invites.filter((i) => i.usesLeft > 0 && (!i.expiresAt || new Date(i.expiresAt) > new Date()));
  paint(`
    <div class="page-head"><div class="grow"><h1>Invites</h1><p>Share a code so someone can create an account. They enter it in the app when signing up.</p></div></div>
    <div class="grid cols-3">
      <section class="card">
        <div class="card-head"><h2>New invite</h2></div>
        <form id="inv">
          <div class="field"><label for="iu">For username <span class="muted">(optional)</span></label><input class="input" id="iu" placeholder="Anyone" autocomplete="off"><span class="hint">Lock the code to one username.</span></div>
          <div class="grid cols-2" style="gap:12px"><div class="field"><label for="in">Uses</label><input class="input" id="in" type="number" min="1" max="1000" value="1"></div>
            <div class="field"><label for="id">Expires in</label><select class="input" id="id"><option value="1">1 day</option><option value="7">7 days</option><option value="14" selected>14 days</option><option value="30">30 days</option><option value="0">Never</option></select></div></div>
          <div class="field"><label for="ino">Note <span class="muted">(optional)</span></label><input class="input" id="ino" placeholder="Who it's for" autocomplete="off"></div>
          <button class="btn primary" style="width:100%">${icon('ticket-diagonal')}Create invite</button>
        </form>
        ${fresh ? `<div style="margin-top:18px"><div class="muted small" style="margin-bottom:6px">New invite code</div>
          <div class="copy-box"><code style="font-size:20px;letter-spacing:.06em">${esc(fresh.code)}</code><button class="btn sm" data-copy="${esc(fresh.code)}">${icon('copy', 'sm')}Copy</button></div></div>` : ''}
      </section>
      <section class="card span-2">
        <div class="card-head"><h2>Invites</h2><span class="muted small">${active.length} active</span></div>
        ${invites.length === 0 ? `<div class="empty"><div class="badge-icon">${icon('ticket-diagonal', 'lg')}</div><p>No invites yet.</p></div>` : `
        <div class="table-wrap"><table><thead><tr><th>Code</th><th>For</th><th>Uses left</th><th>Expires</th><th>Used by</th><th></th></tr></thead><tbody>
          ${invites.map((i) => { const dead = i.usesLeft <= 0 || (i.expiresAt && new Date(i.expiresAt) < new Date());
            return `<tr><td><span class="mono" style="font-size:14px;${dead ? 'text-decoration:line-through;opacity:.6' : ''}">${esc(i.code)}</span>${i.note ? `<div class="muted small">${esc(i.note)}</div>` : ''}</td>
            <td>${i.username ? esc(i.username) : '<span class="muted">Anyone</span>'}</td><td>${dead ? '<span class="pill grey">Used up</span>' : i.usesLeft}</td>
            <td>${i.expiresAt ? esc(date(i.expiresAt)) : 'Never'}</td><td>${i.usedBy ? esc(i.usedBy) : '—'}</td>
            <td style="text-align:right;white-space:nowrap">${dead ? '' : `<button class="btn sm ghost" data-copy="${esc(i.code)}" aria-label="Copy">${icon('copy', 'sm')}</button>`}
              <button class="btn sm ghost" data-del="${esc(i.code)}" aria-label="Delete">${icon('delete', 'sm')}</button></td></tr>`; }).join('')}
        </tbody></table></div>`}
      </section>
    </div>
    <section class="card" style="margin-top:16px">
      <label class="switch"><input type="checkbox" id="open" ${settings.openSignup ? 'checked' : ''}><span class="track"></span>
        <span class="text"><b>Open sign-up</b><span>Let anyone who can reach this server create an account without an invite.</span></span></label>
    </section>`, () => {
    $app.querySelectorAll('[data-copy]').forEach((b) => b.onclick = () => copy(b.dataset.copy));
    $app.querySelectorAll('[data-del]').forEach((b) => b.onclick = async () => {
      if (!await confirmBox({ title: 'Delete this invite?', message: `${b.dataset.del} stops working. Accounts already created with it stay.`, confirm: 'Delete', danger: true })) return;
      await api('/invites/' + encodeURIComponent(b.dataset.del), { method: 'DELETE' }); toast('Invite deleted'); invitesPage();
    });
    $app.querySelector('#inv').onsubmit = async (e) => {
      e.preventDefault();
      try {
        const inv = await api('/invites', { method: 'POST', body: { username: $app.querySelector('#iu').value || null, note: $app.querySelector('#ino').value || null, uses: +$app.querySelector('#in').value || 1, days: +$app.querySelector('#id').value || null } });
        toast('Invite created'); invitesPage(inv);
      } catch (ex) { toast(ex.message, true); }
    };
    $app.querySelector('#open').onchange = async (e) => {
      await api('/settings', { method: 'PUT', body: { openSignup: e.target.checked } });
      toast(e.target.checked ? 'Open sign-up is on' : 'Invite-only');
    };
  });
}

// ----- Settings -----

async function settingsPage() {
  let s, o;
  try { [s, o] = await Promise.all([api('/settings'), api('/overview')]); state.overview = o; } catch { return; }
  paint(`
    <div class="page-head"><div class="grow"><h1>Settings</h1><p>How this server works for the people who use it.</p></div></div>
    <div class="grid cols-2">
      <section class="card"><div class="card-head"><h2>Server</h2></div>
        <form id="sf">
          <div class="field"><label for="sn">Server name</label><input class="input" id="sn" maxlength="60" value="${esc(s.serverName)}"><span class="hint">Shown in the apps when people connect.</span></div>
          <div class="field"><label for="mu">Account limit</label><input class="input" id="mu" type="number" min="0" value="${s.maxUsers}"><span class="hint">0 for no limit.</span></div>
          <label class="switch" style="margin-bottom:18px"><input type="checkbox" id="os" ${s.openSignup ? 'checked' : ''}><span class="track"></span>
            <span class="text"><b>Open sign-up</b><span>Off: people need an invite code.</span></span></label>
          <button class="btn primary">Save</button>
        </form>
      </section>
      <section class="card"><div class="card-head"><h2>Admin password</h2></div>
        <form id="pf">
          <div class="field"><label for="cp">Current password</label><input class="input" id="cp" type="password" autocomplete="current-password" required></div>
          <div class="field"><label for="np">New password</label><input class="input" id="np" type="password" autocomplete="new-password" minlength="10" required><span class="hint">At least 10 characters.</span></div>
          <button class="btn">Change password</button>
        </form>
      </section>
      <section class="card"><div class="card-head"><h2>Backup</h2></div>
        <p class="muted" style="margin-top:0">Download a copy of the database (accounts, invites and everyone's encrypted records). Files like receipts are in the data folder: <span class="mono">${esc(o.dataFolder)}</span></p>
        <a class="btn" href="/admin/api/backup" download>${icon('arrow-download')}Download database backup</a>
      </section>
      <section class="card"><div class="card-head"><h2>About</h2></div>
        <dl class="kv"><dt>Version</dt><dd>WillowWallet Server ${esc(o.version)}</dd><dt>Runs as</dt><dd>${esc(o.runsAs)}</dd><dt>Runtime</dt><dd>${esc(o.runtime)}</dd><dt>Made by</dt><dd>Bytehawk Studio</dd></dl>
        <p class="muted small" style="margin-bottom:0">Data people sync is end-to-end encrypted on their devices: the server stores it but can't read it.</p>
      </section>
    </div>`, () => {
    $app.querySelector('#sf').onsubmit = async (e) => {
      e.preventDefault();
      try {
        await api('/settings', { method: 'PUT', body: { serverName: $app.querySelector('#sn').value, maxUsers: +$app.querySelector('#mu').value || 0, openSignup: $app.querySelector('#os').checked } });
        toast('Settings saved');
      } catch (ex) { toast(ex.message, true); }
    };
    $app.querySelector('#pf').onsubmit = async (e) => {
      e.preventDefault();
      try {
        await api('/password', { method: 'POST', body: { currentPassword: $app.querySelector('#cp').value, newPassword: $app.querySelector('#np').value } });
        toast('Password changed'); e.target.reset();
      } catch (ex) { toast(ex.message, true); }
    };
  });
}

// ----- Logs -----

async function logsPage() {
  const tab = state.logTab || 'activity';
  let rows;
  try { rows = await api(tab === 'activity' ? '/activity' : '/logs'); } catch { return; }
  const level = state.logLevel || 'all';
  const shown = tab === 'server' && level !== 'all' ? rows.filter((r) => level === 'problems' ? r.level !== 'Information' : true) : rows;
  paint(`
    <div class="page-head"><div class="grow"><h1>Logs</h1><p>What's been happening on the server.</p></div>
      <div class="tabs" role="tablist"><button class="${tab === 'activity' ? 'on' : ''}" data-tab="activity">Activity</button><button class="${tab === 'server' ? 'on' : ''}" data-tab="server">Server log</button></div></div>
    <section class="card" style="padding-bottom:0">
      ${tab === 'activity' ? activityList(rows) : `
        <div class="card-head"><div class="tabs"><button class="${level === 'all' ? 'on' : ''}" data-lvl="all">All</button><button class="${level === 'problems' ? 'on' : ''}" data-lvl="problems">Warnings and errors</button></div><span class="muted small">Newest first · refreshes every 5 seconds</span></div>
        <div class="log" style="margin:0 -22px">${shown.length ? shown.map((l) => `<div class="line"><span class="muted">${esc(dateTime(l.at))}</span><span class="lvl-${esc(l.level)}">${esc(l.level)}</span><span class="muted">${esc(l.category)}</span><span>${esc(l.message)}</span></div>`).join('') : '<div class="empty">Nothing logged yet.</div>'}</div>`}
      <div style="height:20px"></div>
    </section>`, () => {
    $app.querySelectorAll('[data-tab]').forEach((b) => b.onclick = () => { state.logTab = b.dataset.tab; logsPage(); });
    $app.querySelectorAll('[data-lvl]').forEach((b) => b.onclick = () => { state.logLevel = b.dataset.lvl; logsPage(); });
  });
  state.timer = setInterval(() => { if (state.page === 'logs') logsPage(); }, 5000);
}

// ----- Start -----

async function start() {
  try {
    const s = await (await fetch('/admin/api/status')).json();
    state.version = s.version;
    if (s.needsSetup) return authPage(true);
    if (!s.signedIn) return showSignIn();
    state.me = s.signedIn;
    const hash = location.hash.slice(1);
    if (PAGES.some(([id]) => id === hash)) state.page = hash;
    render();
  } catch {
    $app.innerHTML = '<div class="auth"><div class="card"><h1>Can\'t reach the server</h1><p class="lead">Check that WillowWallet Server is running, then reload this page.</p></div></div>';
  }
}

window.addEventListener('hashchange', () => { const h = location.hash.slice(1); if (h && h !== state.page && PAGES.some(([id]) => id === h)) go(h); });
start();
