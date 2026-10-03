// Does the English bundle render the exact sentence the server returns?
//
// The rule for every keyed message in this project is that English stays byte-identical:
// a reader on an English client, and every test asserting on the response body, must see
// exactly the bytes the API has always returned. A hand-written English string in the
// bundle is a claim, not evidence — this renders each key through i18next with the same
// arguments the server sends and compares it to the literal in the source.
//
// Run: node scripts/i18n-verify-english.mjs
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';

const ROOT = process.cwd();
const PROJECTS = ['FMS.API', 'FMS.Application', 'FMS.Domain', 'FMS.Infrastructure'];

// Read the server's own constants and their doc comments, which carry the exact English each
// key stands for. The comment is written as `“...”`, the way the file spells it. This used to be
// collected and never read, which meant the claim every constant makes — "the exact English this
// API has always answered" — was checked by nothing at all. It is checked below, against the
// bundle, so a doc comment that has drifted is a failure rather than a comment that lies quietly
// next to the key it describes.
const keysSource = readFileSync(
  join(ROOT, 'src/FMS.Application/Common/DomainMessageKeys.cs'), 'utf8');

const ENGLISH_BY_KEY = new Map();
{
  const pattern = /public const string (\w+)\s*=\s*"(validation\.[^"]+)";/g;
  let match;
  while ((match = pattern.exec(keysSource)) !== null) {
    const [, constant, key] = match;
    // The doc comment immediately above the constant carries the sentence.
    const before = keysSource.slice(0, match.index);
    // `/// <summary>`, not `/**` and not a bare `///`. The block comment marker was never present
    // in this file, so the first version found nothing at all and every constant was recorded
    // as having no documented English. The second version searched for the last `///`, which
    // works for a one-line summary and silently misses a wrapped one — because the last `///`
    // of a five-line comment is its `/// </summary>`, and the sentence is above that.
    // Anchoring on `<summary>` reads the whole comment either way.
    const docStart = before.lastIndexOf('/// <summary>');
    const doc = docStart === -1 ? '' : before.slice(docStart);
    // A wrapped comment repeats the `///` marker on every line, so the quoted sentence comes
    // back as `... is\n    /// {current} {unit}`. Flattening the markers here — rather than comparing
    // them away later — keeps the sentence itself intact for the reader of a failure.
    const flatDoc = doc.replace(/\s*\/\/\/\s*/g, ' ');
    const quoted = [...flatDoc.matchAll(/[“"]([^”"]+)[”"]/g)].map((m) => m[1]);
    if (quoted.length) ENGLISH_BY_KEY.set(key, quoted[quoted.length - 1]);
    else ENGLISH_BY_KEY.set(key, null), void constant;
  }
}

// The English bundle is the other half of the comparison: what a client actually renders.
const bundles = JSON.parse(
  readFileSync(join(ROOT, 'client/src/i18n/locales/en/validation.json'), 'utf8'));

/**
 * Resolves a dotted server key against the bundle.
 *
 * A server key starts with `validation`, which is the bundle's *filename* and not a level
 * inside it, so the first segment is dropped and the rest walked from the bundle root.
 */
const at = (path) =>
  path.split('.').slice(1).reduce((node, part) => (node ? node[part] : undefined), bundles);

/**
 * A sample value standing in for a server expression, so both sides of the comparison can
 * be rendered with the same text. `task.Status.ToString()` becomes `<task.Status>`, and
 * `FormatTime(timeOfDay)` becomes `<FormatTime(timeOfDay)>`: the shape is preserved and
 * the value is not, which is what makes the two sentences comparable at all.
 */
/**
 * Reduces a server expression to the value it denotes, so two spellings of one value
 * compare equal.
 *
 * `task.Status.ToString()` and `task.Status` are the same value, and `.ToString()` is how
 * an enum has to be written to survive JSON at all. `UnitAbbreviation(feedType.Unit)` is
 * not the same text as `feedType.Unit` — that difference is the deliberate one, since the
 * argument carries the raw enum so the client can abbreviate it in the reader's language —
 * so a wrapper call reduces to its inner expression.
 */
const normalise = (expression) =>
  expression.trim()
    .replace(/^UnitAbbreviation\((.*)\)$/, '$1')
    .replace(/^FormatTime\((.*)\)$/, '$1')
    // `string.Join(", ", xs)` in the English stands for the list `xs` sent as an array: the
    // server renders the English by joining, and sends the values for the client to join in
    // the reader's language. Same value, two spellings.
    .replace(/^string\.Join\("[^"]*",\s*(.*)\)$/, '$1')
    .replace(/\.ToString\(\)$/, '')
    // A list travels as an array, so the argument reads `xs.ToArray()` where the message's
    // hole read `xs`. Strip the call: it is how the list is put on the wire, not what it is.
    .replace(/\.ToArray\(\)$/, '')
    // An interpolation format suffix is formatting, not value: `{n:0.#}` is the same number
    // as `{n}`, and the server's suffix is reproduced in the English rather than sent.
    .replace(/:[0-9.,#]+$/, '')
    .trim();

const sample = (expression) => `<${normalise(expression)}>`;

/**
 * Renders a bundle sentence the way i18next would.
 *
 * The bundle's `{{name}}` and the server's `{expression}` are different vocabularies for
 * the same value: one is an argument *name*, the other the C# *expression* the server
 * interpolated. Comparing them as literal text is meaningless, so each server hole is
 * matched to the argument whose value expression it corresponds to, and both sides are
 * then rendered with the same sample text.
 */
function renderBundle(template, expressionsByName) {
  return template.replace(/\{\{(\w+)\}\}/g, (whole, name) =>
    expressionsByName[name] === undefined ? whole : sample(expressionsByName[name]));
}

function renderServer(literal, expressionsByName) {
  // A C# message may be written as adjacent literals joined with `+`, which is one sentence
  // but several literals. Join them before the holes are read, or the split leaves a stray
  // quote and a dangling `+` in the middle of the sentence being compared.
  const joined = literal.replace(/"\s*\+\s*"/g, '');
  return joined.replace(/\{([^{}]+)\}/g, (whole, expression) => {
    const trimmed = expression.trim();
    for (const [name, value] of Object.entries(expressionsByName)) {
      if (normalise(value) === normalise(trimmed)) return sample(trimmed);
      void name;
    }
    return whole;
  });
}

/**
 * Blanks comments in place, so offsets and line numbers survive.
 *
 * Same approach as `i18n-backlog-audit.mjs`: a sentence-shaped string inside a doc comment
 * is not a call site, and a commented-out call must not be mistaken for a live one.
 */
function blankComments(source) {
  const out = source.split('');
  let i = 0;
  while (i < source.length) {
    if (source[i] === '"' || source[i] === "'") {
      const q = source[i];
      i++;
      while (i < source.length) {
        if (source[i] === '\\') { i += 2; continue; }
        if (source[i] === q) { i++; break; }
        i++;
      }
      continue;
    }
    if (source[i] === '/' && source[i + 1] === '/') {
      while (i < source.length && source[i] !== '\n') { out[i] = ' '; i++; }
      continue;
    }
    if (source[i] === '/' && source[i + 1] === '*') {
      const end = source.indexOf('*/', i + 2);
      const stop = end === -1 ? source.length : end + 2;
      for (let j = i; j < stop; j++) if (out[j] !== '\n') out[j] = ' ';
      i = stop;
      continue;
    }
    i++;
  }
  return out.join('');
}

const SOURCES = PROJECTS.flatMap((project) => walk(join(ROOT, 'src', project)));

/**
 * Splits a call's argument list the way `i18n-backlog-audit.mjs` does, including the rule
 * that a string literal is opaque.
 *
 * <para>
 * Shared on purpose. A regex over the source cannot find these call sites reliably, because
 * a message is free to contain the very characters a regex is using as structure — and all
 * four sentences it missed do: `{string.Join(", ", unknown)}` contains `", "`, so a pattern
 * looking for the end of the literal stops inside the join separator. The audit already
 * solved this by walking the text; this file needs the same walk rather than a better regex,
 * because there is no regex that treats a quote inside a string as not a quote.
 * </para>
 */
function readArgs(code, openIndex) {
  const args = [];
  let current = '';
  let depth = 0;
  let inTypeArgs = false;
  let i = openIndex;
  for (; i < code.length; i++) {
    const ch = code[i];

    if (ch === '"' || ch === "'") {
      const quote = ch;
      const interpolated = i > 0 && (code[i - 1] === '$' || /[A-Za-z0-9_)\]]/.test(code[i - 1] ?? ' '));
      current += ch;
      i++;
      while (i < code.length) {
        // An escape.
        if (code[i] === '\\') { current += code.slice(i, i + 2); i += 2; continue; }

        // Inside an interpolated string, a `{...}` hole is code, so it can itself hold a
        // string — `{string.Join(", ", x)}` is the common one here. Its commas are separators
        // for the hole's expression and its quotes open and close that nested literal, so
        // they must not be mistaken for the end of the message. Reading the message as one
        // flat string split it in two at that comma, which is why three sites were reported
        // clean while never being checked.
        if (interpolated && code[i] === '{') {
          let depthHole = 1;
          current += code[i];
          i++;
          while (i < code.length && depthHole > 0) {
            if (code[i] === '"' || code[i] === "'") {
              const nested = code[i];
              current += code[i];
              i++;
              while (i < code.length && code[i] !== nested) {
                if (code[i] === '\\') { current += code.slice(i, i + 2); i += 2; continue; }
                current += code[i];
                i++;
              }
              if (i < code.length) { current += code[i]; i++; }
              continue;
            }
            if (code[i] === '{') depthHole++;
            if (code[i] === '}') depthHole--;
            current += code[i];
            i++;
          }
          continue;
        }

        current += code[i];
        if (code[i] === quote) break;
        i++;
      }
      continue;
    }

    // Comments are prose, not structure: a comma inside one must not split an argument.
    // The source is read raw here, so this walk has to recognise them itself.
    if (ch === '/' && code[i + 1] === '/') {
      const end = code.indexOf('\n', i);
      const stop = end === -1 ? code.length : end;
      current += ' '.repeat(stop - i);
      i = stop;
      continue;
    }
    if (ch === '/' && code[i + 1] === '*') {
      const end = code.indexOf('*/', i + 2);
      const stop = end === -1 ? code.length : end + 2;
      current += code.slice(i, stop).replace(/[^\n]/g, ' ');
      i = stop;
      continue;
    }

    if (ch === '(' || ch === '[' || ch === '{') { depth++; if (depth === 1) continue; }
    else if (ch === '<') {
      if (depth > 0 && /[A-Za-z0-9_>\]]\s*$/.test(current)
        && /^\s*[A-Za-z_][A-Za-z0-9_.]*(,|<|>)/.test(code.slice(i + 1))) {
        inTypeArgs = true;
        depth++;
        continue;
      }
    } else if (ch === '>' && inTypeArgs) { depth--; inTypeArgs = depth > 0; if (!inTypeArgs) continue; }
    else if (ch === ')' || ch === ']' || ch === '}') {
      depth--;
      if (depth === 0) { args.push(current.trim()); return { args, end: i }; }
    }
    if (depth === 1 && ch === ',' && !inTypeArgs) { args.push(current.trim()); current = ''; continue; }
    current += ch;
  }
  return { args, end: i };
}

function walk(dir, out = []) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === 'obj' || entry.name === 'bin') continue;
    const full = join(dir, entry.name);
    if (entry.isDirectory()) walk(full, out);
    else if (entry.name.endsWith('.cs')) out.push(full);
  }
  return out;
}

