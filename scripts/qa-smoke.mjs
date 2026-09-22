// Post-deploy smoke check for the live FMS API.
//
// Runs after a release (deploy.yml, `smoke` job) and answers the two questions no test inside
// the workflow can: is the build answering production the one that was just released, and do
// the contracts that only misbehave in production still hold?
//
//   node scripts/qa-smoke.mjs              # reads, plus one self-cleaning expense write
//   node scripts/qa-smoke.mjs --read-only  # never writes to production
//
// Environment:
//   FMS_API              default the deployed Heroku API (…/api)
//   FMS_EMAIL/PASSWORD   default the QA account
//   FMS_SMOKE_SHA        the commit this run released; /health must report it back
//   FMS_SMOKE_FARM       farm name the account must have before anything is written
//   FMS_SMOKE_READONLY   1 = same as --read-only
//
// Exit code 1 on any failure, with ::error:: annotations naming the endpoint and the API's
// traceId so the failure is findable in the Heroku logs.
import { appendFile } from 'node:fs/promises';

const BASE = process.env.FMS_API ?? 'https://fms-api-3ba95327d590.herokuapp.com/api';
const ORIGIN = new URL(BASE).origin;
const EMAIL = process.env.FMS_EMAIL ?? 'Test@apk.com';
const PASSWORD = process.env.FMS_PASSWORD ?? 'testApk123';
const EXPECTED_SHA = process.env.FMS_SMOKE_SHA?.trim() || null;
const EXPECTED_FARM = process.env.FMS_SMOKE_FARM?.trim() || null;
const PAGES_ORIGIN = process.env.FMS_PAGES_ORIGIN ?? 'https://muhammadabdullah3135.github.io';
const READ_ONLY = process.argv.includes('--read-only') || process.env.FMS_SMOKE_READONLY === '1';
const REQUEST_TIMEOUT_MS = 20_000;

const results = [];
const failures = [];

function check(name, ok, detail = '') {
  results.push({ name, ok, detail });
  if (!ok) {
    failures.push({ name, detail });
    console.log(`  FAIL  ${name}${detail ? ` — ${detail}` : ''}`);
  } else {
    console.log(`  ok    ${name}${detail ? ` — ${detail}` : ''}`);
  }
  return ok;
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function request(url, init = {}) {
  try {
    const res = await fetch(url, {
      ...init,
      signal: AbortSignal.timeout(init.timeoutMs ?? REQUEST_TIMEOUT_MS),
    });
    const text = await res.text();
    let body = null;
    try {
      body = text ? JSON.parse(text) : null;
    } catch {
      body = null;
    }
    return { status: res.status, ok: res.ok, headers: res.headers, text, body };
  } catch (error) {
    return { status: 'network', ok: false, headers: new Headers(), text: error.message, body: null };
  }
}

const api = (path, init) => request(`${BASE}${path}`, init);
const origin = (path, init) => request(`${ORIGIN}${path}`, init);

// The API answers a failure with ProblemDetails, which carries the traceId that ties the
// response to a log line in Heroku.
const traceOf = (response) => (response.body?.traceId ? ` traceId=${response.body.traceId}` : '');

const describe = (response) =>
  `${response.status}${response.text ? ` ${response.text.slice(0, 160)}` : ''}${traceOf(response)}`;

const token = { value: '' };
const authHeaders = (extra = {}) => ({
  Authorization: `Bearer ${token.value}`,
  ...extra,
});

const header = (text) => console.log(`\n${text}`);

// ── 1. the release is serving ───────────────────────────────
//
// Heroku restarts the dyno to swap the container, so the first requests after a release can
// fail. Waiting is the difference between reporting a broken build and reporting a deploy that
// has not finished.
header('Release');
{
  const deadline = Date.now() + 90_000;
  let live = { status: 'not attempted' };
  while (Date.now() < deadline) {
    live = await origin('/health/live');
    if (live.status === 200) break;
    await sleep(3000);
  }
  check('dyno answers /health/live', live.status === 200, live.status === 200 ? '' : describe(live));

  const ready = await origin('/health/ready');
  const readyOk = ready.status === 200 && ready.body?.status === 'Healthy';
  check('database reachable (/health/ready)', readyOk, readyOk ? '' : describe(ready));

  const health = await origin('/health');
  const sha = health.body?.build?.sha;
  check(
    '/health reports the build it is running',
    health.status === 200 && typeof sha === 'string' && sha.length > 0,
    health.status === 200 ? `sha=${sha ?? '(absent — is this build older than the stamp?)'}` : describe(health),
  );

  if (EXPECTED_SHA) {
    // An unstamped or mismatched build is a failure, never a skip: this is the check that
    // catches a release which never reached Heroku while the workflow looked green.
    const ok = sha === EXPECTED_SHA;
    check(
      'production is serving the commit this run released',
      ok,
      ok
        ? EXPECTED_SHA
        : `serving ${sha ?? '(nothing)'}, released ${EXPECTED_SHA}` +
            (sha && sha !== 'unknown' ? ' — a previous release is still live' : ' — the build carries no stamp'),
    );
  } else {
    console.log('  note  FMS_SMOKE_SHA is not set: provenance reported, not asserted');
  }
}

// ── 2. login ────────────────────────────────────────────────
header('Account');
{
  const login = await api('/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: EMAIL, password: PASSWORD }),
  });
  token.value = login.body?.accessToken ?? '';
  check(`login as ${EMAIL}`, login.status === 200 && !!token.value, login.status === 200 ? '' : describe(login));
}

