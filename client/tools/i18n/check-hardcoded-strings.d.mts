// Type surface for the checker so its test can import the .mjs directly.
export interface HardcodedStringViolation {
  rel: string;
  kind: 'jsx-text' | 'attribute' | 'toast' | 'property' | 'template';
  value: string;
}

/** One reviewed baseline entry: the string id plus the reason it was accepted. */
export interface BaselineEntry {
  id: string;
  reason: string;
}

export declare const USER_ATTRS: Set<string>;
export declare const TOAST_KINDS: Set<string>;

/** Placeholder reason written for an entry that has not been given one yet. */
export declare const DEFAULT_BASELINE_REASON: string;

/**
 * The id set a baseline file covers. Tolerates both schemas: bare strings from
 * the pre-reason format and { id, reason } entries from the current one.
 */
/** A baseline entry as stored; unknown extra fields are tolerated when reading. */
export type AnyBaselineEntry = BaselineEntry | (string & Record<string, unknown>);

export declare function baselineIdsFrom(
  file: { strings?: (string | BaselineEntry | Record<string, unknown>)[] } | null | undefined,
): Set<string>;

/**
 * Sorted { id, reason } entries for the current scan: an id present in
 * `previousStrings` keeps its reason (bare strings take the default), a new id
 * lands with DEFAULT_BASELINE_REASON for a reviewer to replace.
 */
export declare function buildBaselineEntries(
  previousStrings: (string | BaselineEntry | Record<string, unknown>)[] | null | undefined,
  currentIds: Iterable<string>,
): BaselineEntry[];

export declare function scanSource(rel: string, code: string): HardcodedStringViolation[];
export declare function collectViolations(root?: string): HardcodedStringViolation[];
export declare function newStrings(
  violations: HardcodedStringViolation[],
  baselineIds: Set<string> | string[],
): string[];
