import { describe, expect, it } from 'vitest';
// The checker is a plain .mjs tool imported directly, so the rules the CI gate enforces are
// tested here rather than trusted. Its own limits are in docs/I18N.md.
import {
  DIRECTION_HELPER,
  NUMERIC_ALIGN_RULE,
  collect,
  evaluate,
  readAllowlist,
  runCheck,
  scanCss,
  scanNumericAlign,
  scanSource,
  stripBlockComments,
} from './check-physical-directions.mjs';

const allow = (...entries: { rel: string; rule: string; contains?: string }[]) => ({
  entries: entries.map((entry) => ({
    ...entry,
    reason: 'a physical value with no logical equivalent, reviewed by hand',
  })),
});

describe('stripBlockComments', () => {
  it('blanks a comment without moving any line', () => {
    const stripped = stripBlockComments('a {}\n/* margin-left: 0 */\nb {}\n');

    expect(stripped.split('\n').length).toBe(4);
    expect(stripped).not.toContain('margin-left');
    expect(stripped.split('\n')[2]).toBe('b {}');
  });
});

describe('scanCss', () => {
  const rules = (css: string) => scanCss('example.css', css).map((v) => v.rule);

  it('catches the physical properties a right-to-left page would not mirror', () => {
    expect(rules('.a { margin-left: 8px; }')).toEqual(['margin-left']);
    expect(rules('.a { padding-right: 1rem; }')).toEqual(['padding-right']);
    expect(rules('.a { border-left: 1px solid red; }')).toEqual(['border-left']);
    expect(rules('.a { border-right-color: red; }')).toEqual(['border-right']);
    expect(rules('.a { left: 0; }')).toEqual(['position-left']);
    expect(rules('.a { right: 0; }')).toEqual(['position-right']);
    expect(rules('.a { text-align: right; }')).toEqual(['text-align']);
    expect(rules('.a { float: left; }')).toEqual(['float']);
  });

  it('accepts the logical properties that replace them', () => {
    expect(rules('.a { margin-inline-start: 8px; inset-inline-end: 0; text-align: start; }')).toEqual([]);
    // `margin-inline-start` must not be mistaken for `margin-left`, nor `border-right` for
    // the tail of a longer property name.
    expect(rules('.a { margin-inline-start: 8px; padding-inline-end: 1px; }')).toEqual([]);
  });

  it('catches a gradient direction, which CSS has no logical keyword for', () => {
    expect(rules('.a { background: linear-gradient(to left, #000, transparent); }')).toEqual([
      'gradient-direction',
    ]);
    // The parameterised form still hardcodes its fallback, and is what the allowlist records.
    expect(
      rules('.a { background: linear-gradient(var(--fms-fade-direction, to left), #000, #fff); }'),
    ).toEqual(['gradient-direction']);
  });

  it('ignores a commented-out rule, so prose cannot satisfy or trip the check', () => {
    expect(rules('/* margin-left: 8px; */\n.a { margin-inline-start: 8px; }')).toEqual([]);
  });

  it('reports the file, the line and what to write instead', () => {
    const [violation] = scanCss('x.css', '.a {}\n\n.a { margin-left: 8px; }');

    expect(violation.rel).toBe('x.css');
    expect(violation.line).toBe(3);
    expect(violation.target).toBe('margin-inline-start');
  });
});

describe('scanSource', () => {
  const rules = (code: string) => scanSource('example.tsx', code).map((v) => v.rule);

  it('catches a physical inline style', () => {
    expect(rules('<span style={{ marginLeft: 6 }} />')).toEqual(['inline-margin-left']);
    expect(rules('<span style={{ paddingRight: 6 }} />')).toEqual(['inline-padding-right']);
    expect(rules('<div style={{ borderLeft: "1px solid red" }} />')).toEqual(['inline-border-left']);
    expect(rules("<th style={{ textAlign: 'left' }} />")).toEqual(['inline-text-align']);
  });

  it('accepts the logical form React writes the same way', () => {
    expect(
      rules('<span style={{ marginInlineStart: 6, textAlign: \'start\', borderInlineStart: x }} />'),
    ).toEqual([]);
  });

  it('catches a directional icon, which antd does not mirror for you', () => {
    expect(rules('<Button icon={<ArrowLeftOutlined />} />')).toEqual(['directional-icon']);
    expect(rules('<Button icon={<LoginOutlined />} />')).toEqual(['directional-icon']);
    expect(rules('<Button icon={<MenuUnfoldOutlined />} />')).toEqual(['directional-icon']);
    // Neutral icons must stay allowed, or the check would be noise nobody reads.
    expect(rules('<Button icon={<PlusOutlined />} />')).toEqual([]);
    expect(rules('<Button icon={<SwapOutlined />} />')).toEqual([]);
  });

  it('exempts the one module that maps a role onto an icon', () => {
    expect(
      scanSource(DIRECTION_HELPER, "return rtl ? <ArrowRightOutlined /> : <ArrowLeftOutlined />;"),
    ).toEqual([]);
  });

  it('catches an arrow glyph baked into a string, which no stylesheet can mirror', () => {
    expect(rules("const marker = '\u21B3 ';")).toEqual(['arrow-glyph']);
    // A doc comment explaining the mapping is not a violation.
    expect(rules('/** `tree-branch` renders \u21B3 in English. */\nconst x = 1;')).toEqual([]);
  });

  it('catches a physically pinned table column', () => {
    expect(rules("const columns = [{ fixed: 'left' }];")).toEqual(['fixed-column']);
    expect(rules("const columns = [{ fixed: 'start' }];")).toEqual([]);
  });
});

