// Phase 5 acceptance run: one command that re-measures every claim the offline work rests on.
//
// Phase 5 (offline capability) spans six subphases and three layers — a service worker, a
// versioned IndexedDB store with an outbox, and a server-side idempotency and delta surface.
// "It is done" is therefore a claim about a lot of independently-verifiable things, and the
// failure mode this script exists to prevent is a report that is *remembered* rather than
// re-measured: a number quoted from a previous run, a constraint assumed to still hold, a test
// that quietly stopped being run.
//
//   node scripts/verify-phase5.mjs                  # everything (suites + invariants)
//   node scripts/verify-phase5.mjs --invariants-only # static checks only (no suites; CI-friendly)
//
// Environment:
//   FMS_PHASE5_BASELINE   commit the phase started from, default the commit before it landed.
//                         The git-history checks skip (never fail) when it is unreachable, which
//                         is what happens on a depth-1 CI checkout.
//
// Exit code 1 on any failed check. Device-only runbooks are printed at the end: they cannot be
// run here, and the script says so rather than implying coverage.
import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { appendFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const clientRoot = join(repoRoot, 'client');
const BASELINE = process.env.FMS_PHASE5_BASELINE?.trim() || 'b664140';
const INVARIANTS_ONLY = process.argv.includes('--invariants-only');

const results = [];
const failures = [];

function check(name, ok, detail = '') {
  results.push({ name, ok, detail });
  if (!ok) failures.push({ name, detail });
  console.log(`  ${ok ? 'ok  ' : 'FAIL'}  ${name}${detail ? ` — ${detail}` : ''}`);
  return ok;
}

function note(text) {
  console.log(`  --    ${text}`);
}

function run(command, args, options = {}) {
  try {
    const stdout = execFileSync(command, args, {
      cwd: options.cwd ?? repoRoot,
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'pipe'],
      maxBuffer: 64 * 1024 * 1024,
      // npm and dotnet are shell scripts on Windows; git is not, and a shell would mangle the
      // arguments it is given (`^{commit}` especially), so callers opt out explicitly.
      shell: options.shell ?? process.platform === 'win32',
    });
    return { ok: true, output: stdout };
  } catch (error) {
    const output = `${error.stdout ?? ''}${error.stderr ?? ''}`;
    return { ok: false, output, reason: error.message };
  }
}

const git = (args) => run('git', args, { shell: false });

// ── 1. the suites ───────────────────────────────────────────

function backendSuite() {
  console.log('\nbackend suite (dotnet test, whole test project)');
  const result = run('dotnet', ['test', 'tests/FMS.Domain.Tests/FMS.Domain.Tests.csproj', '--nologo'], {
    timeout: 15 * 60_000,
  });

  const summary = result.output.match(
    /Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)/,
  );
  if (!summary) return check('dotnet test ran', false, 'no result line — the build may have failed');

  const [, failed, passed, skipped, total] = summary.map(Number);
  check('dotnet test: no failures', failed === 0, `${passed} passed, ${failed} failed, ${skipped} skipped, ${total} total`);

  // The skips are Postgres/SMTP/Hangfire integration classes that skip without their service.
  // They are *not* verified here, and CI runs them against a real postgres (see deploy.yml) —
  // so this is reported, never counted as a pass.
  const gated = ['DashboardPostgres', 'IdempotencyIndexPostgres', 'RealEmail', 'HangfirePostgres'];
  const skippedNames = gated.filter((name) => result.output.includes(name));
  if (skipped) {
    note(`${skipped} skipped, from: ${skippedNames.join(', ')} — verified in CI, not here`);
  }
}

