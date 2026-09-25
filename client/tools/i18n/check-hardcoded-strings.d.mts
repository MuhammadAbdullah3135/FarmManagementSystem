// Type surface for the checker so its test can import the .mjs directly.
export interface HardcodedStringViolation {
  rel: string;
  kind: 'jsx-text' | 'attribute' | 'toast' | 'property' | 'template';
  value: string;
}

export declare const USER_ATTRS: Set<string>;
export declare const TOAST_KINDS: Set<string>;

export declare function scanSource(rel: string, code: string): HardcodedStringViolation[];
export declare function collectViolations(root?: string): HardcodedStringViolation[];
export declare function newStrings(
  violations: HardcodedStringViolation[],
  baselineIds: Set<string> | string[],
): string[];
