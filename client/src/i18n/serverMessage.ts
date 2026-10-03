import i18n from './index';
import { formatDateLong, formatNumber } from './format';
import { enumLabelOf, type EnumName } from './enumOptions';
import { feedUnitLabel } from './vocabulary';

/**
 * Rendering server-generated messages in the client's language.
 *
 * The server sends a message key next to the English text it has always sent: an
 * `X-Message-Key` header on a plain error response, or `errorKey`/`errorArgs` in a
 * structured body (the import preview's per-row problems carry them as fields). The client
 * prefers the key and falls back to the English text whenever the key is unknown — an older
 * server, a key this build has not extracted yet, or a genuine typo.
 *
 * A key that takes arguments gets them from a second header, `X-Message-Args`, carrying
 * percent-encoded JSON; a body-carried key gets them from the body. Both feed the same
 * renderer, so a sentence reads identically whichever path it arrived on.
 *
 * This is why the server never needs to know the user's locale: it states *what* went
 * wrong, and the renderer decides *how* to say it.
 */
/**
 * Splits a server key into its namespace and its path.
 *
 * A server key is always written `namespace.path.to.key` (e.g.
 * `validation.employee.firstNameMaxLength`) because the server has no business knowing
 * about i18next's `:` namespace separator. The client reads the first segment as the
 * namespace and the rest as the key — which is also why a key whose first segment is not a
 * real namespace resolves to nothing and falls back to English rather than rendering oddly.
 */
const splitServerKey = (key: string): { ns: string; path: string } | null => {
  const dot = key.indexOf('.');
  if (dot <= 0 || dot === key.length - 1) return null;
  return { ns: key.slice(0, dot), path: key.slice(dot + 1) };
};

/** A date the server sent: ISO 8601, date-only or with a time. */
const ISO_DATE = /^\d{4}-\d{2}-\d{2}(?:[T ].*)?$/;

/**
 * Formats one argument for the reader.
 *
 * Arguments arrive as raw values on purpose — the server states *what* the message is
 * about without deciding how a date is written. A date therefore gets formatted here, in
 * the reader's language and with a month name (`Aug 3, 2026` / `3 ago 2026`), which is what
 * the server used to do for every reader. Numbers pass through untouched: i18next
 * interpolates them as they are.
 */
const formatArgValue = (value: string | number): string | number =>
  typeof value === 'string' && ISO_DATE.test(value) ? formatDateLong(value) : value;

/** How one argument of one key should be rendered. */
type ArgKind =
  /** A value the reader typed: a name, a label, a file extension. Shown as it arrived. */
  | 'text'
  /**
   * A stored value with an enum's vocabulary, named: `enum:taskStatus`.
   *
   * The enum is part of the kind rather than a separate table because the argument name
   * `status` is genuinely ambiguous — a farm task's, a feeding task's and an attendance day's
   * all arrive under it and disagree about some values — so what an argument *is* has to be
   * said per key, and one table says it once.
   */
  | `enum:${EnumName}`
  /** A stored value with a vocabulary that is not an enum: a feed unit. */
  | 'feedUnit'
  /** A quantity, grouped and decimal-marked in the reader's language. */
  | 'quantity'
  /** A size in megabytes, at most one decimal — the server's `:0.#` rule, kept here. */
  | 'megabytes'
  /** A list of values, joined the way the reader's language joins a list. */
  | 'list'
  /**
   * Text from an exception, which is not copy and cannot be translated.
   *
   * Kept as its own kind rather than folded into `text` so the reason stays visible: the
   * frame of these sentences is translated and the diagnostic inside them is not, which is
   * the most a system error can honestly offer a reader who does not read English.
   */
  | 'diagnostic';

/**
 * What every argument of every keyed sentence is, declared once.
 *
 * Slice 2b began this as two narrow sets — the keys with a `status` enum, and the keys with
 * a `unit` — and immediately ran into the ceiling of that shape: adding the file, import and
 * notification sentences meant asking which *key* a new argument belonged to, which is a
 * question about arguments wearing a costume. Keying by argument name instead is wrong for
 * `status` and right for everything else, so both are declared: the table below is keyed by
 * message key, and inside it by argument name.
 *
 * A key absent from this table renders every argument as it arrived, which is the same
 * honest fallback as an unknown key. The guard test asserts that every placeholder in a
 * server-keyed sentence appears here, so a new argument cannot slip through undeclared.
 */
