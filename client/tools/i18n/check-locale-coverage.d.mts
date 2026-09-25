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

export declare function flatten(value: unknown, prefix?: string): Record<string, string>;
export declare function readLocaleDirs(dir?: string): string[];
export declare function loadLocaleBundles(locale: string, dir?: string): Record<string, Record<string, string>>;
export declare function placeholders(value: unknown): string[];
export declare function compareBundles(
  base: Record<string, Record<string, string>>,
  other: Record<string, Record<string, string>>,
): BundleComparison;
export declare function readAllowlist(file?: string): { byKey: Map<string, AllowlistEntry>; entries: AllowlistEntry[] };
export declare function evaluate(
  report: BundleComparison,
  allowlist: { byKey: Map<string, AllowlistEntry>; entries: AllowlistEntry[] },
  options?: { maxEntries?: number },
): { failures: LocaleIssue[]; allowed: number };
export declare function runCheck(options?: { dir?: string }): {
  base: Record<string, Record<string, string>>;
  locales: LocaleReport[];
  total: number;
};
