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

/**
 * The `@media (max-width: 991.98px)` block that closes the file — the width AppLayout itself calls
 * mobile (`screen and (max-width: 991.98px)`). It is sliced from its own marker rather than from
 * the phone marker, because it sits after the phone block and would otherwise be read as part of
 * it.
 */
const narrowBlock = (): string => {
  const start = css.indexOf('@media (max-width: 991.98px)');
  if (start < 0) throw new Error('AppLayout.css no longer has a @media (max-width: 991.98px) block');
  return css.slice(start).replace(/\/\*[\s\S]*?\*\//g, '');
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
/*
 * The notification centre (and three sibling screens with the same header) squeezed its
 * heading into one character per line on a phone. antd's Typography carries
 * `word-break: break-word`, so a title block reports a min-content width of about one
 * character; in a single-line flex row the controls beside it take their width first and the
 * heading collapses into the remainder. The row must wrap, and the title block must carry a
 * real basis so it claims room before the controls are offered what is left.
 *
 * The notification rows one level down have the same shape: a text column plus an action
 * group that does not fit, which is how the severity tag and the View / Mark read / Dismiss
 * links ended up on one line. The row wraps for the same reason, and its text column carries
 * the same basis.
 *
 * jsdom cannot measure any of this, so — like the card-head fix above — the guard is the
 * shape of the selectors rather than pixels. Comments are stripped by the helpers, so this
 * prose can never satisfy a check.
 */
describe('AppLayout.css page header (all widths)', () => {
  it('floors the title block, rather than leaving it at its one-character min-content', () => {
    expect(baseBlock()).toMatch(
      /\.fms-page-header\s*>\s*\.ant-space-item:first-child\s*\{[^}]*min-width:\s*260px/,
    );
    // A floor and not a growth: growing the title block would take width away from the
    // controls, changing a header that already fits.
    expect(baseBlock()).not.toMatch(
      /\.fms-page-header\s*>\s*\.ant-space-item:first-child\s*\{[^}]*flex:\s*1/,
    );
  });

  it('keeps a wrapped control group inside the card instead of past its border', () => {
    expect(baseBlock()).toMatch(
      /\.fms-page-header\s*>\s*\.ant-space-item:last-child\s*\{[^}]*max-width:\s*100%/,
    );
  });

  it('does not wrap the header at every width, which would restack it on a laptop', () => {
    expect(baseBlock()).not.toMatch(/\.fms-page-header\s*\{[^}]*flex-wrap/);
  });
});

describe('AppLayout.css page header below the mobile breakpoint', () => {
  it('wraps it there, so the controls drop below rather than hang past the card', () => {
    expect(narrowBlock()).toMatch(/\.fms-page-header\s*\{[^}]*flex-wrap:\s*wrap/);
  });
});

describe('AppLayout.css notification row (all widths)', () => {
  it('wraps the row instead of squeezing the text column beside the actions', () => {
    expect(baseBlock()).toMatch(/\.fms-notification-item\s*\{[^}]*flex-wrap:\s*wrap/);
  });

  it('gives the text column a real basis and lets it shrink to the row', () => {
    expect(baseBlock()).toMatch(
      /\.fms-notification-item\s*>\s*\.fms-notification-body\s*\{[^}]*flex:\s*1 1 260px[^}]*min-width:\s*0/,
    );
  });

  it('keeps a wrapped action group inside the row', () => {
    expect(baseBlock()).toMatch(
      /\.fms-notification-item\s*>\s*\.fms-notification-actions\s*\{[^}]*max-width:\s*100%/,
    );
  });
});

describe('AppLayout.css page header and notification row on a phone', () => {
  it('gives the header two rows: the title, then the controls', () => {
    expect(mobileBlock()).toMatch(/\.fms-page-header\s*\{[^}]*flex-direction:\s*column/);
    // The floor is dropped again: in a column it would be a width the phone has to fit
    // inside the row it is stacked in.
    expect(mobileBlock()).toMatch(
      /\.fms-page-header\s*>\s*\.ant-space-item:first-child\s*\{[^}]*min-width:\s*0/,
    );
  });

  it('lets the controls wrap inside their own row rather than hang past the card', () => {
    expect(mobileBlock()).toMatch(
      /\.fms-page-header\s*>\s*\.ant-space-item:last-child\s*>\s*\.ant-space\s*\{[^}]*flex-wrap:\s*wrap/,
    );
  });

  it('stacks a notification row, so the severity tag cannot share a line with the actions', () => {
    expect(mobileBlock()).toMatch(/\.fms-notification-item\s*\{[^}]*flex-direction:\s*column/);
    expect(mobileBlock()).toMatch(
      /\.fms-notification-item\s*>\s*\.fms-notification-actions\s*\{[^}]*justify-content:\s*flex-end/,
    );
  });
});

/*
 * The lineage chart draws its own cards and connectors, and the page positions all of them 
 * itself. That puts two things in this stylesheet that the geometry depends on:
 *
 *   - the sizes. They are published as custom properties on the scroller *because* JavaScript
 *     reads them back to lay the chart out; a variable that disappears does not fail, it
 *     silently falls back to a desktop default, so each one is asserted to exist.
 *
 *   - the accent edges. A `data-side` with no colour leaves the connector's
 *     `var(--fms-lineage-link-color)` invalid, and an invalid `background` paints nothing at
 *     all: the lines would simply be missing, which no colour-blind spot-check would find.
 *
 * The physical-direction guard already covers `inset-inline-start` over `left`; what is
 * asserted here is only what that checker cannot know.
 */
describe('AppLayout.css lineage chart', () => {
  const METRIC_VARIABLES = [
    '--fms-lineage-card-w',
    '--fms-lineage-card-h',
    '--fms-lineage-gap-x',
    '--fms-lineage-gap-y',
    '--fms-lineage-link-w',
    '--fms-lineage-pad-x',
    '--fms-lineage-pad-y',
  ];

  it('publishes every size the layout reads back from the canvas', () => {
    for (const name of METRIC_VARIABLES) {
      expect(baseBlock()).toMatch(new RegExp(`\\.fms-lineage-canvas\\s*\\{[^}]*${name}:`));
    }
  });

  it('scrolls the chart sideways only, so a wide generation is reachable rather than clipped', () => {
    const canvas = /\.fms-lineage-canvas\s*\{([^}]*)\}/.exec(baseBlock())?.[1] ?? '';
    expect(canvas).toMatch(/overflow-x:\s*auto/);
    expect(canvas).toMatch(/overflow-y:\s*hidden/);
    // Bare `overflow: hidden` would clip the far generations away with nothing to scroll to.
    expect(canvas).not.toMatch(/overflow:\s*(hidden|auto|clip|scroll)/);
  });

  it('sizes the card slot from the very variable the geometry was computed against', () => {
    const slot = /\.fms-lineage-card-slot\s*\{([^}]*)\}/.exec(baseBlock())?.[1] ?? '';
    expect(slot).toMatch(/inline-size:\s*var\(--fms-lineage-card-w\)/);
    expect(slot).toMatch(/block-size:\s*var\(--fms-lineage-card-h\)/);
    expect(slot).toMatch(/position:\s*absolute/);
  });

  it('hides a card body that outgrows its box instead of letting it move the next stem', () => {
    const card = /\.fms-lineage-card\s*\{([^}]*)\}/.exec(baseBlock())?.[1] ?? '';
    expect(card).toMatch(/block-size:\s*100%/);
    expect(card).toMatch(/overflow:\s*hidden/);
  });

  it('accents the card on its logical edge, so the chart mirrors in Arabic', () => {
    expect(baseBlock()).toMatch(
      /\.fms-lineage-card\s*\{[^}]*border-inline-start-width:\s*4px[^}]*border-inline-start-style:\s*solid/,
    );
  });

  it('gives every connector side a colour of its own', () => {
    for (const side of ['sire', 'dam', 'child', 'root']) {
      expect(baseBlock()).toMatch(
        new RegExp(`\\.fms-lineage-link\\[data-side='${side}'\\]\\s*\\{[^}]*--fms-lineage-link-color`),
      );
    }
    expect(baseBlock()).toMatch(/\.fms-lineage-link\s*\{[^}]*background:\s*var\(--fms-lineage-link-color\)/);
  });
});