const farms = token.value ? await api('/farms', { headers: authHeaders() }) : { status: 'skipped', body: null };
const farmList = Array.isArray(farms.body) ? farms.body : [];
check('account has a farm to check', farmList.length > 0, farmList.length ? '' : describe(farms));

const farm = EXPECTED_FARM ? farmList.find((f) => f.name === EXPECTED_FARM) : farmList[0];
// The write cycle below may only ever touch the farm the caller named: if the account no longer
// holds it, this fails rather than picking whatever farm came first and writing there.
const farmMatches = !!farm && (!EXPECTED_FARM || farm.name === EXPECTED_FARM);
if (EXPECTED_FARM && !farmMatches) {
  check(
    `account still owns the ${EXPECTED_FARM} farm`,
    false,
    `farms on the account: ${farmList.map((f) => f.name).join(', ') || '(none)'} — no write will be attempted`,
  );
}
const farmId = farmMatches ? farm.id : farmList[0]?.id;
const farmPath = farmId ? `/farm/${farmId}` : null;

// ── 3. the web app can reach the API ────────────────────────
//
// The SPA runs from a different origin, so a CORS misconfiguration breaks the entire product
// while every in-process test stays green — nothing else in the pipeline makes this check.
if (farmPath) {
  header('Web app origin');
  const preflight = await request(`${BASE}${farmPath}/dashboard/summary`, {
    method: 'OPTIONS',
    headers: {
      Origin: PAGES_ORIGIN,
      'Access-Control-Request-Method': 'GET',
      'Access-Control-Request-Headers': 'authorization,x-farm-id',
    },
  });
  const allowed = preflight.headers.get('access-control-allow-origin');
  const ok = preflight.status >= 200 && preflight.status < 300 && (allowed === PAGES_ORIGIN || allowed === '*');
  check(`preflight from ${PAGES_ORIGIN}`, ok, ok ? `allow-origin: ${allowed}` : `status ${preflight.status}, allow-origin: ${allowed}`);
}

// ── 4. the read contracts ───────────────────────────────────
//
// Every one of these has been broken in production at some point while the suite was green:
// the two 500s grouped by a computed month label inside the query, which PostgreSQL refuses to
// translate; the date-only ones answered 400 because a naive DateTime reached a timestamptz
// column; and the summary is where "real PostgreSQL computed every metric" is observable
// (degradedMetrics is what the API says when it had to give up on one).
if (farmPath) {
  header('Contracts');
  const now = new Date();
  const today = now.toISOString().slice(0, 10);
  const year = now.getUTCFullYear();

  const reads = [
    {
      label: 'dashboard/summary',
      path: `${farmPath}/dashboard/summary`,
      assert: (b) => Array.isArray(b.degradedMetrics) && b.degradedMetrics.length === 0 && b.totalAnimals > 0,
      expect: 'every metric computed, animals > 0',
    },
    {
      label: 'dashboard/charts',
      path: `${farmPath}/dashboard/charts`,
      assert: (b) => Array.isArray(b.animalTrends) && b.animalTrends.length > 0,
      expect: 'animal trend has buckets',
    },
    {
      label: 'reports/animals',
      path: `${farmPath}/reports/animals`,
      assert: (b) => Array.isArray(b.growthTrend),
      expect: 'growth trend present',
    },
    { label: 'dashboard/alerts', path: `${farmPath}/dashboard/alerts`, assert: (b) => Array.isArray(b) },
    { label: `feed/tasks?date=${today}`, path: `${farmPath}/feed/tasks?date=${today}&page=1&pageSize=10`, expect: 'date-only query accepted' },
    {
      label: `finance/reports/monthly-summary?year=${year}`,
      path: `${farmPath}/finance/reports/monthly-summary?year=${year}`,
      assert: (b) => Array.isArray(b) && b.length === 12,
      expect: 'twelve months',
    },
    { label: 'finance/reports/expense-breakdown', path: `${farmPath}/finance/reports/expense-breakdown?from=${year}-01-01&to=${year}-12-31`, expect: 'date range accepted' },
    { label: 'finance/reports/profit-loss', path: `${farmPath}/finance/reports/profit-loss?from=${year}-01-01&to=${year}-12-31`, expect: 'date range accepted' },
    { label: `health/costs/by-month?year=${year}`, path: `${farmPath}/health/costs/by-month?year=${year}`, expect: 'year accepted' },
    { label: 'attendance', path: `${farmPath}/attendance?from=${year}-01-01&to=${year}-12-31`, expect: 'date range accepted' },
  ];

  for (const read of reads) {
    const response = await api(read.path, { headers: authHeaders() });
    if (response.status !== 200) {
      check(read.label, false, describe(response));
      continue;
    }
    const shapeOk = read.assert ? read.assert(response.body) : true;
    check(read.label, shapeOk, shapeOk ? read.expect ?? '' : `200 but the shape is wrong: ${response.text.slice(0, 160)}`);
  }
}

