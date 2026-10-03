// One-off audit: which unkeyed English sentences the backend still emits, and can a caller reach them?
//
// Run: node scripts/i18n-backlog-audit.mjs          human-readable
//      node scripts/i18n-backlog-audit.mjs --json   machine-readable
//
// Two questions, kept separate on purpose because they answer different things:
//   1. What is unkeyed?    a literal passed to Error.X(...) with no message key alongside it.
//   2. What is reachable?  whether the method containing it is in the closure over the
//                          controllers' action methods.
// An unkeyed sentence nobody can reach is a cosmetic problem. A reachable one is a user
// reading English inside an Arabic session.
//
// Two honest limits on the reachability half, stated here so the numbers are not
// over-trusted:
//   * Edges are matched by method NAME, not by receiver type, so `GetAsync` on two
//     different services is one node. That over-approximates the graph and therefore
//     over-approximates reachability — the safe direction, since it can only make a
//     sentence look more reachable than it is, never less.
//   * Dynamic dispatch through an interface is invisible for the same reason. Also
//     biased toward "reachable".
import { readdirSync, readFileSync } from "node:fs";
import { join, relative, sep } from "node:path";

const ROOT = process.cwd();
const PROJECTS = ["FMS.API", "FMS.Application", "FMS.Domain", "FMS.Infrastructure"];
const CODES = [
  "NotFound", "Validation", "Unauthorized", "Conflict",
  "Unexpected", "Unavailable", "Superseded",
];

function walk(dir, out) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === "obj" || entry.name === "bin") continue;
    const full = join(dir, entry.name);
    if (entry.isDirectory()) walk(full, out);
    else if (entry.name.endsWith(".cs")) out.push(full);
  }
  return out;
}

const files = PROJECTS.flatMap((p) => walk(join(ROOT, "src", p), []));
const controllerDir = join(ROOT, "src", "FMS.API", "Controllers");
const controllerFiles = readdirSync(controllerDir).filter((f) => f.endsWith(".cs"));

/**
 * One method declaration, used for BOTH the call graph and the enclosing lookup.
 *
 * They have to be the same pattern. An earlier version had two, and the enclosing one
 * could not match `public async Task<Result<X>> LoginAsync(` because its return-type
 * alternation stopped at the first `>`. Every method with a generic return type then
 * resolved to whatever method preceded it, and the audit reported `Invalid email or
 * password` — the login failure every user of this product has seen — as unreachable.
 *
 * The pattern deliberately does not require a line start: seven CustomerController actions
 * sit on the same line as their `[HttpGet]` attribute, and a line-anchored version
 * silently dropped all seven.
 */
const DECL = /(?:public|internal|private|protected)[ \t]+(?:static[ \t]+|async[ \t]+|override[ \t]+|virtual[ \t]+|sealed[ \t]+|partial[ \t]+|extern[ \t]+)*(?:[A-Za-z0-9_]+[ \t]*<[^<>()]*>|\w+[ \t]*\??|[A-Za-z0-9_.<>,\[\]?]+[ \t]+\*?)([A-Za-z0-9_]+)[ \t]*\(/g;

/** Blank out comments in place, so offsets and line numbers survive. */
function blankComments(source) {
  const out = source.split("");
  let i = 0;
  while (i < source.length) {
    if (source[i] === '"' || source[i] === "'") {
      const q = source[i];
      i++;
      while (i < source.length) {
        if (source[i] === "\\") { i += 2; continue; }
        if (source[i] === q) { i++; break; }
        i++;
      }
      continue;
    }
    if (source[i] === "/" && source[i + 1] === "/") {
      while (i < source.length && source[i] !== "\n") { out[i] = " "; i++; }
      continue;
    }
    if (source[i] === "/" && source[i + 1] === "*") {
      const end = source.indexOf("*/", i + 2);
      const stop = end === -1 ? source.length : end + 2;
      for (let j = i; j < stop; j++) if (out[j] !== "\n") out[j] = " ";
      i = stop;
      continue;
    }
    i++;
  }
  return out.join("");
}

/**
 * Split a call's argument list, starting at its open paren.
 *
 * Angle brackets count as nesting so `new Dictionary<string, object?>` stays one argument.
 * This is the same trap that made Mission 8's own guard read a comma as an argument
 * boundary, which is why the type-argument case is handled explicitly rather than by
 * hoping the literals never contain one.
 */
function readArgs(code, openIndex) {
  const args = [];
  let current = "";
  let depth = 0;
  let inTypeArgs = false;
  let i = openIndex;
  for (; i < code.length; i++) {
    const ch = code[i];

    // A string literal is opaque: its commas, braces and parens are prose, not structure.
    // Without this, `'{field.Label}' is mapped to column {column + 1}, but the file has…`
    // split at the comma after the closing brace and reported a truncated sentence — which
    // is the fourth time a parser has been fooled by text it should have skipped.
    if (ch === '"' || ch === "'") {
      const quote = ch;
      current += ch;
      i++;
      while (i < code.length) {
        if (code[i] === "\\") { current += code.slice(i, i + 2); i += 2; continue; }
        current += code[i];
        if (code[i] === quote) break;
        i++;
      }
      continue;
    }

    if (ch === "(" || ch === "[" || ch === "{") { depth++; if (depth === 1) continue; }
    else if (ch === "<") {
      // Only a type-argument list when an identifier sits on both sides: `count < limit`
      // is a comparison, `List<Foo>` is not.
      if (depth > 0 && /[A-Za-z0-9_>\]]\s*$/.test(current)
        && /^\s*[A-Za-z_][A-Za-z0-9_.]*(,|<|>)/.test(code.slice(i + 1))) {
        inTypeArgs = true;
        depth++;
        continue;
      }
    } else if (ch === ">" && inTypeArgs) { depth--; inTypeArgs = depth > 0; if (!inTypeArgs) continue; }
    else if (ch === ")" || ch === "]" || ch === "}") {
      depth--;
      if (depth === 0) { args.push(current.trim()); return { args, end: i }; }
    }
    if (depth === 1 && ch === "," && !inTypeArgs) { args.push(current.trim()); current = ""; continue; }
    current += ch;
  }
  return { args, end: i };
}

