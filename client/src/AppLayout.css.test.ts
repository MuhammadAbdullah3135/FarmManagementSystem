/// <reference types="node" />
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';

// Read from disk rather than importing: the assertions are about what the
// stylesheet says, and vitest's CSS handling does not hand back the raw text.
// vitest runs with the vite root (client/) as its cwd, here and in CI.
const css = readFileSync(path.resolve(process.cwd(), 'src/AppLayout.css'), 'utf8');

/*
 * jsdom cannot measure layout, so the mobile card-head fix is guarded by its
 * selector shape instead of by pixels. The bug this encodes: antd's card head
 * is two levels — `.ant-card-head` is a plain block and the flex row is
 * `.ant-card-head-wrapper` inside it — so the obvious-looking
 * `.ant-card-head { flex-wrap: wrap }` never wrapped anything and the title
 * stayed ellipsised to "P…" on a phone.
 *
 * That fix is split across two blocks: an all-width guard (the head row wraps, so actions that
 * cannot share the line drop below it) and the phone block (the head stacks and its controls go
 * full width). They are asserted separately because they fix different ranges of window widths.
 *
 * Comments are stripped so that prose in the file can never satisfy a check.
 */
const mobileBlock = (): string => {
  const start = css.indexOf('@media (max-width: 576px)');
  if (start < 0) throw new Error('AppLayout.css no longer has a @media (max-width: 576px) block');
  return css.slice(start).replace(/\/\*[\s\S]*?\*\//g, '');
};

/** Everything that applies at every width: the part of the file before the phone media query. */
const baseBlock = (): string => {
  const end = css.indexOf('@media (max-width: 576px)');
  if (end < 0) throw new Error('AppLayout.css no longer has a @media (max-width: 576px) block');
  return css.slice(0, end).replace(/\/\*[\s\S]*?\*\//g, '');
};

/*
 * The phone block cannot be the whole fix: antd's head row does not wrap and its title is `flex: 1`
 * (a base size of 0), so an `extra` wider than the leftover space ellipsises the title at *any*
 * width. hr/salary-payments lost its "Payroll Report" title on every window under ~1800px, which
 * the 576px query never reached. The guard is a wrap on the row plus a shrink floor on the actions,
 * deliberately leaving the title's own rules alone: a flex line forms from base sizes, so a short
 * extra still shares the row, a long title still truncates with an ellipsis, and only a wide action
 * area wraps.
 */
describe('AppLayout.css card head at every width', () => {
  it('lets the head row wrap, so wide actions drop onto a line of their own', () => {
    expect(baseBlock()).toMatch(
      /\.ant-card-head\s*>\s*\.ant-card-head-wrapper\s*\{[^}]*flex-wrap:\s*wrap/,
    );
  });

  it('keeps a wrapped action area inside the card instead of past its border', () => {
    expect(baseBlock()).toMatch(/\.ant-card-extra\s*\{[^}]*min-width:\s*0[^}]*max-width:\s*100%/);
  });

  it('leaves the title rules alone, so a long title still truncates rather than wrapping', () => {
    expect(baseBlock()).not.toMatch(/\.ant-card-head-title\s*\{/);
  });
});

describe('AppLayout.css mobile card head', () => {
  it('stacks the head on the element that is actually the flex container', () => {
    expect(mobileBlock()).toMatch(
      /\.ant-card-head\s*>\s*\.ant-card-head-wrapper\s*\{[^}]*flex-direction:\s*column/,
    );
  });

  it('lets the title use the full row instead of being ellipsised', () => {
    expect(mobileBlock()).toMatch(/\.ant-card-head-title\s*\{[^}]*text-overflow:\s*clip/);
  });

  it('makes a filter in the head full width, past its inline width', () => {
    expect(mobileBlock()).toMatch(
      /\.ant-card-extra\s+\.ant-(?:select|picker|input)[^{]*\{[^}]*width:\s*100%\s*!important/,
    );
  });

  it('does not go back to flex-wrap on .ant-card-head, which is not the flex container', () => {
    expect(mobileBlock()).not.toMatch(/\.ant-card-head\s*\{[^}]*flex-wrap/);
  });
});

/*
 * The same file gives top-of-page filter rows (`<Row className="fms-filter-row">`) one full-width
 * control per line. Those rows deliberately keep auto-sized columns, because the responsive `Col`
 * props cannot express "natural width on a laptop" — `xs={24}` renders flex: 0 0 100% at every
 * width in this antd version, which stacked the desktop filters too.
 */
describe('AppLayout.css mobile filter rows', () => {
  it('stacks the columns of an fms-filter-row', () => {
    expect(mobileBlock()).toMatch(/\.fms-filter-row\s*>\s*\.ant-col\s*\{[^}]*flex:\s*0 0 100%/);
  });

  it('makes the controls in that row full width, past their inline width', () => {
    expect(mobileBlock()).toMatch(/\.fms-filter-row\s+\.ant-(?:picker|select|input)[^{]*\{[^}]*width:\s*100%\s*!important/);
  });
});