// ── 5. the write path ───────────────────────────────────────
//
// The create that answered 500 in production (trace 0HNONSA8QMOC4:00000004) is what shipped the
// whole release this check belongs to, and no read-only test can see it.
if (READ_ONLY) {
  header('Write path');
  console.log('  skip  --read-only: nothing was written');
} else if (!farmPath || !farmMatches) {
  header('Write path');
  check('write path', false, 'skipped: the farm this run may write to could not be confirmed');
} else {
  header('Write path');
  const expensesPath = `${farmPath}/finance/expenses`;
  const list = () => api(`${expensesPath}?page=1&pageSize=100`, { headers: authHeaders() });

  const before = await list();
  const categories = await api(`${farmPath}/finance/expense-categories`, { headers: authHeaders() });
  const methods = await api(`${farmPath}/finance/payment-methods`, { headers: authHeaders() });
  const first = (body) => (Array.isArray(body) ? body[0] : body?.items?.[0]);
  const categoryId = first(categories.body)?.id;
  const methodId = first(methods.body)?.id;

  if (!categoryId || !methodId) {
    check('expense lookups available', false, `${categories.status}/${methods.status} — cannot create without a category and a payment method`);
  } else {
    // Exactly what the SPA sends: a date-only expenseDate, which is the value the naive-DateTime
    // path rejected.
    const payload = {
      expenseDate: new Date().toISOString().slice(0, 10),
      amount: 13.37,
      expenseCategoryId: categoryId,
      paymentMethodId: methodId,
      description: 'Post-deploy smoke check — deleted by the same run',
    };

    const created = await api(expensesPath, {
      method: 'POST',
      headers: authHeaders({ 'Content-Type': 'application/json' }),
      body: JSON.stringify(payload),
    });
    const id = created.body?.id;
    const createdOk = created.status === 201 && !!id;
    check('expense can be created with the payload the web app sends', createdOk, createdOk ? `id=${id}` : describe(created));

    if (createdOk) {
      try {
        const listed = await list();
        // Asserted by the id being present rather than by the total going up by one: the QA
        // farm is shared with people, and a concurrent change should not fail this check.
        const appears = (listed.body?.items ?? []).some((row) => row.id === id);
        check('the created expense is in the list', appears, appears ? `listed with ${listed.body?.totalCount ?? '?'} rows` : 'not found in the first page');

        const updated = await api(`${expensesPath}/${id}`, {
          method: 'PUT',
          headers: authHeaders({ 'Content-Type': 'application/json' }),
          body: JSON.stringify({ ...payload, amount: 42.42, description: 'Post-deploy smoke check — updated' }),
        });
        const amount = updated.body?.amount;
        check('expense can be updated', updated.status === 200 && amount === 42.42, updated.status === 200 ? `amount=${amount}` : describe(updated));
      } finally {
        // In a finally block on purpose: a failure above must not leave a row behind on a farm
        // people use.
        const deleted = await api(`${expensesPath}/${id}`, { method: 'DELETE', headers: authHeaders() });
        const deletedOk = deleted.status === 204 || deleted.status === 200;
        check('the smoke expense is cleaned up', deletedOk, deletedOk ? '' : `LEFTOVER id=${id} — ${describe(deleted)}`);
      }

      const after = await list();
      const gone = !(after.body?.items ?? []).some((row) => row.id === id);
      check('the expense is gone afterwards', gone, gone ? '' : `id=${id} is still listed`);
      console.log(`  note  expenses before ${before.body?.totalCount ?? '?'}, after ${after.body?.totalCount ?? '?'}`);
    }
  }
}

// ── 6. report ───────────────────────────────────────────────
const passed = results.filter((r) => r.ok).length;
const summary = [
  `## Smoke check — ${failures.length === 0 ? 'passed' : 'FAILED'}`,
  '',
  `API: \`${BASE}\`${EXPECTED_SHA ? `\nReleased commit: \`${EXPECTED_SHA}\`` : ''}`,
  `${passed}/${results.length} checks passed.`,
  '',
  ...results.map((r) => `- ${r.ok ? '✅' : '❌'} **${r.name}**${r.detail ? ` — ${r.detail}` : ''}`),
  '',
].join('\n');

if (process.env.GITHUB_STEP_SUMMARY) {
  await appendFile(process.env.GITHUB_STEP_SUMMARY, summary);
}

console.log(`\n${passed}/${results.length} checks passed`);
if (failures.length) {
  // Annotations so a failure is visible on the run page without opening the log.
  for (const failure of failures) {
    console.log(`::error::Smoke check failed: ${failure.name}${failure.detail ? ` — ${failure.detail}` : ''}`);
  }
}

process.exit(failures.length ? 1 : 0);