describe('AppLayout.css lineage chart on a phone', () => {
  it('gives the phone its own card and gap sizes rather than the desktop ones', () => {
    const canvas = /\.fms-lineage-canvas\s*\{([^}]*)\}/.exec(mobileBlock())?.[1] ?? '';
    expect(canvas).toMatch(/--fms-lineage-card-w:\s*200px/);
    expect(canvas).toMatch(/--fms-lineage-gap-x:\s*12px/);
    expect(canvas).toMatch(/--fms-lineage-gap-y:\s*32px/);
    expect(canvas).toMatch(/--fms-lineage-pad-x:\s*8px/);
  });

  it('stacks the controls, stretching the picker past the width it carries inline', () => {
    expect(mobileBlock()).toMatch(/\.fms-lineage-controls\s*\{[^}]*flex-direction:\s*column/);
    // antd's Space is an inline-flex box, so without this it shrinks to its widest item and the
    // `width: 100%` below is 100% of that instead of of the card — measured at 220px on a 360px
    // phone before the rule was added.
    expect(mobileBlock()).toMatch(/\.fms-lineage-controls\s*\{[^}]*display:\s*flex/);
    expect(mobileBlock()).toMatch(/\.fms-lineage-controls\s*\{[^}]*inline-size:\s*100%/);
    expect(mobileBlock()).toMatch(
      /\.fms-lineage-controls\s+\.ant-select\s*\{[^}]*width:\s*100%\s*!important/,
    );
  });
});

describe('AppLayout.css mobile filter rows', () => {
  it('stacks the columns of an fms-filter-row', () => {
    expect(mobileBlock()).toMatch(/\.fms-filter-row\s*>\s*\.ant-col\s*\{[^}]*flex:\s*0 0 100%/);
  });

  it('makes the controls in that row full width, past their inline width', () => {
    expect(mobileBlock()).toMatch(/\.fms-filter-row\s+\.ant-(?:picker|select|input)[^{]*\{[^}]*width:\s*100%\s*!important/);
  });
});
