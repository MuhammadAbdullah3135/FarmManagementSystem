import type { LineageNode } from '../../types';

/**
 * Geometry for the lineage chart.
 *
 * Why this is arithmetic instead of nested flex boxes: a pedigree is *ragged* — the sire may
 * have four recorded grandparents while the dam has none — and a flex row only aligns what
 * shares a flex line. Two nested branches of different heights therefore put their cards at
 * different offsets, and the connector between a pair of parents has no single height to sit
 * at. Nothing in CSS can align sibling cards on the generation they belong to *and* join them
 * to the generation above, because the join has to be drawn at a y that both halves agree on.
 *
 * So the page computes one card per loaded relative with an explicit inline-start offset and
 * top, and the connectors as straight runs between them. Three properties fall out of that,
 * and each one is asserted in `lineageLayout.test.ts` rather than hoped for:
 *
 *   - a stem is drawn into a card only when a *loaded* parent (or child) is on the other end,
 *     so no line can lead off to a generation that was never returned;
 *   - exactly one stem enters each card, from the bus of its own family;
 *   - every offset is positive, because the box scrolls forward only — the asked-about animal
 *     can never end up off the unreachable inline-start edge, which is where the previous
 *     markup clipped it on a phone.
 *
 * Sizes are parameters, not constants: the page reads them from the stylesheet's
 * `--fms-lineage-*` custom properties, so the phone breakpoint stays a media query.
 */
export interface LineageMetrics {
  cardWidth: number;
  cardHeight: number;
  /** Gap between two cards of the same generation. */
  gapX: number;
  /** Gap between two generations — the band a family's bus is drawn in. */
  gapY: number;
  linkWidth: number;
  padX: number;
  padY: number;
}

/**
 * Fallbacks for a runtime that cannot resolve the stylesheet's custom properties (jsdom, or a
 * browser that has not loaded the sheet yet). An unread property comes back as `''`, which
 * parses to `NaN`, and `NaN` geometry collapses the whole chart — so a read is only accepted
 * when it is a finite positive number.
 */
export const DEFAULT_LINEAGE_METRICS: LineageMetrics = {
  cardWidth: 220,
  cardHeight: 112,
  gapX: 32,
  gapY: 44,
  linkWidth: 2,
  padX: 12,
  padY: 12,
};

/** Which relation to the asked-about animal a card shows. Also picks the connector's colour. */
export type LineageSide = 'root' | 'sire' | 'dam' | 'child';

/** The two sides a parent can be on. An animal can be a sire or a dam of its child, not both. */
type ParentSide = Extract<LineageSide, 'sire' | 'dam'>;

export interface PlacedCard {
  key: string;
  node: LineageNode;
  side: LineageSide;
  /** Offset of the card's leading edge from the chart's inline-start edge. */
  x: number;
  /** Offset of the card's top from the chart's top edge. */
  y: number;
}

/**
 * One straight run of a family's connector, as a box: a stem is `linkWidth` wide, a bus is
 * `linkWidth` tall. Boxes rather than an SVG path so that they are placed with the same
 * logical inset (`insetInlineStart`) the cards use — the chart then mirrors itself in Arabic
 * without a second set of coordinates, and without a `[dir='rtl']` rule to find.
 */