describe('numeric column alignment is counted, not failed', () => {
  it('separates an aligned figure from a mirrored layout', () => {
    const code = "const columns = [{ align: 'right' }];";

    expect(scanSource('example.tsx', code)).toEqual([]);
    expect(scanNumericAlign('example.tsx', code).map((v) => v.rule)).toEqual([NUMERIC_ALIGN_RULE]);
  });
});

describe('evaluate', () => {
  const violation = (rel: string, rule: string, value: string, target = 'something logical') => ({
    rel,
    rule,
    value,
    line: 1,
    target,
  });

  it('fails a violation nobody justified', () => {
    const failures = evaluate(
      [violation('a.tsx', 'inline-margin-left', 'marginLeft: 6', 'marginInlineStart')],
      allow(),
    );

    expect(failures.map((f) => f.kind)).toEqual(['physical-direction']);
    // The report names the property to write instead, so the fix needs no research.
    expect(failures[0].detail).toContain('marginInlineStart');
    expect(failures[0].value).toBe('marginLeft: 6');
  });

  it('accepts a violation the allowlist explains by file and rule', () => {
    const failures = evaluate(
      [violation('a.css', 'gradient-direction', 'linear-gradient(to left, #000, #fff)')],
      allow({ rel: 'a.css', rule: 'gradient-direction' }),
    );

    expect(failures).toEqual([]);
  });

  it('lets an entry narrow to one line of a file', () => {
    const entry = () => allow({ rel: 'a.css', rule: 'gradient-direction', contains: 'fade' });

    expect(evaluate([violation('a.css', 'gradient-direction', 'var(--fade, to left)')], entry())).toEqual([]);
    // The entry no longer explains this line, so it fails as a violation *and* as stale —
    // which is the point of narrowing: the excuse is attached to a value, not to a file.
    expect(
      evaluate([violation('a.css', 'gradient-direction', 'linear-gradient(to left, #000)')], entry()).map(
        (f) => f.kind,
      ),
    ).toEqual(['physical-direction', 'allowlist-stale']);
  });

  it('fails a stale entry, so a fixed violation cannot leave its excuse behind', () => {
    const failures = evaluate([], allow({ rel: 'a.css', rule: 'gradient-direction' }));

    expect(failures.map((f) => f.kind)).toEqual(['allowlist-stale']);
  });

  it('fails an entry with no reason, so the list cannot become a rubber stamp', () => {
    const failures = evaluate([], {
      entries: [{ rel: 'a.css', rule: 'gradient-direction', reason: '' }],
    });

    expect(failures.map((f) => f.kind)).toEqual(['allowlist-reason-missing', 'allowlist-stale']);
  });
});

describe('the repository as it stands', () => {
  it('has no unmirrored layout direction and a justified allowlist', () => {
    const { violations, numeric, failures } = runCheck();

    expect(failures).toEqual([]);
    // Printed, not only asserted: the alignment decision is deliberately excluded, and a
    // count that jumps is how a reviewer notices the exclusion growing into something else.
    console.log(
      `physical directions: ${violations.length} physical value(s), all allowlisted; ` +
        `${numeric.length} numeric alignments excluded`,
    );
    expect(numeric.length).toBeGreaterThan(0);
  });

  it('scans both the stylesheets and the components, and every hit is one the allowlist explains', () => {
    const { violations } = collect();
    const entries = readAllowlist().entries;

    // The one that remains is the gradient fallback: a physical value with no logical
    // equivalent, recorded with a reason rather than left invisible.
    expect(violations.length).toBeGreaterThan(0);
    for (const violation of violations) {
      expect(entries.some((e) => e.rel === violation.rel && e.rule === violation.rule)).toBe(true);
    }
    expect(entries.length).toBeLessThanOrEqual(5);
  });
});
