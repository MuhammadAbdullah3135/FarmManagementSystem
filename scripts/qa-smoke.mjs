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
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

const BASE = process.env.FMS_API ?? 'https://fms-api-3ba95327d590.herokuapp.com/api';
const ORIGIN = new URL(BASE).origin;
const EMAIL = process.env.FMS_EMAIL ?? 'Test@apk.com';
const PASSWORD = process.env.FMS_PASSWORD ?? 'testApk123';
const EXPECTED_SHA = process.env.FMS_SMOKE_SHA?.trim() || null;
const EXPECTED_FARM = process.env.FMS_SMOKE_FARM?.trim() || null;
// The deployed web app, as a full URL. Pages serves this repo as a *project* site, so the path
// matters: the bare host is GitHub's 404.
//
// The CORS origin used to be a second constant with its own default, and the two were allowed
// to disagree — which is exactly the shape of bug this script exists to catch, sitting inside
// the script. Deriving one from the other makes a wrong URL and a wrong origin the same
// mistake, so they cannot drift apart again.
const PAGES_URL = process.env.FMS_PAGES_URL ?? 'https://muhammadabdullah3135.github.io/FarmManagementSystem/';
// An Origin is scheme + host + port. The project path is deliberately NOT part of it, so this
// is the same string the browser sends and the API has to allow — and the preflight below
// compares against exactly this value for exactly that reason.
const PAGES_ORIGIN = new URL(PAGES_URL).origin;
// The Pages workflow and the Heroku workflow are separate runs, so the API can be serving a
// new build while Pages is still publishing the previous one. A freshness check that does not
// allow for that is a flake waiting to page somebody at 3am, so it polls briefly before failing.
const PAGES_SETTLE_MS = Number(process.env.FMS_PAGES_SETTLE_MS ?? 120_000);
const READ_ONLY = process.argv.includes('--read-only') || process.env.FMS_SMOKE_READONLY === '1';
const REQUEST_TIMEOUT_MS = 20_000;

const results = [];
const failures = [];
// The repository root, for reading the bundles the freshness probes are sampled from. The
// script is run from the repository root by the deploy workflow; a `script/`-relative guess
// would be wrong in one of the two layouts it has been run in.
const ROOT = process.cwd();

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

// Heroku's router answers an unknown hostname with its own HTML page, so the request never
// reached the API at all. That is a different problem from an API that answered (and a raw body
// of hex escapes hides it), so name the cause instead of pasting the body.
const isEdgePage = (response) =>
  typeof response.status === 'number' &&
  response.status >= 400 &&
  /<title>\s*(No such app|Application Error)/i.test(response.text ?? '');

const describe = (response) => {
  if (isEdgePage(response)) {
    return `${response.status} Heroku's router has no app at ${new URL(BASE).host} — the request never reached the API; check FMS_API (the app's own .../api URL)`;
  }
  return `${response.status}${response.text ? ` ${response.text.slice(0, 160)}` : ''}${traceOf(response)}`;
};

const token = { value: '' };
const authHeaders = (extra = {}) => ({
  Authorization: `Bearer ${token.value}`,
  ...extra,
});

const header = (text) => console.log(`\n${text}`);

// The mode, before anything runs, because the two modes report different totals and the
// difference is five checks rather than one: 24 with the write path, 19 without it. Both were
// read off a real run. Printing the count here is what stops the two being compared with each
// other -- and the count is not derivable from the source, since the read-path section emits ten
// checks from a single call site.
//
// This line states what the run will do. It adds no check, changes no exit code, and touches no
// branch below it.
console.log(
  `Smoke check: ${READ_ONLY ? 'read-only' : 'write mode'}`
  + ` — ${READ_ONLY ? '19' : '24'} checks expected`
  + (EXPECTED_SHA ? `, asserting ${EXPECTED_SHA}` : ', provenance reported but not asserted (no FMS_SMOKE_SHA)'),
);

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

