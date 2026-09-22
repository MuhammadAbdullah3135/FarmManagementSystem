// Seeds QA test data into the live FMS API for the Test@apk.com account.
// Only fills tables that are empty or thin — it never updates or deletes.
//
//   node scripts/qa-seed.mjs --dry     # show what would be created
//   node scripts/qa-seed.mjs           # create it
const BASE = process.env.FMS_API ?? 'https://fms-api-3ba95327d590.herokuapp.com/api';
const EMAIL = process.env.FMS_EMAIL ?? 'Test@apk.com';
const PASSWORD = process.env.FMS_PASSWORD ?? 'testApk123';
const DRY = process.argv.includes('--dry');

let token = '';
let farmId = '';

const login = await http('POST', '/auth/login', { email: EMAIL, password: PASSWORD }, false);
if (!login.ok) throw new Error(`login failed: ${login.status} ${login.text}`);
token = login.body.accessToken;

const farms = (await http('GET', '/farms', null, false)).body;
if (!Array.isArray(farms) || farms.length === 0) {
  throw new Error(`no farms on the ${EMAIL} account — nothing to seed`);
}
farmId = farms[0].id;
console.log(`farm ${farmId}`);

const C = (p) => `/farm/${farmId}/${p}`;

let created = 0;
let planned = 0;
let skipped = 0;

async function call(method, path, body) {
  return http(method, path, body, true);
}

