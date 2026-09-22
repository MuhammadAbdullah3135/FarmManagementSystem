// Read-only survey of the QA API: prints how many rows each list endpoint returns.
const BASE = process.env.FMS_API ?? 'https://fms-api-3ba95327d590.herokuapp.com/api';
const EMAIL = process.env.FMS_EMAIL ?? 'Test@apk.com';
const PASSWORD = process.env.FMS_PASSWORD ?? 'testApk123';

const loginRes = await fetch(`${BASE}/auth/login`, {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ email: EMAIL, password: PASSWORD }),
});
const loginText = await loginRes.text();
let login = null;
try {
  login = JSON.parse(loginText);
} catch {
  login = null;
}
if (!loginRes.ok || !login?.accessToken) {
  throw new Error(`login failed for ${EMAIL}: HTTP ${loginRes.status} ${login?.error ?? loginText.slice(0, 200)}`);
}

const farms = await fetch(`${BASE}/farms`, {
  headers: { Authorization: `Bearer ${login.accessToken}` },
}).then((r) => r.json());
if (!Array.isArray(farms) || farms.length === 0) {
  throw new Error(`no farms on the ${EMAIL} account — nothing to survey`);
}
const farmId = farms[0].id;
console.log('farm', farmId, farms[0].name);

const H = { Authorization: `Bearer ${login.accessToken}`, 'X-Farm-Id': farmId };

function countOf(body) {
  if (Array.isArray(body)) return body.length;
  if (body && typeof body === 'object') {
    if (typeof body.totalCount === 'number') return body.totalCount;
    if (Array.isArray(body.items)) return body.items.length;
  }
  return '?';
}

const endpoints = [
  ['configuration/animal-types', 'configuration/animal-types'],
  ['configuration/breeds', 'configuration/breeds'],
  ['configuration/sex-options', 'configuration/sex-options'],
  ['configuration/age-categories', 'configuration/age-categories'],
  ['configuration/statuses', 'configuration/statuses'],
  ['configuration/identification-types', 'configuration/identification-types'],
  ['configuration/location-types', 'configuration/location-types'],
  ['configuration/locations', 'configuration/locations'],
  ['configuration/custom-fields', 'configuration/custom-fields'],
  ['animals', 'animals'],
  ['breeding-records', 'breeding-records'],
  ['gestation', 'gestation'],
  ['births', 'births'],
  ['feed/types', 'feed/types'],
  ['feed/records', 'feed/records'],
  ['feed/diet-plans', 'feed/diet-plans'],
  ['feed/schedules', 'feed/schedules'],
  ['feed/tasks', 'feed/tasks'],
  ['feed/inventory/stock', 'feed/inventory/stock'],
  ['medicines', 'medicines'],
  ['vaccines', 'vaccines'],
  ['vaccinations', 'vaccinations'],
  ['vaccinations/schedule', 'vaccinations/schedule'],
  ['medical-records', 'medical-records'],
  ['weight-schedules', 'weight-schedules'],
  ['employees', 'employees'],
  ['departments', 'departments'],
  ['employee-roles', 'employee-roles'],
  ['attendance', 'attendance'],
  ['performance-reviews', 'performance-reviews'],
  ['finance/expense-categories', 'finance/expense-categories'],
  ['finance/payment-methods', 'finance/payment-methods'],
  ['finance/expenses', 'finance/expenses'],
  ['finance/income-categories', 'finance/income-categories'],
  ['finance/income-records', 'finance/income-records'],
  ['inventory-items', 'inventory-items'],
  ['inventory/suppliers', 'inventory/suppliers'],
  ['inventory/supplier-purchases', 'inventory/supplier-purchases'],
  ['inventory/customers', 'inventory/customers'],
  ['inventory/customer-sales', 'inventory/customer-sales'],
  ['tasks', 'tasks'],
];

for (const [path, label] of endpoints) {
  const url = `${BASE}/farm/${farmId}/${path}`;
  try {
    const res = await fetch(url, { headers: H });
    const text = await res.text();
    let body;
    try { body = JSON.parse(text); } catch { body = text; }
    const n = res.ok ? countOf(body) : `HTTP ${res.status}`;
    console.log(`${String(n).padStart(6)}  ${label}`);
    if (!res.ok) console.log(`        ${String(text).slice(0, 200)}`);
  } catch (e) {
    console.log(`   ERR  ${label}: ${e.message}`);
  }
}
