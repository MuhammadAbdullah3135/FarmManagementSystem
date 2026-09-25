// Type surface for the direction checker so its test can import the .mjs directly.
export interface PhysicalDirectionRule {
  rule: string;
  re: RegExp;
  target: string;
}

export interface PhysicalDirectionViolation {
  rel: string;
  line: number;
  rule: string;
  value: string;
  target?: string;
}

export interface PhysicalDirectionFailure {
  kind: 'physical-direction' | 'allowlist-stale' | 'allowlist-reason-missing';
  id: string;
  detail: string;
  value?: string;
}

export interface PhysicalDirectionAllowlistEntry {
  rel: string;
  rule: string;
  contains?: string;
  reason: string;
}

export declare const SRC: string;
export declare const ALLOWLIST_FILE: string;
export declare const DIRECTION_HELPER: string;
export declare const CSS_RULES: PhysicalDirectionRule[];
export declare const TS_RULES: PhysicalDirectionRule[];
export declare const NUMERIC_ALIGN_RULE: string;
export declare const NUMERIC_ALIGN_RE: RegExp;
export declare const stripBlockComments: (code: string) => string;

export declare function scanCss(rel: string, code: string): PhysicalDirectionViolation[];
export declare function scanSource(rel: string, code: string): PhysicalDirectionViolation[];
export declare function scanNumericAlign(rel: string, code: string): PhysicalDirectionViolation[];
export declare function collect(root?: string): {
  violations: PhysicalDirectionViolation[];
  numeric: PhysicalDirectionViolation[];
};
export declare function readAllowlist(file?: string): { entries: PhysicalDirectionAllowlistEntry[] };
export declare function evaluate(
  violations: PhysicalDirectionViolation[],
  allowlist: { entries?: PhysicalDirectionAllowlistEntry[] },
): PhysicalDirectionFailure[];
export declare function runCheck(options?: { root?: string; allowlistFile?: string }): {
  violations: PhysicalDirectionViolation[];
  numeric: PhysicalDirectionViolation[];
  failures: PhysicalDirectionFailure[];
};