const ARG_KINDS: Record<string, Record<string, ArgKind>> = {
  // ── conflict family (slice 2b) ──
  'validation.conflict.taskNotPendingToStart': { status: 'enum:taskStatus' },
  'validation.conflict.taskNotOpenToComplete': { status: 'enum:taskStatus' },
  'validation.conflict.taskNotOpenToCancel': { status: 'enum:taskStatus' },
  'validation.conflict.taskNotReopenable': { status: 'enum:taskStatus' },
  'validation.conflict.feedingTaskAlreadyStatus': { status: 'enum:feedingTaskStatus' },
  'validation.supersede.attendanceAlreadyMarked': { status: 'enum:attendanceStatus' },
  'validation.conflict.insufficientStockToRemove': { current: 'quantity', unit: 'feedUnit', attempted: 'quantity' },
  'validation.conflict.insufficientStockToFeed': { current: 'quantity', unit: 'feedUnit', attempted: 'quantity' },
  'validation.conflict.insufficientStockToExtend': { current: 'quantity', unit: 'feedUnit', attempted: 'quantity' },
  'validation.conflict.insufficientStockAvailable': { available: 'quantity', unit: 'feedUnit' },

  // ── names the reader typed: shown as they arrived, never translated ──
  'validation.conflict.animalTagExists': { tag: 'text' },
  'validation.conflict.identificationValueExists': { value: 'text' },
  'validation.conflict.departmentNameExists': { name: 'text' },
  'validation.conflict.roleNameExists': { name: 'text' },
  'validation.conflict.feedTypeNameExists': { name: 'text' },
  'validation.conflict.expenseCategoryNameExists': { name: 'text' },
  'validation.conflict.paymentMethodNameExists': { name: 'text' },
  'validation.conflict.incomeCategoryNameExists': { name: 'text' },
  'validation.conflict.feedTypeAlreadyInDietPlan': { name: 'text' },
  'validation.conflict.systemStatusCannotBeDeleted': { name: 'text' },
  'validation.conflict.scheduleTimeExists': { time: 'text' },
  'validation.validation.invalidFarmRole': { role: 'text' },

  // ── health: a medicine's unit is free text the farm typed, not the feed-unit enum ──
  'validation.validation.medicineStockInsufficient': { available: 'quantity', unit: 'text', requested: 'quantity' },
  'validation.validation.vaccineStockInsufficient': { available: 'quantity', required: 'quantity' },

  // ── files ──
  'validation.validation.fileTooLarge': { max: 'megabytes' },
  'validation.validation.fileTypeNotAllowed': { extension: 'text', allowed: 'list' },

  // ── import: field labels are the server's own English and have no vocabulary yet ──
  'validation.validation.importFieldBadColumn': { field: 'text', column: 'quantity', count: 'quantity' },
  'validation.validation.importFieldsUnmapped': { fields: 'list' },
  'validation.validation.importExtensionNotSupported': { extension: 'text' },
  'validation.validation.importTooManyCsvRows': { rows: 'quantity' },
  'validation.validation.importTooManyWorkbookRows': { rows: 'quantity' },
  'validation.validation.importCsvUnreadable': { detail: 'diagnostic' },
  'validation.validation.importWorkbookUnreadable': { detail: 'diagnostic' },

  // ── notifications ──
  'validation.validation.unknownAlertTypes': { types: 'list' },
  'validation.validation.pushDeviceLimitReached': { count: 'quantity' },

  // ── storage: an S3 fault, reported with the service's own diagnostic ──
  'validation.validation.fileStoreFailed': { detail: 'diagnostic' },
  'validation.validation.fileSignFailed': { detail: 'diagnostic' },

  // ── unauthorized: the action is a phrase, resolved from a vocabulary ──
  // `enum:farmOwnerAction` rather than a verb on its own. A bare stem would ask each language
  // to conjugate English at the point of insertion, which Arabic cannot do; the vocabulary
  // entry is a phrase the language has already written, so the frame only has to place it.
  'validation.unauthorized.farmOwnerOrManagerOnly': { action: 'enum:farmOwnerAction' },
  'validation.unauthorized.farmOwnerOnly': { action: 'enum:farmOwnerAction' },
};

/** The enum named by an `enum:<name>` kind. */
const enumFor = (kind: ArgKind): EnumName | null =>
  kind.startsWith('enum:') ? (kind.slice('enum:'.length) as EnumName) : null;

/**
 * Joins a list the way the reader's language joins one.
 *
 * `Intl.ListFormat` rather than a translated separator: Spanish writes "a, b y c" and
 * Arabic "أ، ب، و ج", and the conjunction, the commas and the spacing between them are all
 * the language's business. A server that joined with `", "` would have frozen English's
 * punctuation into every reader's sentence, which is the same mistake as freezing a number's
 * decimal mark.
 */
const joinList = (values: readonly unknown[]): string => {
  const locale = i18n.resolvedLanguage ?? 'en';
  try {
    return new Intl.ListFormat(locale, { style: 'long', type: 'conjunction' })
      .format(values.map(String));
  } catch {
    // A locale Intl does not know still has to render something; a plain comma join is the
    // least surprising thing, and it is what the server used to send.
    return values.map(String).join(', ');
  }
};

/**
 * Renders one argument of one key.
 *
 * <para>
 * Numbers go through <c>formatNumber</c> so a Spanish reader sees <c>1.234,5</c> and an
 * Arabic reader sees Arabic-Indic digits, rather than every language reading the server's
 * English formatting. The server sends the number unformatted precisely so this can happen
 * here; a value that is not a number, or an argument with no declared kind, is left alone.
 * </para>
 *
 * <para>
 * An argument with no declared kind falls back to its raw value. That is the same honest
 * fallback as an unknown key: a correct translation is not available, so the server's text
 * stands rather than a wrong one being invented. The guard test below is what keeps that
 * from being a silent accident.
 * </para>
 */