export interface LinkSegment {
  key: string;
  side: LineageSide;
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface LineageLayout {
  cards: PlacedCard[];
  links: LinkSegment[];
  width: number;
  height: number;
  /** The asked-about animal's card, so the view can open on it rather than on either edge. */
  rootX: number;
  rootY: number;
  /** Generations actually drawn above and below the root; 0 means that half is empty. */
  ancestorRows: number;
  descendantRows: number;
}

interface Context {
  m: LineageMetrics;
  ancestorDepth: number;
  descendantDepth: number;
  /** Unique, deterministic keys for the rendered runs and cards. */
  seq: { n: number };
}

interface Branch {
  cards: PlacedCard[];
  links: LinkSegment[];
  /** Card edges, so a sibling is placed past the whole branch rather than past its own card. */
  minX: number;
  maxX: number;
  centreX: number;
  topY: number;
  bottomY: number;
  rows: number;
}

const nextKey = (context: Context, prefix: string) => `${prefix}${(context.seq.n += 1)}`;

const rowPitch = (m: LineageMetrics) => m.cardHeight + m.gapY;

/** A vertical run: `centre` is where the line's middle sits, `top` its highest edge. */
function stem(
  side: LineageSide,
  centre: number,
  top: number,
  height: number,
  context: Context,
): LinkSegment {
  const { m } = context;
  return {
    key: nextKey(context, 'stem-'),
    side,
    x: centre - m.linkWidth / 2,
    y: top,
    width: m.linkWidth,
    height,
  };
}

/**
 * A horizontal run bridging `from` to `to`. It is widened by half a line at each end so the
 * corner with the outermost stem is filled rather than notched.
 */
function bus(
  side: LineageSide,
  from: number,
  to: number,
  y: number,
  context: Context,
): LinkSegment {
  const { m } = context;
  return {
    key: nextKey(context, 'bus-'),
    side,
    x: from - m.linkWidth / 2,
    y: y - m.linkWidth / 2,
    width: to - from + m.linkWidth,
    height: m.linkWidth,
  };
}

/**
 * One animal's two (or fewer) parents, joined to it by a bus.
 *
 * The bus is drawn at the middle of the band between the rows, so it sits below every parent
 * card and above the child's. A parent that has no recorded parents of its own contributes
 * nothing above itself: that omission is the difference between this and the stub-per-card
 * the page used to draw.
 */
function ancestorFamily(
  side: LineageSide,
  parents: Array<{ centreX: number; bottomY: number; side: ParentSide }>,
  child: { centreX: number; topY: number },
  context: Context,
): LinkSegment[] {
  const { m } = context;
  const busY = child.topY - m.gapY / 2;
  // Each run wears the colour of the card it touches on its own end: the paternal line leaves
  // the sire card in the sire's colour, the bus and the arrival belong to the child.
  const runs = parents.map((parent) =>
    stem(parent.side, parent.centreX, parent.bottomY, busY - parent.bottomY, context),
  );

  const xs = [...parents.map((parent) => parent.centreX), child.centreX];
  const from = Math.min(...xs);
  const to = Math.max(...xs);
  // A single parent sits directly above its child, so the bus has no length to draw; the two
  // stems then read as one straight line into the child.
  if (to > from) runs.push(bus(side, from, to, busY, context));

  runs.push(stem(side, child.centreX, busY, child.topY - busY, context));
  return runs;
}

/**
 * One animal's offspring, joined by a bus drawn under it. Mirrors `ancestorFamily`: one stem
 * leaves the parent, one enters each child, and the bus spans the outermost pair. The lower
 * row of this chart is always an offspring, so the bus and the stems that arrive wear the
 * offspring colour while the run leaving the parent wears the parent's own.
 */
function descendantFamily(
  side: LineageSide,
  parent: { centreX: number; bottomY: number },
  children: Array<{ centreX: number; topY: number }>,
  context: Context,
): LinkSegment[] {
  const { m } = context;
  const busY = parent.bottomY + m.gapY / 2;
  const runs = [stem(side, parent.centreX, parent.bottomY, busY - parent.bottomY, context)];

  const xs = [parent.centreX, ...children.map((child) => child.centreX)];
  const from = Math.min(...xs);
  const to = Math.max(...xs);
  if (to > from) runs.push(bus('child', from, to, busY, context));

  for (const child of children) {
    runs.push(stem('child', child.centreX, busY, child.topY - busY, context));
  }
  return runs;
}

/**
 * An animal and everything recorded above it.
 *
 * `path` carries the animals already on this branch: a record whose parent is itself (or whose
 * grandparents point back at it) would otherwise recurse until the tab dies, and the farm's
 * data already contains one impossible parent — a female recorded as a sire.
 */
function placeAncestors(
  node: LineageNode,
  generation: number,
  side: LineageSide,
  start: number,
  context: Context,
  path: ReadonlySet<string>,
): Branch {
  const { m } = context;
  const parents: Array<{ node: LineageNode; side: ParentSide }> = [];
  if (generation < context.ancestorDepth) {
    if (node.sire && !path.has(node.sire.id)) parents.push({ node: node.sire, side: 'sire' });
    if (node.dam && !path.has(node.dam.id)) parents.push({ node: node.dam, side: 'dam' });
  }

  if (parents.length === 0) {
    const centreX = start + m.cardWidth / 2;
    const topY = -generation * rowPitch(m);
    return {
      cards: [{ key: nextKey(context, 'card-'), node, side, x: start, y: topY }],
      links: [],
      minX: start,
      maxX: start + m.cardWidth,
      centreX,
      topY,
      bottomY: topY + m.cardHeight,
      rows: generation,
    };
  }

  const cards: PlacedCard[] = [];
  const links: LinkSegment[] = [];
  const anchors: Array<{ centreX: number; bottomY: number; side: ParentSide }> = [];
  let cursor = start;
  let minX = Infinity;
  let maxX = -Infinity;
  let rows = generation;

  for (const parent of parents) {
    const branch = placeAncestors(
      parent.node,
      generation + 1,
      parent.side,
      cursor,
      context,
      new Set([...path, parent.node.id]),
    );
    cards.push(...branch.cards);
    links.push(...branch.links);
    minX = Math.min(minX, branch.minX);
    maxX = Math.max(maxX, branch.maxX);
    rows = Math.max(rows, branch.rows);
    anchors.push({ centreX: branch.centreX, bottomY: branch.bottomY, side: parent.side });
    cursor = branch.maxX + m.gapX;
  }

  // Centred on the parents' cards, not on their branches: a branch that extends further up and
  // out would otherwise pull the child sideways for no reason the reader can see.
  const centreX = (anchors[0].centreX + anchors[anchors.length - 1].centreX) / 2;
  const topY = -generation * rowPitch(m);
  cards.push({ key: nextKey(context, 'card-'), node, side, x: centreX - m.cardWidth / 2, y: topY });
  minX = Math.min(minX, centreX - m.cardWidth / 2);
  maxX = Math.max(maxX, centreX + m.cardWidth / 2);
  links.push(...ancestorFamily(side, anchors, { centreX, topY }, context));

  return {
    cards,
    links,
    minX,
    maxX,
    centreX,
    topY,
    bottomY: topY + m.cardHeight,
    rows,
  };
}

/** An animal and everything recorded below it, by the same rules as the ancestors. */
function placeDescendants(
  node: LineageNode,
  generation: number,
  side: LineageSide,
  start: number,
  context: Context,
  path: ReadonlySet<string>,
): Branch {
  const { m } = context;
  const children =
    generation < context.descendantDepth
      ? (node.offspring ?? []).filter((child) => !path.has(child.id))
      : [];

  if (children.length === 0) {
    const centreX = start + m.cardWidth / 2;
    const topY = generation * rowPitch(m);
    return {
      cards: [{ key: nextKey(context, 'card-'), node, side, x: start, y: topY }],
      links: [],
      minX: start,
      maxX: start + m.cardWidth,
      centreX,
      topY,
      bottomY: topY + m.cardHeight,
      rows: generation,
    };
  }

  const cards: PlacedCard[] = [];
  const links: LinkSegment[] = [];
  const anchors: Array<{ centreX: number; topY: number }> = [];
  let cursor = start;
  let minX = Infinity;
  let maxX = -Infinity;
  let rows = generation;

  for (const child of children) {
    const branch = placeDescendants(
      child,
      generation + 1,
      'child',
      cursor,
      context,
      new Set([...path, child.id]),
    );
    cards.push(...branch.cards);
    links.push(...branch.links);
    minX = Math.min(minX, branch.minX);
    maxX = Math.max(maxX, branch.maxX);
    rows = Math.max(rows, branch.rows);
    anchors.push({ centreX: branch.centreX, topY: branch.topY });
    cursor = branch.maxX + m.gapX;
  }

  const centreX = (anchors[0].centreX + anchors[anchors.length - 1].centreX) / 2;
  const topY = generation * rowPitch(m);
  cards.push({ key: nextKey(context, 'card-'), node, side, x: centreX - m.cardWidth / 2, y: topY });
  minX = Math.min(minX, centreX - m.cardWidth / 2);
  maxX = Math.max(maxX, centreX + m.cardWidth / 2);
  links.push(...descendantFamily(side, { centreX, bottomY: topY + m.cardHeight }, anchors, context));

  return {
    cards,
    links,
    minX,
    maxX,
    centreX,
    topY,
    bottomY: topY + m.cardHeight,
    rows,
  };
}

const shiftCards = (cards: PlacedCard[], dx: number, dy: number): PlacedCard[] =>
  cards.map((card) => ({ ...card, x: card.x + dx, y: card.y + dy }));

const shiftLinks = (links: LinkSegment[], dx: number, dy: number): LinkSegment[] =>
  links.map((link) => ({ ...link, x: link.x + dx, y: link.y + dy }));

/**
 * Turns one API response into everything the page needs to draw it.
 *
 * The tree is laid out in two independent passes — up from the root, down from the root —
 * because each pass centres a card on its own family and neither can see the other. They are
 * then joined at the root and slid into positive coordinates, which is what makes the chart
 * scroll forward only.
 */
export function buildLineageLayout(
  root: LineageNode,
  ancestorDepth: number,
  descendantDepth: number,
  metrics: LineageMetrics = DEFAULT_LINEAGE_METRICS,
): LineageLayout {
  const context: Context = {
    m: metrics,
    ancestorDepth: Math.max(0, ancestorDepth),
    descendantDepth: Math.max(0, descendantDepth),
    seq: { n: 0 },
  };

  const above = placeAncestors(root, 0, 'root', 0, context, new Set([root.id]));
  const below = placeDescendants(root, 0, 'root', 0, context, new Set([root.id]));

  // Above the root the rows run negative; below it they run positive from 0. Both are moved
  // down by the number of ancestor rows actually drawn, which is how the two halves meet on
  // the root's own row instead of overlapping on row 0.
  const dy = above.rows * rowPitch(metrics) + metrics.padY;
  const dx = above.centreX - below.centreX;

  const cards = [
    ...shiftCards(above.cards, 0, dy),
    // The root is placed by the ancestor pass; the descendant pass only supplied its row.
    ...shiftCards(
      below.cards.filter((card) => card.node !== root),
      dx,
      dy,
    ),
  ];
  const links = [...shiftLinks(above.links, 0, dy), ...shiftLinks(below.links, dx, dy)];

  let minX = Infinity;
  let maxX = -Infinity;
  let minY = Infinity;
  let maxY = -Infinity;
  for (const card of cards) {
    minX = Math.min(minX, card.x);
    maxX = Math.max(maxX, card.x + metrics.cardWidth);
    minY = Math.min(minY, card.y);
    maxY = Math.max(maxY, card.y + metrics.cardHeight);
  }

  const offsetX = metrics.padX - minX;
  const offsetY = metrics.padY - minY;
  const placed = shiftCards(cards, offsetX, offsetY);
  const rootCard = placed.find((card) => card.node === root);
  if (!rootCard) throw new Error('lineage layout lost the root card');

  return {
    cards: placed,
    links: shiftLinks(links, offsetX, offsetY),
    width: maxX - minX + metrics.padX * 2,
    height: maxY - minY + metrics.padY * 2,
    rootX: rootCard.x + metrics.cardWidth / 2,
    rootY: rootCard.y,
    ancestorRows: above.rows,
    descendantRows: below.rows,
  };
}

/** Reads one `--fms-lineage-*` length, falling back when it is absent or unusable. */
export function readMetric(styles: CSSStyleDeclaration, name: string, fallback: number): number {
  const value = Number.parseFloat(styles.getPropertyValue(name));
  return Number.isFinite(value) && value > 0 ? value : fallback;
}

/** The metrics a rendered canvas reports, so a relayout only happens when they really changed. */
export function metricsOf(styles: CSSStyleDeclaration): LineageMetrics {
  const d = DEFAULT_LINEAGE_METRICS;
  return {
    cardWidth: readMetric(styles, '--fms-lineage-card-w', d.cardWidth),
    cardHeight: readMetric(styles, '--fms-lineage-card-h', d.cardHeight),
    gapX: readMetric(styles, '--fms-lineage-gap-x', d.gapX),
    gapY: readMetric(styles, '--fms-lineage-gap-y', d.gapY),
    linkWidth: readMetric(styles, '--fms-lineage-link-w', d.linkWidth),
    padX: readMetric(styles, '--fms-lineage-pad-x', d.padX),
    padY: readMetric(styles, '--fms-lineage-pad-y', d.padY),
  };
}

export const sameMetrics = (a: LineageMetrics, b: LineageMetrics): boolean =>
  a.cardWidth === b.cardWidth &&
  a.cardHeight === b.cardHeight &&
  a.gapX === b.gapX &&
  a.gapY === b.gapY &&
  a.linkWidth === b.linkWidth &&
  a.padX === b.padX &&
  a.padY === b.padY;