// ── 3b. the web app is actually published there ─────────────
//
// The preflight above proves the API would talk to that origin. It says nothing about whether
// anything is served there: GitHub's "Site not found" page answers 200-ish with valid CORS
// implications, and so does an empty deployment. For nine deploys this section asserted a
// configuration and reported success, and nobody noticed that it never asked whether the app
// existed — the same failure as every other one in this project, except that this one was
// sitting inside the script whose job is to catch them.
header('Web app is published');
const page = await request(PAGES_URL);
const isNotFoundPage = /Site not found/i.test(page.text);
const assets = [...page.text.matchAll(/(?:src|href)="([^"]+\.js)"/g)].map((m) => m[1]);
check(
  `${PAGES_URL} serves the app`,
  page.status === 200 && !isNotFoundPage && assets.length > 0,
  page.status !== 200
    ? `status ${page.status}`
    : isNotFoundPage
      ? 'GitHub answered with its "Site not found" page — the site is not published'
      : assets.length === 0
        ? 'the page references no JavaScript; it is not the built app'
        : `${assets.length} script asset(s)`,
);

// ── 3c. the published bundle carries this build's translations ──
//
// The API and the frontend deploy separately, so "the key is on the wire" and "the client can
// render it" are two different claims. Only the second one is observable here, and until now
// nothing observed it: a Pages deploy that silently served a stale asset would leave every
// check in this script green while every Spanish and Arabic reader saw the English fallback.
//
// The probes are sampled out of the repository's own bundles rather than written out by hand.
// A hand-written list would be one more list to keep in step — the exact thing every slice in
// this project has been removing — and it would go stale the day a translation was reworded,
// at which point it would report a healthy deploy as broken.
header('Published bundle is current');
const SAMPLE = 10;
const bundles = {
  es: JSON.parse(readFileSync(join(ROOT, 'client/src/i18n/locales/es/validation.json'), 'utf8')).validation,
  ar: JSON.parse(readFileSync(join(ROOT, 'client/src/i18n/locales/ar/validation.json'), 'utf8')).validation,
};

/**
 * An evenly spaced sample of a namespace's values.
 *
 * Evenly spaced rather than the first N or the last N, because the interesting case is a stale
 * bundle and both of those miss it: the first N are the oldest keys, which a stale bundle has in
 * abundance, and the last N depend on insertion order surviving the build. Sorted keys sampled
 * across the whole range put the newest material in the sample wherever it lands, and the
 * sample changes as the bundle grows rather than needing to be updated.
 */
function sampleSentences(entries, count) {
  const keys = Object.keys(entries).sort();
  const values = [];
  for (let i = 0; i < count; i++) {
    const key = keys[Math.floor((i * keys.length) / count)];
    const value = entries[key];
    // A value too short to be distinctive proves nothing: a two-letter string can appear
    // anywhere in a 4 MiB bundle.
    if (typeof value === 'string' && value.length >= 12) values.push({ key, value });
  }
  return values;
}

const served = assets.length ? (await Promise.all(
  assets.map(async (href) => (await request(new URL(href, PAGES_URL))).text))).join('\n') : '';

/**
 * One poll across the window, rather than a fixed sleep.
 *
 * The two deploys are independent runs, so right after a release Pages may still be publishing
 * the previous build. Retrying is the difference between a check that is right and one that is
 * right most of the time.
 */
const deadline = Date.now() + PAGES_SETTLE_MS;
let freshness;
for (;;) {
  const missing = [];
  for (const [locale, entries] of Object.entries(bundles)) {
    for (const { key, value } of sampleSentences(entries, SAMPLE)) {
      if (!served.includes(value)) missing.push(`${locale}.${key}`);
    }
  }
  freshness = missing;
  if (!missing.length || Date.now() >= deadline) break;
  console.log(`  note  the published bundle is behind the API (${missing.length} probe(s)); retrying`);
  await sleep(15_000);
}

const probeTotal = SAMPLE * Object.keys(bundles).length;
check(
  'the published bundle carries the current translations',
  assets.length > 0 && freshness.length === 0,
  // The reason matters as much as the failure. If nothing was fetched, every probe is trivially
  // missing, and reporting that as "the bundle is stale" sends whoever reads it to look at the
  // wrong deploy. The previous section already says whether the site is published at all.
  assets.length === 0
    ? 'nothing was published to check; see the section above'
    : freshness.length
      ? `${freshness.length} of ${probeTotal} sampled sentences are missing: ${freshness.slice(0, 4).join(', ')}${freshness.length > 4 ? '…' : ''}`
      : `${probeTotal} sampled sentences found across ${Object.keys(bundles).length} locales`,
);

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