const renderArg = (key: string, name: string, value: unknown): string | number => {
  const kind = ARG_KINDS[key]?.[name];

  const vocabulary = kind !== undefined ? enumFor(kind) : null;
  if (vocabulary !== null) return enumLabelOf(vocabulary, value as string);

  if (kind === 'feedUnit') return feedUnitLabel(i18n.t.bind(i18n), value as string);
  if (kind === 'quantity' && typeof value === 'number') return formatNumber(value);
  if (kind === 'megabytes' && typeof value === 'number') {
    // At most one decimal, because that is what the server's `0.#` format produced and
    // the English has to keep rendering identically.
    return formatNumber(value, undefined, { maximumFractionDigits: 1 });
  }
  if (kind === 'list') {
    return joinList(Array.isArray(value) ? value : [value]);
  }

  return typeof value === 'string' || typeof value === 'number' ? formatArgValue(value) : String(value);
};

const coerceArgs = (key: string, raw: Record<string, unknown> | null | undefined): Record<string, string | number> => {
  const args: Record<string, string | number> = {};
  for (const [name, value] of Object.entries(raw ?? {})) {
    // A list argument arrives as an array, so `coerceArgs` keeps arrays where a string or
    // number used to be the only shape it carried through.
    if (typeof value === 'string' || typeof value === 'number' || Array.isArray(value)) {
      args[name] = renderArg(key, name, value);
    }
  }
  return args;
};

/** The argument names a key declares, for the guard test that reads this table. */
export const argKindsFor = (key: string): Record<string, ArgKind> | undefined => ARG_KINDS[key];

/** True when this build can render the key (it exists in the English resources). */
export const canRenderMessageKey = (key: string | null | undefined): key is string => {
  if (typeof key !== 'string' || !key.trim()) return false;
  const split = splitServerKey(key);
  return split !== null && i18n.exists(split.path, { ns: split.ns });
};

/** The key's text for the active language, or `fallback` when the key is unknown. */
export const renderKeyedMessage = (
  key: string | null | undefined,
  args: Record<string, unknown> | null | undefined,
  fallback: string,
): string => {
  if (!canRenderMessageKey(key)) return fallback;
  const split = splitServerKey(key)!;
  const rendered = i18n.t(split.path, { ns: split.ns, ...coerceArgs(key, args) });

  // Never hand a reader a raw placeholder. A sentence that needs an argument we did not get
  // renders as "First name cannot exceed {{max}} characters", which is worse than the
  // English we were about to fall back to: it is broken *and* it is in the reader's
  // language, so it looks deliberate. i18next leaves an unsupplied placeholder in place by
  // default, so this has to be checked rather than assumed. Nothing in any bundle contains
  // `{{` except a real placeholder, so the test cannot misfire on ordinary copy.
  //
  // Checking the rendered text rather than the key means it covers the case nobody
  // anticipated: a key that grows a placeholder later, or arguments dropped by some future
  // path, degrades to the server's English instead of to a brace.
  return rendered.includes('{{') ? fallback : rendered;
};

/**
 * Decodes the `X-Message-Args` header: percent-encoded JSON, which the server writes with
 * `Uri.EscapeDataString` so no interpolated value can put a character in a header that a
 * parser would object to.
 *
 * Returns null for anything it cannot read — absent, blank, not valid percent-encoding, not
 * valid JSON, or not a plain object. A header the client cannot parse is a missing argument,
 * not a thrown error: the caller's fallback is a whole correct sentence, and refusing to
 * render because the arguments were malformed would trade a full stop for a blank screen.
 */
const parseArgsHeader = (raw: unknown): Record<string, unknown> | null => {
  if (typeof raw !== 'string' || !raw.trim()) return null;
  try {
    const parsed: unknown = JSON.parse(decodeURIComponent(raw));
    if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) return null;
    return parsed as Record<string, unknown>;
  } catch {
    return null;
  }
};

/** The key a response carries, from its header or its body, or null when it carries none. */
export const messageKeyFromResponse = (
  headers: Record<string, unknown> | undefined,
  data: unknown,
): { key: string; args: Record<string, unknown> | null } | null => {
  const headerKey = headers?.['x-message-key'];
  if (typeof headerKey === 'string' && headerKey.trim()) {
    return { key: headerKey, args: parseArgsHeader(headers?.['x-message-args']) };
  }

  if (data && typeof data === 'object') {
    const body = data as Record<string, unknown>;
    const key = body.errorKey ?? body.messageKey;
    if (typeof key === 'string' && key.trim()) {
      const args = (body.errorArgs ?? body.messageArgs ?? null) as Record<string, unknown> | null;
      return { key, args };
    }
  }

  return null;
};