// --- the call graph, keyed by method name --------------------------------

/** Words that look like a call but are not, filtered out of the bare-call edges. */
const RESERVED = new Set([
  "if", "else", "for", "foreach", "while", "do", "switch", "case", "return", "yield",
  "await", "throw", "catch", "finally", "try", "lock", "using", "var", "new", "typeof",
  "nameof", "sizeof", "checked", "unchecked", "default", "in", "is", "as", "from", "select",
  "where", "group", "join", "into", "orderby", "let", "get", "set", "when", "async", "true",
  "false", "null", "this", "base", "value", "and", "or", "not", "public", "private",
  "protected", "internal", "static", "readonly", "sealed", "override", "virtual", "abstract",
  "partial", "class", "record", "struct", "interface", "enum", "namespace", "void",
]);

const methodCalls = new Map();
for (const file of files) {
  const code = blankComments(readFileSync(file, "utf8"));
  const marks = [...code.matchAll(DECL)];
  for (let i = 0; i < marks.length; i++) {
    const start = marks[i].index + marks[i][0].length;
    const end = i + 1 < marks.length ? marks[i + 1].index : code.length;
    const called = new Set();
    for (const call of code.slice(start, end).matchAll(/[A-Za-z0-9_]+[ \t]*\.[ \t]*([A-Za-z_][A-Za-z0-9_]*)[ \t]*\(/g)) {
      called.add(call[1]);
    }
    // Bare calls too: `await ValidateBatchAsync(farmId, items)` has no receiver, and
    // reading only the dotted form made every private helper look like a leaf. That
    // reported `No animals were supplied` — the answer to the bulk-animal preview every
    // importer sees — as unreachable.
    for (const call of code.slice(start, end).matchAll(/(?:^|[^A-Za-z0-9_.])([A-Za-z_][A-Za-z0-9_]*)[ \t]*\(/g)) {
      if (!RESERVED.has(call[1])) called.add(call[1]);
    }
    if (!methodCalls.has(marks[i][1])) methodCalls.set(marks[i][1], new Set());
    for (const c of called) methodCalls.get(marks[i][1]).add(c);
  }
}

const controllerActions = new Set();
for (const file of controllerFiles) {
  const code = blankComments(readFileSync(join(controllerDir, file), "utf8"));
  for (const action of code.matchAll(DECL)) controllerActions.add(action[1]);
}

const reachableMethods = new Set(controllerActions);
const queue = [...controllerActions];
while (queue.length) {
  const name = queue.pop();
  for (const callee of methodCalls.get(name) || []) {
    if (!reachableMethods.has(callee)) { reachableMethods.add(callee); queue.push(callee); }
  }
}

// --- the unkeyed sites ---------------------------------------------------

const KEY = /DomainMessageKeys\.|AlertMessageKeys\./;
const rows = [];

for (const file of files) {
  const code = blankComments(readFileSync(file, "utf8"));
  const rel = relative(ROOT, file).split(sep).join("/");
  // `Result<List<FeedingTaskDto>>.Validation(...)` has two levels of angle brackets, and
  // `(?:<[^<>]*>)?` stops at the first `>`. That silently skipped every site whose return
  // type was itself generic — 7 of FeedService's alone, which the guard test found and the
  // audit did not. The sixth time in this project that a pattern could not see a whole
  // category of the thing it checks, and the fifth that the guard caught and the audit missed.
  //
  // So the generic is consumed by counting depth rather than by a character class: `>` is a
  // closer only while a type-argument list is open, and a comparison is not one.
  const pattern = /\b(?:Error|Result)(?:\s*<(?![<>=!]))?/g;
  let match;
  while ((match = pattern.exec(code)) !== null) {
    // Consume the type-argument list by depth, then the factory name and its open paren.
    //
    // The depth loop runs only when a `<` was actually consumed. Starting it regardless made a
    // non-generic `Result.Conflict(` swallow text until it found a stray `>` somewhere later
    // in the file, which dropped the ConfigurationService composite from the audit entirely —
    // the failure mode of a counter initialised as if a bracket had been seen.
    let i = match.index + match[0].length;
    if (match[0].endsWith("<")) {
      let angle = 1;
      while (i < code.length && angle > 0) {
        if (code[i] === "<") angle++;
        else if (code[i] === ">") angle--;
        i++;
      }
    }
    const rest = /^(\s*\.\s*)([A-Za-z][A-Za-z0-9_]*)\s*\(/.exec(code.slice(i));
    if (!rest) continue;
    const factory = rest[2];
    const openParen = i + rest[0].length - 1;

    if (!CODES.includes(factory)) continue;
    const { args } = readArgs(code, openParen);
    // `$"..."` counts as a literal too. An earlier version required a bare `"` and so
    // skipped all 31 interpolated sites — which are exactly the argument-taking
    // sentences, the ones a reader is most likely to see with the wrong language.
    const literal = (a) => a.startsWith('"') || a.startsWith('$"');
    if (args.length === 0 || !literal(args[0])) continue;
    if (args.slice(1).some(literal) || args.slice(1).some((a) => KEY.test(a))) continue;

    const line = code.slice(0, match.index).split("\n").length;
    // Enclosing method, from the same DECL the graph was built with.
    const marks = [...code.slice(0, match.index).matchAll(DECL)];
    const method = marks.length ? marks[marks.length - 1][1] : "?";
    rows.push({
      file: rel,
      line,
      code: factory,
      message: args[0].replace(/^\$?"/, "").replace(/"$/, ""),
      method,
      reachable: reachableMethods.has(method),
    });
  }
}

const byCode = {};
for (const r of rows) byCode[r.code] = (byCode[r.code] || 0) + 1;
const reachable = rows.filter((r) => r.reachable);
const distinct = new Set(rows.map((r) => r.message)).size;
const distinctReachable = new Set(reachable.map((r) => r.message)).size;

if (process.argv.includes("--json")) {
  console.log(JSON.stringify({
    sites: rows.length,
    distinctSentences: distinct,
    reachableSites: reachable.length,
    distinctReachableSentences: distinctReachable,
    byCode,
    rows: rows.sort((a, b) => a.file.localeCompare(b.file) || a.line - b.line),
  }, null, 2));
} else {
  console.log(`unkeyed sites: ${rows.length} (${distinct} distinct sentences)`);
  console.log(`reachable from a controller: ${reachable.length} sites (${distinctReachable} distinct)`);
  console.log("by code:", JSON.stringify(byCode));
  console.log("");

  const byFile = {};
  for (const r of rows) (byFile[r.file] = byFile[r.file] || []).push(r);
  for (const file of Object.keys(byFile).sort()) {
    const list = byFile[file];
    const dead = list.filter((r) => !r.reachable).length;
    console.log(`${file}  (${list.length}${dead ? `, ${dead} unreachable` : ""})`);
    for (const r of list) {
      const msg = r.message.length > 80 ? r.message.slice(0, 77) + "..." : r.message;
      const flag = r.reachable ? "" : `   [unreachable: ${r.method}]`;
      console.log(`  ${String(r.line).padStart(5)}  ${r.code.padEnd(12)} ${msg}${flag}`);
    }
    console.log("");
  }
}