let checked = 0;
let mismatched = 0;
let missing = 0;
let calls = 0;
let undocumented = 0;
const missingEnglish = [];
const coveredConstants = new Set();

/**
 * The constants this slice added, so the script can report what it failed to reach.
 *
 * A verifier that silently checks 25 of 29 sites and prints "0 mismatched" is the same
 * failure this project has now hit three times: a check that reports success for a subset
 * of what it claims. Listing the expected constants makes an uncovered one a visible line
 * of output rather than an absence nobody notices.
 */
const SLICE_2B = [
  'AnimalTagExists', 'IdentificationValueExists', 'DepartmentNameExists', 'RoleNameExists',
  'FeedTypeNameExists', 'ExpenseCategoryNameExists', 'PaymentMethodNameExists',
  'IncomeCategoryNameExists', 'FeedTypeAlreadyInDietPlan', 'ScheduleTimeExists',
  'SystemStatusCannotBeDeleted', 'InsufficientStockToRemove', 'InsufficientStockToFeed',
  'InsufficientStockToExtend', 'InsufficientStockAvailable', 'FeedingTaskAlreadyStatus',
  'TaskNotPendingToStart', 'TaskNotOpenToComplete', 'TaskNotOpenToCancel', 'TaskNotReopenable',
  'AttendanceAlreadyMarked',
  'InvalidFarmRole', 'FileTooLarge', 'FileTypeNotAllowed', 'MedicineStockInsufficient',
  'VaccineStockInsufficient', 'ImportFieldBadColumn', 'ImportFieldsUnmapped',
  'ImportExtensionNotSupported', 'ImportTooManyCsvRows', 'ImportTooManyWorkbookRows',
  'ImportCsvUnreadable', 'ImportWorkbookUnreadable', 'UnknownAlertTypes',
  'PushDeviceLimitReached', 'FileStoreFailed', 'FileSignFailed',
];

