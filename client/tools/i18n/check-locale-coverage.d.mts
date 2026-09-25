// Type surface for the locale coverage checker so tests can import the .mjs directly.
export interface LocaleIssue {
  kind:
    | 'missing-namespace'
    | 'extra-namespace'
    | 'missing-key'
    | 'extra-key'
    | 'empty-value'
    | 'placeholder-drift'
    | 'line-break-drift'
    | 'plural-forms-without-other'
    | 'missing-plural-form'
    | 'untranslated'
    | 'allowlist-reason-missing'
    | 'allowlist-too-large'
    | 'allowlist-stale';
  key: string;
  detail: string;
}

export interface AllowlistEntry {
  key: string;
  value: string;
  reason: string;
  /** Restricts the entry to the locales it was reviewed for. Absent means every locale. */
  locales?: string[];
}

export interface BundleComparison {
  checked: number;
  issues: LocaleIssue[];
  identical: string[];
}

export interface LocaleReport {
  locale: string;
  checked: number;
  identical: number;
  allowed: number;
  failures: LocaleIssue[];
}

export declare const CLIENT: string;
export declare const LOCALES_DIR: string;
export declare const BASE_LOCALE: string;
export declare const ALLOWLIST_FILE: string;
export declare const MAX_ALLOWLIST_ENTRIES: number;
export declare const MIN_ALLOWLIST_REASON: number;
export declare const PLURAL_SUFFIXES: string[];
export declare const logicalKey: (key: string) => string;
export declare const isPluralVariant: (key: string) => boolean;
export declare function pluralCategories(locale: string): string[];

export declare function flatten(value: unknown, prefix?: string): Record<string, string>;
export declare function readLocaleDirs(dir?: string): string[];
export declare function loadLocaleBundles(locale: string, dir?: string): Record<string, Record<string, string>>;
export declare function placeholders(value: unknown): string[];
export declare function compareBundles(
  base: Record<string, Record<string, string>>,
  other: Record<string, Record<string, string>>,
  options?: { locale?: string },
): BundleComparison;
export declare function readAllowlist(file?: string): { entries: AllowlistEntry[] };
export declare function evaluate(
  report: BundleComparison,
  allowlist: { entries: AllowlistEntry[] },
  options?: { maxEntries?: number; locale?: string },
): { failures: LocaleIssue[]; allowed: number };
export declare function runCheck(options?: { dir?: string }): {
  base: Record<string, Record<string, string>>;
  locales: LocaleReport[];
  total: number;
};