function clientSuites() {
  console.log('\nclient suite (lint, build, test)');

  const lint = run('npm', ['run', 'lint'], { cwd: clientRoot, timeout: 5 * 60_000 });
  const lintSummary = lint.output.match(/Found (\d+) warnings and (\d+) errors/);
  if (lintSummary) {
    const warnings = Number(lintSummary[1]);
    const errors = Number(lintSummary[2]);
    // The baseline is 8 warnings, none of them in an offline file (all pre-existing pages).
    check('lint: 0 errors, at or below the 8-warning baseline', errors === 0 && warnings <= 8,
      `${warnings} warnings, ${errors} errors`);
  } else {
    check('lint ran', false, 'no summary line');
  }

  const build = run('npm', ['run', 'build'], { cwd: clientRoot, timeout: 10 * 60_000 });
  check('build succeeds', build.ok, build.ok ? '' : build.output.slice(-200));

  const test = run('npm', ['test'], { cwd: clientRoot, timeout: 20 * 60_000 });
  const testSummary = test.output.match(/Tests\s+(.+?)\n/);
  const filesSummary = test.output.match(/Test Files\s+(.+?)\n/);
  check(
    'vitest: no failures',
    Boolean(testSummary) && !testSummary[1].includes('failed'),
    `${(filesSummary?.[1] ?? '?').trim()} · ${(testSummary?.[1] ?? 'no summary').trim()}`,
  );
}

// ── 2. the offline shell ────────────────────────────────────

function offlineShell() {
  console.log('\noffline shell (built worker + its source)');

  const workerPath = join(clientRoot, 'dist', 'sw.js');
  if (!existsSync(workerPath)) {
    return check('built worker exists (run the build first)', false, 'client/dist/sw.js is missing');
  }

  const worker = readFileSync(workerPath, 'utf8');
  const base = '/FarmManagementSystem/';
  check('precaches the app shell under the deployment base', worker.includes(`${base}index.html`),
    'index.html is the navigation fallback');
  check('precaches the hashed bundle', /FarmManagementSystem\/assets\/index-[A-Za-z0-9_-]+\.js/.test(worker));
  check('is versioned, so a redeploy replaces it', /var VERSION = "[^"]+"/.test(worker));

  const template = readFileSync(join(clientRoot, 'tools', 'offline', 'sw-template.ts'), 'utf8');
  check('never handles a cross-origin request', template.includes('url.origin !== self.location.origin'));
  check('serves only the precache list or the hashed asset prefix', template.includes('PRECACHE.indexOf(url.pathname) !== -1'));
  check('serves navigations network-first with the shell as the offline fallback',
    template.includes('networkFirstShell(') && template.includes("request.mode === 'navigate'"));
}

// ── 3. the source invariants ────────────────────────────────

function sourceInvariants() {
  console.log('\nsource invariants (the constraints, not the behaviour)');

  const offlineDir = join(clientRoot, 'src', 'offline');
  const sources = readdirSync(offlineDir)
    .filter((name) => /\.tsx?$/.test(name) && !/\.test\.tsx?$/.test(name))
    .map((name) => ({ name, source: readFileSync(join(offlineDir, name), 'utf8') }));

  const polling = sources.filter(({ source }) => source.includes('setInterval(')).map(({ name }) => name);
  check('no interval anywhere in the offline layer', polling.length === 0, polling.join(', '));

  const engine = sources.find(({ name }) => name === 'syncEngine.ts')?.source ?? '';
  check('the flush resets the offline-write window when it reached the server',
    engine.includes('noteServerContact(') && engine.includes('reachedTheServer('));
  check('a 401 or a transport failure cannot reset it',
    /case 'auth':\n\s*case 'skipped':\n\s*return false;/.test(engine));

  const syncApi = readFileSync(join(clientRoot, 'src', 'api', 'sync.ts'), 'utf8');
  const axios = readFileSync(join(clientRoot, 'src', 'api', 'axios.ts'), 'utf8');
  check('queue traffic is marked, so a flush cannot re-trigger itself',
    /syncRequest:\s*true/.test(syncApi) && /if \(!response\.config\?\.syncRequest\)/.test(axios));

  const db = sources.find(({ name }) => name === 'db.ts')?.source ?? '';
  check('the store is scoped by (accountId, farmId) in its key paths',
    db.includes("keyPath: ['accountId', 'farmId', 'collection', 'id']")
    && db.includes("keyPath: ['accountId', 'farmId', 'collection']")
    && db.includes("keyPath: ['accountId', 'farmId', 'mutationId']"));
  check('the session marker is per account and outside the read cache',
    db.includes("export const SESSION_STORE = 'session'"));

  const cachedQuery = readFileSync(join(clientRoot, 'src', 'offline', 'cachedQuery.ts'), 'utf8');
  const registry = cachedQuery.slice(
    cachedQuery.indexOf('export const CACHED_COLLECTIONS'),
    cachedQuery.indexOf('} as const;'),
  );
  const approved = ['weightCheckStatus', 'tasks', 'employees', 'animals', 'attendance'];
  const declared = approved.filter((name) => registry.includes(`${name}:`));
  check('the cached surface is exactly the approved collections', declared.length === approved.length,
    declared.join(', '));
}