/**
 * The constants the argument-free sweep added, for the same coverage-reporting reason.
 *
 * These sites pass a message and a key and nothing else, so the branch below used to skip
 * them with `args.length < 3` and report a clean run over a set it had never looked at —
 * the fourth time a check reported a category as absent rather than unknown.
 */
const ARGUMENT_FREE = [
  'AdjustmentQuantityZero', 'QuantityMustBePositive', 'UnitCostNegative', 'MovementDateFuture',
  'FeedTargetBothSpecified', 'FeedTargetNeitherSpecified', 'FedDateFuture', 'TimeOfDayFormat',
  'FeedingScheduleLabelTooLong', 'FromDateAfterToDate', 'BreedWrongAnimalTypeInDiet',
  'QuantityPerFeedingPositive', 'FeedingTaskDateInPast', 'FeedingTaskStatusInvalid',
  'ConsumptionPeriodInvalid', 'TagNumberRequired', 'NoAnimalsSupplied', 'AnimalAlreadyHasStatus',
  'IdentificationValueRequired', 'WeightMustBePositive', 'AnimalDocumentCategoryRequired',
  'DocumentFileMissing', 'ImageFileMissing', 'AnimalAlreadyInLocation', 'TransferDateFuture',
  'NoAnimalsSelected',
];

