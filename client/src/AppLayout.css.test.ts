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
 * Comments are stripped so that prose in the file can never satisfy a check.
 */
const mobileBlock = (): string => {
  const start = css.indexOf('@media (max-width: 576px)');
  if (start < 0) throw new Error('AppLayout.css no longer has a @media (max-width: 576px) block');
  return css.slice(start).replace(/\/\*[\s\S]*?\*\//g, '');
};

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