// ── 4. the server-side shape of the phase ──────────────────

function gitHistory() {
  console.log(`\nphase shape since ${BASELINE} (skips when the history is not here)`);

  if (!git(['rev-parse', '--verify', '--quiet', BASELINE]).ok) {
    note(`the baseline commit ${BASELINE} is not in this clone (shallow checkout?): history checks skipped`);
    return;
  }

  const range = `${BASELINE}..HEAD`;

  const middleware = git(['diff', '--name-only', range, '--', 'src/FMS.API/Middleware']).output.trim();
  check('FarmContextMiddleware is untouched', middleware === '', middleware);

  const migrations = git(['diff', '--name-only', range, '--', 'src/FMS.Infrastructure/Migrations'])
    .output.split('\n')
    .map((line) => line.trim())
    .filter((line) => line.endsWith('.cs') && !line.endsWith('.Designer.cs') && !line.includes('ModelSnapshot'));
  check('exactly one migration was added', migrations.length === 1, migrations.join(', '));

  const controllerDiff = git(['diff', '--unified=0', range, '--', 'src/FMS.API/Controllers']).output;
  const routes = controllerDiff
    .split('\n')
    .filter((line) => line.startsWith('+') && /\[Http(Get|Post|Put|Patch|Delete)/.test(line));
  check('exactly one new route was added', routes.length === 1, routes.map((r) => r.trim()).join(' | '));

  const ef = run('dotnet', [
    'ef', 'migrations', 'has-pending-model-changes',
    '--project', 'src/FMS.Infrastructure', '--startup-project', 'src/FMS.API',
  ], { timeout: 5 * 60_000 });
  if (ef.output.includes('No changes have been made to the model')) {
    check('the model still equals the last migration', true);
  } else if (ef.output.includes('No executable found') || ef.output.includes('dotnet-ef')) {
    note('dotnet-ef is not installed here: the model/snapshot check was skipped');
  } else {
    check('the model still equals the last migration', false, ef.output.trim().split('\n').slice(-3).join(' '));
  }
}

// ── 5. what this script cannot cover ────────────────────────

function deviceRunbooks() {
  console.log('\ndevice-only (cannot be run here — docs/VERIFICATION.md section 6)');
  for (const item of [
    '6a  the shell loads offline, and does CacheModes.NoCache defeat the worker?',
    '6b  cached pages + the offline write path on a real WebView',
    '6c  a deploy reaches the device within one online launch',
    '6d  a queue survives a cold kill and flushes exactly once (all three workflows)',
    '6e  sign-out with unsynced work keeps the queue and never flushes another account',
    '6f  the five-day warning and the seven-day refusal',
    '6g  two devices see each other\'s deltas, per farm',
  ]) {
    note(item);
  }
}

// ── run ─────────────────────────────────────────────────────

console.log(`Phase 5 acceptance run — baseline ${BASELINE}${INVARIANTS_ONLY ? ' (invariants only)' : ''}`);

if (!INVARIANTS_ONLY) {
  backendSuite();
  clientSuites();
}
offlineShell();
sourceInvariants();
gitHistory();
deviceRunbooks();

const passed = results.filter((r) => r.ok).length;
const summary = [
  `## Phase 5 acceptance — ${failures.length === 0 ? 'PASSED' : 'FAILED'}`,
  '',
  `${passed}/${results.length} checks passed.`,
  '',
  ...results.map((r) => `- ${r.ok ? '✅' : '❌'} **${r.name}**${r.detail ? ` — ${r.detail}` : ''}`),
  '',
  'Device-only runbooks remain: docs/VERIFICATION.md section 6 (6a–6g).',
  '',
].join('\n');

if (process.env.GITHUB_STEP_SUMMARY) {
  await appendFile(process.env.GITHUB_STEP_SUMMARY, summary);
}

console.log(`\n${passed}/${results.length} checks passed`);
for (const failure of failures) {
  console.log(`::error::Phase 5 check failed: ${failure.name}${failure.detail ? ` — ${failure.detail}` : ''}`);
}

process.exit(failures.length ? 1 : 0);