/**
 * The ten the medicine and vaccine sweep added, listed for the same coverage-reporting reason.
 *
 * `QuantityMustBePositive` is deliberately absent: it was added with the first sweep and is
 * reused here, so naming it twice would assert nothing.
 */
const MEDICINES_AND_VACCINES = [
  'MedicineNameRequired', 'MedicineUnitRequired', 'MedicineInUse', 'BatchNumberRequired',
  'StockBatchInUse', 'VaccineNameRequired', 'VaccineTypeInUseByVaccinations',
  'VaccineTypeInUseBySchedules', 'QuantityUsedPositive', 'RecurrenceIntervalPositive',
];

/**
 * The six the employee sweep added. `FromDateAfterToDate` is deliberately absent: FeedService
 * keyed that sentence first and EmployeeService reuses the constant, so naming it twice
 * would assert nothing.
 */
const EMPLOYEES = [
  'RoleDescriptionTooLong', 'SalaryAmountPositive', 'PaymentDateFuture', 'PeriodCoveredFuture',
  'PaymentReferenceTooLong', 'SalaryDeleteReasonRequired',
];

/**
 * The ten the breeding and inventory sweep added. `MovementDateFuture` is deliberately absent:
 * FeedService keyed that sentence first and InventoryService reuses the constant.
 */
