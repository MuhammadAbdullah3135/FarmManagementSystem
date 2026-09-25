import type { TFunction } from 'i18next';

/**
 * The fixed vocabularies, translated at the point of display.
 *
 * Everything here is *a value the server stores* (`OverdueVaccination`, `Forage`,
 * `FarmManager`, `pending`) paired with the key that names it in the reader's language.
 * Those two things must never be confused: a feed type's category is stored as `Forage`
 * and stays `Forage` in the database and in every API call, whatever language the person
 * filling the form was reading. Translating the stored value would corrupt data and break
 * every filter that compares it.
 *
 * So the tables below are deliberately value → **key**, not value → text: there is no
 * English string here to forget to translate, and the same table serves a dropdown option,
 * a table cell and an aria-label.
 *
 * Each vocabulary lives in the namespace that owns it, and the fallback is always the raw
 * value — an alert type from a newer server shows as `SomeNewAlert` rather than as a blank
 * or as a missing key.
 */

/** Resolves a value's key, falling back to the value itself when there is nothing to say. */
const label = (t: TFunction, key: string | undefined, fallback: string): string =>
  key ? t(key) : fallback;

/**
 * Alert types, as stored on a notification. Mirrors the server's `AlertTypes` constants.
 * The words match the sidebar and the report pages that cover the same conditions.
 */
const ALERT_TYPE_KEYS: Record<string, string> = {
  OverdueVaccination: 'notifications:alertTypeOverdueVaccination',
  OverdueWeightCheck: 'notifications:alertTypeOverdueWeightCheck',
  Medicine: 'notifications:alertTypeMedicine',
  OverdueTask: 'notifications:alertTypeOverdueTask',
  DueBirth: 'notifications:alertTypeDueBirth',
  LowInventory: 'notifications:alertTypeLowInventory',
};

export const alertTypeLabel = (t: TFunction, alertType: string): string =>
  label(t, ALERT_TYPE_KEYS[alertType], alertType);

/**
 * Severity, as stored on an alert. The server owns the three levels; the colour for each
 * stays in `api/notifications.ts` next to the tags that use it.
 */
const SEVERITY_KEYS: Record<string, string> = {
  Critical: 'notifications:severityCritical',
  Warning: 'notifications:severityWarning',
  Info: 'notifications:severityInfo',
};

export const severityLabel = (t: TFunction, severity: string): string =>
  label(t, SEVERITY_KEYS[severity], severity);

/** The device-side write queue's states, as stored on an outbox item. */
const OUTBOX_STATUS_KEYS: Record<string, string> = {
  pending: 'offline:statusPending',
  applied: 'offline:statusSynced',
  quarantined: 'offline:statusRefused',
  dismissed: 'offline:statusDismissed',
};

export const outboxStatusKey = (status: string): string | undefined => OUTBOX_STATUS_KEYS[status];

export const outboxStatusLabel = (t: TFunction, status: string): string =>
  label(t, OUTBOX_STATUS_KEYS[status], status);

/**
 * Feed categories and units.
 *
 * These are the one vocabulary the *user* extends: a farm can create a feed type in any
 * category, but only these six are offered as choices and only these seven units. Whatever
 * is stored is shown as-is; the presets are shown translated.
 */
const FEED_CATEGORY_KEYS: Record<string, string> = {
  Forage: 'feed:categoryForage',
  Concentrate: 'feed:categoryConcentrate',
  Mineral: 'feed:categoryMineral',
  Supplement: 'feed:categorySupplement',
  Additive: 'feed:categoryAdditive',
  Other: 'feed:categoryOther',
};

const FEED_UNIT_KEYS: Record<string, string> = {
  Kilogram: 'feed:unitKilogram',
  Gram: 'feed:unitGram',
  Ton: 'feed:unitTon',
  Liter: 'feed:unitLiter',
  Bale: 'feed:unitBale',
  Bag: 'feed:unitBag',
  Other: 'feed:unitOther',
};

export const feedCategoryLabel = (t: TFunction, category: string): string =>
  label(t, FEED_CATEGORY_KEYS[category], category);

export const feedUnitLabel = (t: TFunction, unit: string): string =>
  label(t, FEED_UNIT_KEYS[unit], unit);

/** `{ value, label }` pairs for a `Select`, with the value left exactly as the API wants it. */
export const labeledOptions = (
  values: readonly string[],
  toLabel: (t: TFunction, value: string) => string,
  t: TFunction,
): { value: string; label: string }[] => values.map((value) => ({ value, label: toLabel(t, value) }));

/**
 * Farm roles, as stored on a membership and in the role claims.
 *
 * Two cases are worth naming: `Accountant` and `Viewer` only ever *read*, and the role
 * names are also what `MODULE_ROLES` in `utils/permissions.ts` compares against — that
 * comparison must keep using the stored value, never the label.
 */
const FARM_ROLE_KEYS: Record<string, string> = {
  SystemOwner: 'common:roleSystemOwner',
  FarmManager: 'common:roleFarmManager',
  Veterinarian: 'common:roleVeterinarian',
  Employee: 'common:roleEmployee',
  Accountant: 'common:roleAccountant',
  Viewer: 'common:roleViewer',
};

export const farmRoleLabel = (t: TFunction, role: string): string =>
  label(t, FARM_ROLE_KEYS[role], role);