async function http(method, path, body, withFarm) {
  if (DRY && method !== 'GET' && withFarm) {
    console.log(`  DRY  ${method} ${path}  ${body ? JSON.stringify(body).slice(0, 120) : ''}`);
    return { ok: true, status: 0, body: null, text: '' };
  }
  const headers = {};
  if (token) headers.Authorization = `Bearer ${token}`;
  if (withFarm && farmId) headers['X-Farm-Id'] = farmId;
  if (body !== undefined && body !== null) headers['Content-Type'] = 'application/json';
  const res = await fetch(`${BASE}${path}`, {
    method,
    headers,
    body: body === undefined || body === null || method === 'GET' ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  let parsed = null;
  try { parsed = text ? JSON.parse(text) : null; } catch { parsed = null; }
  return { ok: res.ok, status: res.status, body: parsed, text };
}
const get = (path, body, withFarm = true) => http('GET', path, body, withFarm);
const post = (path, body, _x, withFarm = true) => http('POST', path, body, withFarm);

async function create(label, path, body, method = 'POST') {
  // A dry run must not report work it did not do: nothing is sent, and the
  // record is counted as planned rather than created.
  if (DRY) {
    planned++;
    console.log(`  DRY  would create ${label}`);
    return null;
  }

  const r = await call(method, C(path), body);
  if (r.ok) { created++; console.log(`  + ${label}`); }
  else { console.log(`  ! FAILED ${label} -> ${r.status} ${r.text.slice(0, 160)}`); }
  return r.body;
}

const daysAgo = (n) => new Date(Date.now() - n * 86400000).toISOString();

// ── reference data ──────────────────────────────────────────────────
const animalTypes = (await get(C('configuration/animal-types'))).body;
const breeds = (await get(C('configuration/breeds'))).body;
const sexOptions = (await get(C('configuration/sex-options'))).body;
const ageCategories = (await get(C('configuration/age-categories'))).body;
const statuses = (await get(C('configuration/statuses'))).body;
const photoTypes = (await get(C('configuration/identification-types'))).body;
const locationTypes = (await get(C('configuration/location-types'))).body;
const locations = (await get(C('configuration/locations'))).body;
const customFields = (await get(C('configuration/custom-fields'))).body;
const feedTypes = (await get(C('feed/types'))).body;
const medicines = (await get(C('medicines?page=1&pageSize=100'))).body.items;
const vaccines = (await get(C('vaccines?page=1&pageSize=100'))).body.items;
const employees = (await get(C('employees?page=1&pageSize=100'))).body.items;
const departments = (await get(C('departments'))).body;
const roles = (await get(C('employee-roles'))).body;
const inventoryItems = (await get(C('inventory-items?page=1&pageSize=100'))).body.items;
const suppliers = (await get(C('inventory/suppliers?page=1&pageSize=100'))).body.items;
const customers = (await get(C('inventory/customers?page=1&pageSize=100'))).body.items;
const expenseCategories = (await get(C('finance/expense-categories'))).body;
const paymentMethods = (await get(C('finance/payment-methods'))).body;
const incomeCategories = (await get(C('finance/income-categories'))).body;
const animals = (await get(C('animals?page=1&pageSize=300'))).body.items;

const byName = (list, key) => new Map(list.map((x) => [String(x[key]).toLowerCase(), x]));
const findType = (name) => animalTypes.find((t) => t.name.toLowerCase() === name.toLowerCase());
const findBreed = (name) => breeds.find((b) => b.name.toLowerCase() === name.toLowerCase());
const findLocation = (name) => locations.find((l) => l.name.toLowerCase() === name.toLowerCase());
const findStatus = (name) => statuses.find((s) => s.name.toLowerCase() === name.toLowerCase());
const findAge = (name) => ageCategories.find((a) => a.name.toLowerCase() === name.toLowerCase());
const male = animals.find((a) => /male/i.test(a.sexValue) && !/female/i.test(a.sexValue));
const female = animals.find((a) => /female/i.test(a.sexValue));
const male2 = animals.filter((a) => /male/i.test(a.sexValue) && !/female/i.test(a.sexValue))[1];
const sexMale = sexOptions.find((s) => /male/i.test(s.value) && !/female/i.test(s.value));
const sexFemale = sexOptions.find((s) => /female/i.test(s.value));
const activeStatus = findStatus('Active') ?? findStatus('Active ') ?? statuses.find((s) => /active/i.test(s.name));
const sickStatus = findStatus('Sick');
const pregnantStatus = findStatus('Pregnant');

console.log(`refs: ${animalTypes.length} types, ${animals.length} animals, ${feedTypes.length} feed types, ${employees.length} employees`);

// ── 1. configuration ────────────────────────────────────────────────
console.log('\nConfiguration');
const existingFields = new Set((customFields ?? []).map((f) => f.fieldName.toLowerCase()));
for (const f of [
  { fieldName: 'Color', fieldType: 'Select', isRequired: false, options: ['Black', 'Brown', 'White', 'Mixed'] },
  { fieldName: 'Registration Number', fieldType: 'String', isRequired: false },
  { fieldName: 'Is Insured', fieldType: 'Boolean', isRequired: false },
]) {
  if (!existingFields.has(f.fieldName.toLowerCase())) {
    await create(`custom field: ${f.fieldName}`, 'configuration/custom-fields', f);
  }
}

if (!locationTypes.some((t) => /pasture/i.test(t.name))) {
  const t = await create('location type: Pasture', 'configuration/location-types', { name: 'Pasture' });
  if (t) locationTypes.push(t);
}
if (!locationTypes.some((t) => /barn/i.test(t.name))) {
  const t = await create('location type: Barn', 'configuration/location-types', { name: 'Barn' });
  if (t) locationTypes.push(t);
}
const pastureType = locationTypes.find((t) => /pasture/i.test(t.name));
const barnType = locationTypes.find((t) => /barn/i.test(t.name));
for (const [name, type] of [['Shed C', barnType], ['Grazing East', pastureType], ['Grazing West', pastureType]]) {
  if (name && type && !findLocation(name)) {
    await create(`location: ${name}`, 'configuration/locations', { name, locationTypeId: type.id });
  }
}

const wantedBreeds = [
  ['Sahiwal', 'Cattle', 283], ['Holstein Friesian', 'Cattle', 279],
  ['Nili-Ravi', 'Buffalo', 305], ['Beetal', 'Goat', 150], ['Rhode Island Red', 'Poultry', 21],
];
for (const [name, typeName, gest] of wantedBreeds) {
  const type = findType(typeName);
  if (type && !findBreed(name)) {
    await create(`breed: ${name} (${typeName})`, 'configuration/breeds', {
      name, animalTypeId: type.id, averageGestationDays: gest,
    });
  }
}

// ── 2. feed types + stock ───────────────────────────────────────────
console.log('\nFeed');
const wantedFeed = [
  ['Alfalfa Hay', 'Forage', 'Kilogram', 85],
  ['Corn Silage', 'Forage', 'Kilogram', 45],
  ['Concentrate 18%', 'Concentrate', 'Kilogram', 115],
  ['Mineral Mix', 'Mineral', 'Kilogram', 320],
  ['Fresh Water', 'Other', 'Liter', 0],
];
for (const [name, category, unit, cost] of wantedFeed) {
  if (!feedTypes.some((f) => f.name.toLowerCase() === name.toLowerCase())) {
    const f = await create(`feed type: ${name}`, 'feed/types', { name, category, unit, costPerUnit: cost });
    if (f) feedTypes.push(f);
  }
}
const freshFeed = [];
for (const f of feedTypes) freshFeed.push(f);

// A purchase movement per feed type (needed before any consumption is recorded).
const stock = (await get(C('feed/inventory/stock'))).body;
const stockedIds = new Set(stock.filter((s) => s.quantityPurchased > 0).map((s) => s.feedTypeId));
for (const f of feedTypes) {
  if (!stockedIds.has(f.id)) {
    await create(`stock purchase: ${f.name}`, 'feed/inventory/movements', {
      feedTypeId: f.id, movementType: 'Purchase', quantity: 500, unitCost: f.costPerUnit,
      supplier: 'Sibbi Feed Depot', notes: 'Seed purchase', movementDate: daysAgo(20),
    });
  }
}

const feedRecordsCount = (await get(C('feed/records?page=1&pageSize=1'))).body.totalCount;
if (feedRecordsCount < 5) {
  const targets = animals.slice(0, 6);
  const loc = findLocation('Shed A') ?? locations[0];
  const usable = feedTypes.slice(0, 4);
  for (let i = 0; i < 5 && i < targets.length; i++) {
    const f = usable[i % usable.length];
    await create(`feed record: ${targets[i].tagNumber}`, 'feed/records', {
      feedTypeId: f.id, animalId: targets[i].id, quantity: 8 + i, fedAt: daysAgo(i + 1),
      notes: 'Seed feeding',
    });
  }
  if (loc && usable.length) {
    await create('feed record: herd at location', 'feed/records', {
      feedTypeId: usable[0].id, locationId: loc.id, quantity: 40, fedAt: daysAgo(1),
      notes: 'Group feed',
    });
  }
}

const dietPlans = (await get(C('feed/diet-plans'))).body;
if (dietPlans.length < 2) {
  const adult = findAge('Adult') ?? ageCategories[0];
  const calf = findAge('Calf') ?? ageCategories[0];
  const cattle = findType('Cattle');
  const planA = await create('diet plan: Adult Cattle Maintenance', 'feed/diet-plans', {
    name: 'Adult Cattle Maintenance', animalTypeId: cattle?.id, ageCategoryId: adult?.id,
    notes: 'Seed plan',
    items: [
      { feedTypeId: feedTypes[0].id, quantityPerFeeding: 6 },
      { feedTypeId: (feedTypes[2] ?? feedTypes[1]).id, quantityPerFeeding: 2 },
    ],
  });
  const planB = await create('diet plan: Calf Starter', 'feed/diet-plans', {
    name: 'Calf Starter', animalTypeId: cattle?.id, ageCategoryId: calf?.id,
    notes: 'Seed plan',
    items: [{ feedTypeId: (feedTypes[1] ?? feedTypes[0]).id, quantityPerFeeding: 1.5 }],
  });

  const schedules = (await get(C('feed/schedules?page=1&pageSize=1'))).body?.totalCount ?? 0;
  if (schedules < 2 && planA) {
    await create('feeding schedule 06:00', 'feed/schedules', { dietPlanId: planA.id, timeOfDay: '06:00', label: 'Morning' });
    await create('feeding schedule 17:00', 'feed/schedules', { dietPlanId: planA.id, timeOfDay: '17:00', label: 'Evening' });
  }
  if (planB) {
    await create('feeding schedule 12:00', 'feed/schedules', { dietPlanId: planB.id, timeOfDay: '12:00', label: 'Midday' });
  }
}
await create('generate feeding tasks (today)', 'feed/tasks/generate', { date: new Date().toISOString() });

// ── 3. health ───────────────────────────────────────────────────────
console.log('\nHealth');
const medicineNames = byName(medicines, 'name');
for (const m of [
  { name: 'Ivermectin Injection 1%', description: 'Broad-spectrum dewormer', unit: 'ml', lowStockThreshold: 5, expiringSoonDays: 30 },
  { name: 'Multivitamin Injection', description: 'Vitamin B-complex support', unit: 'ml', lowStockThreshold: 5, expiringSoonDays: 45 },
  { name: 'Oxytetracycline 20% LA', description: 'Long-acting antibiotic', unit: 'ml', lowStockThreshold: 5, expiringSoonDays: 30 },
]) {
  if (!medicineNames.has(m.name.toLowerCase())) {
    const made = await create(`medicine: ${m.name}`, 'medicines', m);
    if (made) medicines.push(made);
  }
}
const currentMedicines = (await get(C('medicines?page=1&pageSize=100'))).body.items;
for (const m of currentMedicines) {
  const batches = (await get(C(`${'medicines'}/${m.id}/stock`))).body ?? [];
  if ((batches?.length ?? 0) < 1) {
    await create(`medicine stock: ${m.name} +60d`, `medicines/${m.id}/stock`, {
      batchNumber: `B-${Math.abs(hash(m.name)) % 9000 + 1000}-A`, quantity: 20, unitCost: 12,
      expiryDate: daysAgo(-60), supplier: 'AgroVet Supplies', dateReceived: daysAgo(10),
    });
    await create(`medicine stock: ${m.name} +180d`, `medicines/${m.id}/stock`, {
      batchNumber: `B-${Math.abs(hash(m.name)) % 9000 + 1000}-B`, quantity: 30, unitCost: 11,
      expiryDate: daysAgo(-180), supplier: 'AgroVet Supplies', dateReceived: daysAgo(5),
    });
  }
}

const vaccineNames = byName(vaccines, 'name');
const linkedMed = currentMedicines[0];
for (const v of [
  { name: 'FMD Vaccine', defaultDosage: '2 ml', notes: 'Foot-and-mouth disease', linkedMedicineId: linkedMed?.id },
  { name: 'HS Vaccine', defaultDosage: '3 ml', notes: 'Hemorrhagic septicemia' },
  { name: 'Dewormer', defaultDosage: '1 ml/50kg', notes: 'Routine deworming' },
]) {
  if (!vaccineNames.has(v.name.toLowerCase())) {
    await create(`vaccine: ${v.name}`, 'vaccines', v);
  }
}
const currentVaccines = (await get(C('vaccines?page=1&pageSize=100'))).body.items;

const vaccCount = (await get(C('vaccinations?page=1&pageSize=1'))).body.totalCount;
if (vaccCount < 5) {
  const v0 = currentVaccines[0];
  for (let i = 0; i < 4 && i < animals.length; i++) {
    await create(`vaccination: ${animals[i].tagNumber}`, 'vaccinations', {
      animalId: animals[i].id, vaccineTypeId: v0.id, dateGiven: daysAgo(30 + i),
      vetName: 'Dr. Ayesha Khan', batchNumber: `VX-100${i}`, quantityUsed: 1, cost: 1500,
      notes: 'Seed vaccination',
    });
  }
}

const vaccSchedules = (await get(C('vaccinations/schedule?page=1&pageSize=1'))).body.totalCount;
if (vaccSchedules === 0 && currentVaccines[0]) {
  await create('vaccination schedule: annual FMD', 'vaccinations/schedule', {
    vaccineTypeId: currentVaccines[0].id, animalTypeId: findType('Cattle')?.id, recurrenceDays: 180,
    isActive: true, notes: 'Seed schedule',
  });
}

const medRecords = (await get(C('medical-records?page=1&pageSize=1'))).body.totalCount;
if (medRecords === 0) {
  const cases = [
    { i: 0, symptoms: 'Reduced appetite and mild fever', diagnosis: 'Bovine respiratory disease', treatment: 'Antibiotics for 5 days', cost: 2200, status: 'Resolved' },
    { i: 1, symptoms: 'Lameness in left hind leg', diagnosis: 'Foot rot', treatment: 'Hoof trimming and dressing', cost: 1800, status: 'InProgress' },
    { i: 2, symptoms: 'Diarrhoea and dehydration', diagnosis: 'Calf scours', treatment: 'ORS and electrolytes', cost: 950, status: 'Open' },
    { i: 4, symptoms: 'Skin lesions', diagnosis: 'Dermatitis', treatment: 'Topical spray', cost: 600, status: 'Resolved' },
  ];
  for (const c of cases) {
    const a = animals[c.i];
    if (!a) continue;
    await create(`medical record: ${a.tagNumber}`, 'medical-records', {
      animalId: a.id, symptoms: c.symptoms, diagnosis: c.diagnosis, treatment: c.treatment,
      medicineUsed: linkedMed?.name, dosage: '2 ml', vetName: 'Dr. Ayesha Khan', cost: c.cost,
      dateRecorded: daysAgo(45 + c.i), status: c.status, notes: 'Seed medical record',
    });
  }
}

const weightSchedules = (await get(C('weight-schedules?page=1&pageSize=1'))).body.totalCount;
if (weightSchedules === 0) {
  await create('weight schedule: monthly cattle', 'weight-schedules', {
    animalTypeId: findType('Cattle')?.id, recurrenceDays: 30, isActive: true, notes: 'Seed weight schedule',
  });
}

// ── 4. weight records ───────────────────────────────────────────────
console.log('\nWeights');
const withoutWeight = animals.filter((a) => a.latestWeightKg == null).slice(0, 12);
const animalsWithWeight = animals.length - animals.filter((a) => a.latestWeightKg == null).length;
for (const a of (animalsWithWeight < 12 ? withoutWeight : [])) {
  const base = 180 + (Math.abs(hash(a.tagNumber)) % 220);
  await create(`weights: ${a.tagNumber}`, `animals/${a.id}/weights`, { weightKg: base, recordedAt: daysAgo(60), notes: 'Seed weight' });
  await create(`weights: ${a.tagNumber}`, `animals/${a.id}/weights`, { weightKg: base + 12, recordedAt: daysAgo(30), notes: 'Seed weight' });
  await create(`weights: ${a.tagNumber}`, `animals/${a.id}/weights`, { weightKg: base + 21, recordedAt: daysAgo(3), notes: 'Seed weight' });
}

// ── 5. breeding / gestation / births ────────────────────────────────
console.log('\nBreeding');
const breedingCount = (await get(C('breeding-records?page=1&pageSize=1'))).body.totalCount;
if (breedingCount < 2 && male && female && sexFemale) {
  // An active gestation (recent breeding, confirmed).
  const rec = await create('breeding record: active gestation', 'breeding-records', {
    sireId: male.id, damId: female.id, breedingDate: daysAgo(70), method: 'Natural',
    vetName: 'Dr. Ayesha Khan', notes: 'Seed breeding record',
  });
  if (rec) {
    await create('confirm pregnancy', 'gestation/confirm', { breedingRecordId: rec.id, confirmedDate: daysAgo(50) });
  }
  // A completed cycle that produces offspring.
  const dam2 = animals.filter((a) => /female/i.test(a.sexValue))[1] ?? female;
  const oldRec = await create('breeding record: birth cycle', 'breeding-records', {
    sireId: male2?.id ?? male.id, damId: dam2.id, breedingDate: daysAgo(310), method: 'Natural',
    vetName: 'Dr. Ayesha Khan', notes: 'Seed breeding record (birth cycle)',
  });
  if (oldRec) {
    const gest = await create('confirm pregnancy (birth cycle)', 'gestation/confirm', { breedingRecordId: oldRec.id, confirmedDate: daysAgo(280) });
    await create('birth record', 'births', {
      damId: dam2.id, gestationRecordId: gest?.id ?? null, breedingRecordId: oldRec.id,
      birthDate: daysAgo(28), vetName: 'Dr. Ayesha Khan', notes: 'Seed birth record',
      offspring: [
        { sexOptionId: sexMale?.id ?? sexOptions[0].id, outcome: 'Alive', birthWeightKg: 32, name: 'Seed Calf A' },
        { sexOptionId: sexFemale.id, outcome: 'Alive', birthWeightKg: 29, name: 'Seed Calf B' },
      ],
    });
  }
}

// ── 6. HR ───────────────────────────────────────────────────────────
console.log('\nHR');
for (const d of ['Dairy', 'Veterinary', 'Maintenance']) {
  if (!departments.some((x) => x.name.toLowerCase() === d.toLowerCase())) {
    const made = await create(`department: ${d}`, 'departments', { name: d });
    if (made) departments.push(made);
  }
}
for (const r of [['Farm Manager', 'Oversees operations'], ['Milker', 'Milking and herd care'], ['Vet Assistant', 'Supports the veterinarian']]) {
  if (!roles.some((x) => x.name.toLowerCase() === r[0].toLowerCase())) {
    const made = await create(`role: ${r[0]}`, 'employee-roles', { name: r[0], description: r[1] });
    if (made) roles.push(made);
  }
}
const dairy = departments.find((d) => /dairy/i.test(d.name));
const vet = departments.find((d) => /veterinary/i.test(d.name));
const maint = departments.find((d) => /maintenance/i.test(d.name));
const roleMgr = roles.find((r) => /manager/i.test(r.name));
const roleMilker = roles.find((r) => /milker/i.test(r.name));
const roleVet = roles.find((r) => /vet/i.test(r.name));

const wantedEmployees = [
  { firstName: 'Imran', lastName: 'Shah', phone: '0300-1112233', email: 'imran.shah@apk.example', departmentId: dairy?.id, employeeRoleId: roleMgr?.id, salaryType: 'Monthly', salaryRate: 45000, hireDate: daysAgo(400) },
  { firstName: 'Bilal', lastName: 'Ahmed', phone: '0301-2223344', email: 'bilal.ahmed@apk.example', departmentId: dairy?.id, employeeRoleId: roleMilker?.id, salaryType: 'Daily', salaryRate: 1500, hireDate: daysAgo(200) },
  { firstName: 'Sana', lastName: 'Yousaf', phone: '0302-3334455', email: 'sana.yousaf@apk.example', departmentId: vet?.id, employeeRoleId: roleVet?.id, salaryType: 'Monthly', salaryRate: 38000, hireDate: daysAgo(150) },
  { firstName: 'Adnan', lastName: 'Ali', phone: '0303-4445566', email: 'adnan.ali@apk.example', departmentId: maint?.id, employeeRoleId: roleMgr?.id, salaryType: 'Weekly', salaryRate: 9000, hireDate: daysAgo(100) },
];
const employeeNames = new Set(employees.map((e) => `${e.firstName} ${e.lastName}`.toLowerCase()));
for (const e of wantedEmployees) {
  if (!employeeNames.has(`${e.firstName} ${e.lastName}`.toLowerCase())) {
    await create(`employee: ${e.firstName} ${e.lastName}`, 'employees', { ...e, notes: 'Seed employee' });
  }
}
const allEmployees = (await get(C('employees?page=1&pageSize=100'))).body.items;

const attendanceCount = (await get(C('attendance?page=1&pageSize=1'))).body.totalCount;
if (attendanceCount === 0) {
  for (const emp of allEmployees.slice(0, 3)) {
    for (let d = 1; d <= 5; d++) {
      const day = new Date(Date.now() - d * 86400000);
      const status = d === 3 ? 'Late' : 'Present';
      await create(`attendance: ${emp.firstName} day-${d}`, 'attendance', {
        employeeId: emp.id, date: day.toISOString(), status,
        checkInAt: status === 'Late' ? null : new Date(day.setHours(8, 0, 0, 0)).toISOString(),
        checkOutAt: new Date(day.setHours(17, 0, 0, 0)).toISOString(),
        notes: 'Seed attendance',
      }, 'PUT');
    }
  }
}

const perfCount = (await get(C('performance-reviews?page=1&pageSize=1'))).body.totalCount;
if (perfCount === 0) {
  for (let i = 0; i < 3 && i < allEmployees.length; i++) {
    await create(`performance review: ${allEmployees[i].firstName}`, 'performance-reviews', {
      employeeId: allEmployees[i].id, rating: 4 - i, reviewDate: daysAgo(20 + i),
      periodStart: daysAgo(120), periodEnd: daysAgo(20),
      strengths: 'Reliable and thorough', areasForImprovement: 'Record keeping',
      comments: 'Seed review',
    });
  }
}

const payroll = (await get(C('employees/payroll-report'))).body;
if ((payroll?.paymentCount ?? 0) === 0) {
  for (const emp of allEmployees.slice(0, 3)) {
    await create(`salary payment: ${emp.firstName}`, `employees/${emp.id}/salary-payments`, {
      amount: emp.salaryRate || 10000, paymentDate: daysAgo(10), notes: 'Seed salary payment',
    });
  }
}

// ── 7. finance ──────────────────────────────────────────────────────
console.log('\nFinance');
const incomeCount = (await get(C('finance/income-records?page=1&pageSize=1'))).body.totalCount;
if (incomeCount < 3) {
  const milkCat = incomeCategories.find((c) => /milk|sale|product/i.test(c.name)) ?? incomeCategories[0];
  const pm = paymentMethods[0];
  const sales = [
    { amount: 52000, description: 'Milk sales — first half' },
    { amount: 48000, description: 'Milk sales — second half' },
    { amount: 165000, description: 'Culled animal sale' },
    { amount: 30000, description: 'Manure sales' },
    { amount: 90000, description: 'Surplus hay sale' },
  ];
  for (let i = 0; i < sales.length; i++) {
    await create(`income record: ${sales[i].description}`, 'finance/income-records', {
      incomeDate: daysAgo(i * 7 + 2), amount: sales[i].amount, incomeCategoryId: milkCat.id,
      paymentMethodId: pm.id, description: sales[i].description,
    });
  }
}

const expenses = (await get(C('finance/expenses?page=1&pageSize=1'))).body;
if (expenses.totalCount < 5) {
  const cat = expenseCategories[0];
  const pm = paymentMethods[0];
  const rows = [
    { amount: 65000, description: 'Feed purchase — Sibbi depot' },
    { amount: 12000, description: 'Veterinary services' },
    { amount: 8500, description: 'Electricity bill' },
    { amount: 22000, description: 'Labour wages advance' },
  ];
  for (let i = 0; i < rows.length; i++) {
    await create(`expense: ${rows[i].description}`, 'finance/expenses', {
      expenseDate: daysAgo(i * 9 + 3), amount: rows[i].amount, expenseCategoryId: cat.id,
      paymentMethodId: pm.id, description: rows[i].description,
    });
  }
}

// ── 8. inventory ────────────────────────────────────────────────────
console.log('\nInventory');
const wantedItems = [
  { name: 'Milking Gloves (L)', category: 'Consumables', unit: 'box', quantity: 40, reorderLevel: 10, unitCost: 450.75, location: 'Shed A' },
  { name: 'Dairy Feed Buckets', category: 'Equipment', unit: 'piece', quantity: 12, reorderLevel: 4, unitCost: 850, location: 'Store' },
  { name: 'Rope (nylon, 20m)', category: 'Equipment', unit: 'roll', quantity: 6, reorderLevel: 8, unitCost: 1200, location: 'Store' },
  { name: 'Wound Dressing', category: 'Medical', unit: 'box', quantity: 25, reorderLevel: 5, unitCost: 300, location: 'Vet Room' },
];
for (const it of wantedItems) {
  if (!inventoryItems.some((x) => x.name.toLowerCase() === it.name.toLowerCase())) {
    await create(`inventory item: ${it.name}`, 'inventory-items', it);
  }
}
const allItems = (await get(C('inventory-items?page=1&pageSize=100'))).body.items;

const wantedSuppliers = [
  { name: 'Sibbi Cattle Market', contactInfo: '081-555-1200', productsSupplied: 'Livestock, feed' },
  { name: 'AgroVet Supplies', contactInfo: '042-555-8890', productsSupplied: 'Medicine, vaccines' },
];
for (const s of wantedSuppliers) {
  if (!suppliers.some((x) => x.name.toLowerCase() === s.name.toLowerCase())) {
    await create(`supplier: ${s.name}`, 'inventory/suppliers', s);
  }
}
const allSuppliers = (await get(C('inventory/suppliers?page=1&pageSize=100'))).body.items;
const purchaseCount = (await get(C('inventory/supplier-purchases?page=1&pageSize=1'))).body.totalCount;
if (purchaseCount === 0 && allSuppliers[0] && allItems[0]) {
  await create('supplier purchase: gloves', 'inventory/supplier-purchases', {
    supplierId: allSuppliers[0].id, inventoryItemId: allItems[0].id, quantity: 40, totalCost: 18030,
    purchaseDate: daysAgo(15), expenseCategoryId: expenseCategories[0].id, paymentMethodId: paymentMethods[0].id,
    notes: 'Seed purchase',
  });
  if (allSuppliers[1] && allItems[3]) {
    await create('supplier purchase: dressing', 'inventory/supplier-purchases', {
      supplierId: allSuppliers[1].id, inventoryItemId: allItems[3].id, quantity: 25, totalCost: 7500,
      purchaseDate: daysAgo(8), expenseCategoryId: expenseCategories[0].id, paymentMethodId: paymentMethods[0].id,
      notes: 'Seed purchase',
    });
  }
}

const wantedCustomers = [
  { name: 'Karachi Retail', contactInfo: '021-555-4321' },
  { name: 'Local Dairy Shop', contactInfo: '0300-555-7788' },
];
for (const c of wantedCustomers) {
  if (!customers.some((x) => x.name.toLowerCase() === c.name.toLowerCase())) {
    await create(`customer: ${c.name}`, 'inventory/customers', c);
  }
}
const allCustomers = (await get(C('inventory/customers?page=1&pageSize=100'))).body.items;
const saleCount = (await get(C('inventory/customer-sales?page=1&pageSize=1'))).body.totalCount;
if (saleCount === 0 && allCustomers[0] && allItems[0]) {
  await create('customer sale: gloves', 'inventory/customer-sales', {
    customerId: allCustomers[0].id, inventoryItemId: allItems[0].id, quantity: 5, totalAmount: 3000,
    saleDate: daysAgo(6), incomeCategoryId: incomeCategories[0].id, paymentMethodId: paymentMethods[0].id,
    notes: 'Seed sale',
  });
  if (allCustomers[1] && allItems[1]) {
    await create('customer sale: buckets', 'inventory/customer-sales', {
      customerId: allCustomers[1].id, inventoryItemId: allItems[1].id, quantity: 2, totalAmount: 1900,
      saleDate: daysAgo(3), incomeCategoryId: incomeCategories[0].id, paymentMethodId: paymentMethods[0].id,
      notes: 'Seed sale',
    });
  }
}

// ── 9. tasks ────────────────────────────────────────────────────────
console.log('\nTasks');
const taskCount = (await get(C('tasks?page=1&pageSize=1'))).body.totalCount;
if (taskCount < 5) {
  const loc = findLocation('Shed B') ?? locations[0];
  const tasks = [
    { title: 'Fence repair Shed B', priority: 'High', dueDate: daysAgo(2), description: 'Broken rail on the north side' },
    { title: 'Vaccination drive — Cattle', priority: 'High', dueDate: daysAgo(-3), description: 'Annual FMD round' },
    { title: 'Restock mineral mix', priority: 'Medium', dueDate: daysAgo(-5), description: 'Below reorder level' },
    { title: 'Clean water troughs', priority: 'Low', dueDate: daysAgo(-1), description: 'Weekly cleaning' },
    { title: 'Review monthly payroll', priority: 'Medium', dueDate: daysAgo(-7), description: 'Approve salary payments' },
  ];
  for (const t of tasks) {
    await create(`task: ${t.title}`, 'tasks', {
      title: t.title, description: t.description, priority: t.priority, dueDate: t.dueDate,
      assignedEmployeeId: allEmployees[0]?.id, locationId: loc?.id,
    });
  }
}

console.log(DRY
  ? `\n${planned} record(s) would be created — dry run, nothing was written`
  : `\ncreated ${created} records`);
function hash(s) { let h = 0; for (const ch of s) h = (h * 31 + ch.charCodeAt(0)) | 0; return h; }