const BREEDING_AND_INVENTORY = [
  'SireDamMustDiffer', 'PregnancyNotConfirmable', 'OffspringRequired', 'LineageRecordRequired',
  'SexOptionsInvalid', 'MovementTypeInvalid', 'ConsumptionQuantityPositive',
  'PurchaseQuantityPositive', 'TransferAdjustmentQuantityZero', 'MovementTypeFilterInvalid',
];

const EXPECTED = [
  ...SLICE_2B, ...ARGUMENT_FREE, ...MEDICINES_AND_VACCINES, ...EMPLOYEES,
  ...BREEDING_AND_INVENTORY,
];

for (const file of SOURCES) {
  const source = readFileSync(file, 'utf8').replace(/\r\n/g, '\n');

  // Call sites are found by walking the text, not by a pattern over it. Three regexes failed
  // here in a row, each silently checking a subset while printing a clean run: adjacency
  // missed the sites with a comment between the key and its arguments; a comment gap then
  // backtracked without bound and hung; and allowing escaped quotes still could not tell a
  // quote inside `{string.Join(", ", x)}` from the quote that ends the literal.
  //
  // So the source is read raw and `readArgs` does the skipping itself — it treats a string
  // as opaque and a comment as prose — which is the same walk the audit performs. Blanking
  // comments up front would have been simpler and is wrong: it walks strings too, and the
  // nested literal inside an interpolated message lost its quotes in the process.
  // The type argument list is matched to a depth of two because one is not enough:
  // `Result<List<FeedingTaskDto>>.Validation(` and `Result<PagedResult<FeedingTaskDto>>`
  // both defeated `<[^<>]*>`, and a pattern that cannot see a shape reports the sites in
  // that shape as absent rather than unknown.
  const pattern = /\b(?:Error|Result)\s*(?:<[^<>]*(?:<[^<>]*>[^<>]*)*>)?\s*\.\s*(?:NotFound|Validation|Unexpected|Conflict|Superseded)\s*\(/g;
  let match;
  while ((match = pattern.exec(source)) !== null) {
    calls++;
    const { args } = readArgs(source, match.index + match[0].length - 1);
    if (process.argv[2]) {
      console.log(`  ${match[0].trim()} -> ${args.length}: ${args.map((a) => JSON.stringify(a.slice(0, 44))).join(' | ')}`);
    }
    if (args.length < 2) continue;

    // Each argument arrives with its surrounding syntax still attached: the message keeps
    // its quotes, the key its `DomainMessageKeys.` prefix. Strip both before comparing.
    const literal = args[0].replace(/^\$?"/, '').replace(/"$/, '');
    const constant = args[1].replace(/["']/g, '').replace('DomainMessageKeys.', '').trim();
    if (!literal || !constant || !keysSource.includes(`string ${constant} `)) continue;
    const argBlock = args.slice(2).join(',');

    const constantLine = keysSource.match(new RegExp(`public const string ${constant}\\s*=\\s*"([^"]+)"`));
    if (!constantLine) { console.log(`  ?? no constant for ${constant}`); continue; }
    const key = constantLine[1];
    coveredConstants.add(constant);
    const template = at(key);
    if (template === undefined) { console.log(`  ?? ${key} is not in the en bundle`); missing++; continue; }

    // An argument-free message is the easy case and the easy case is where the checking
    // stops: there is no argument dictionary to line up, so both sides are the whole
    // sentence and equality is the whole test. The hole convention is still asserted —
    // a bundle that grew a `{{}}` here would render something the server never said.
    if (args.length < 3) {
      checked++;
      if (template !== literal || /\{\{\w+\}\}/.test(template)) {
        mismatched++;
        console.log(`  MISMATCH ${key}`);
        console.log(`    server: ${literal}`);
        console.log(`    bundle: ${template}`);
      }
      continue;
    }

    // The argument dictionary the server passes, as name -> the expression it evaluates.
    const expressionsByName = {};
    for (const [, name, expression] of argBlock.matchAll(/\["(\w+)"\]\s*=\s*([^,}\n]+)/g)) {
      expressionsByName[name] = expression.trim();
    }

    const renderedBundle = renderBundle(template, expressionsByName);
    const renderedServer = renderServer(literal, expressionsByName);
    checked++;
    if (renderedBundle !== renderedServer) {
      mismatched++;
      console.log(`  MISMATCH ${key}`);
      console.log(`    server: ${renderedServer}`);
      console.log(`    bundle: ${renderedBundle}`);
    }
  }
}

// The doc comment's claim, against the bundle's value. This is the same rule the call-site
// comparison enforces, applied to the sentence written beside the key rather than the sentence
// sent at runtime, so a key cannot be documented one way and rendered another.
//
// Three differences have to be reconciled first, or the check reports the same sentence many
// times over: a comment writes the C# hole `{detail}` and the bundle writes the i18next hole
// `{{detail}}`; a comment quotes with curly marks where the bundle uses straight ones; and a
// long sentence is wrapped across lines in the comment and kept on one in the bundle. None of
// the three is a disagreement about the sentence. Everything else is.
const asBundle = (english) => english
  .replace(/\s+/g, ' ')
  .trim()
  .replace(/[‘’]/g, "'")
  .replace(/\{(\w+)\}/g, '{{$1}}');

for (const [key, english] of ENGLISH_BY_KEY) {
  const template = at(key);
  if (template === undefined) continue;
  if (english === null) { undocumented++; missingEnglish.push(key); continue; }
  if (template !== asBundle(english)) {
    mismatched++;
    console.log(`  MISMATCH ${key} (doc comment vs bundle)`);
    console.log(`    comment: ${english}`);
    console.log(`    bundle:  ${template}`);
  }
  checked++;
}

const uncovered = EXPECTED.filter((name) => !coveredConstants.has(name));

// Why is a named constant not being reached? Prints the source around each use of it,
// after comment blanking, so the shape that defeats the pattern is visible.
if (process.argv[2]) {
  const wanted = process.argv[2];
  for (const file of SOURCES) {
    const code = blankComments(readFileSync(file, 'utf8').replace(/\r\n/g, '\n'));
    const at = code.indexOf(`DomainMessageKeys.${wanted}`);
    if (at === -1) continue;
    const line = code.slice(0, at).split('\n').length;
    console.log(`${file.split(/[\\/]/).slice(-3).join('/')}:${line}`);
    console.log(code.split('\n').slice(line - 3, line + 9).join('\n'));
    break;
  }
}

console.log(`call sites checked: ${checked}   mismatched: ${mismatched}   missing from bundle: ${missing}`);
console.log(`constants whose doc comment states their English: ${undocumented ? `${undocumented} do not` : 'all of them'}`);
if (missingEnglish.length) console.log(`  NO DOCUMENTED ENGLISH: ${missingEnglish.join(', ')}`);
console.log(`expected constants covered: ${EXPECTED.length - uncovered.length}/${EXPECTED.length}`);
if (uncovered.length) {
  console.log(`  NOT COVERED: ${uncovered.join(', ')}`);
  process.exitCode = 1;
}
if (mismatched || missing) process.exitCode = 1;
