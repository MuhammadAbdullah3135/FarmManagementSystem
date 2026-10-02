import i18n from './index';
import { formatDateLong } from './format';

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

const coerceArgs = (raw: Record<string, unknown> | null | undefined): Record<string, string | number> => {
  const args: Record<string, string | number> = {};
  for (const [name, value] of Object.entries(raw ?? {})) {
    if (typeof value === 'string' || typeof value === 'number') args[name] = formatArgValue(value);
  }
  return args;
};

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
  const rendered = i18n.t(split.path, { ns: split.ns, ...coerceArgs(args) });

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
