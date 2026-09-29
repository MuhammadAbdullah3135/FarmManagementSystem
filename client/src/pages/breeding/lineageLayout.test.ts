import { describe, expect, it } from 'vitest';
import type { LineageNode } from '../../types';
import {
  DEFAULT_LINEAGE_METRICS,
  buildLineageLayout,
  metricsOf,
  type LineageLayout,
  type LineageMetrics,
  type PlacedCard,
} from './lineageLayout';

/*
 * The chart's guarantee is geometric, not visual, so it is asserted here rather than left to
 * a screenshot: jsdom cannot measure a layout, and "it looked right at 1280" says nothing
 * about the phone width the reader complained about. Every claim the page makes about its
 * connectors — one stem into each card, nothing drawn towards a generation that was never
 * returned, nothing at a negative offset — is a statement about these numbers.
 */
const M: LineageMetrics = DEFAULT_LINEAGE_METRICS;

const animal = (overrides: Partial<LineageNode> & Pick<LineageNode, 'id' | 'tagNumber'>): LineageNode => ({
  sex: 'Female',
  status: 'Active',
  isRoot: false,
  offspring: [],
  ...overrides,
});

const layoutOf = (root: LineageNode, ancestorDepth = 5, descendantDepth = 0) =>
  buildLineageLayout(root, ancestorDepth, descendantDepth, M);

const cardOf = (layout: LineageLayout, id: string): PlacedCard => {
  const card = layout.cards.find((candidate) => candidate.node.id === id);
  if (!card) throw new Error(`no card placed for ${id}`);
  return card;
};

const centreOf = (card: PlacedCard) => card.x + M.cardWidth / 2;

/** Runs that end exactly on a card's top edge and overlap it: the stems entering the card. */
const entering = (layout: LineageLayout, card: PlacedCard) =>
  layout.links.filter(
    (link) =>
      link.y + link.height === card.y &&
      link.x <= centreOf(card) &&
      link.x + link.width >= centreOf(card),
  );

/** Runs that start exactly on a card's bottom edge and overlap it: the stems leaving it. */
const leaving = (layout: LineageLayout, card: PlacedCard) =>
  layout.links.filter(
    (link) =>
      link.y === card.y + M.cardHeight &&
      link.x <= centreOf(card) &&
      link.x + link.width >= centreOf(card),
  );

/**
 * The record the farm actually holds: TAG-0058-2, a female, recorded as the sire of TAG-0079.
 * The view is not allowed to reject or crash on it — correcting the data is a separate job —
 * so it is the standing fixture for every claim below about the *sire* side.
 */
const femaleSire = animal({ id: 'sire', tagNumber: 'TAG-0058-2', name: 'Seed Calf B', sex: 'Female' });
const dam = animal({ id: 'dam', tagNumber: 'TAG-0100', name: 'Boocho Dam' });
const root = animal({
  id: 'root',
  tagNumber: 'TAG-0079',
  name: 'Boocho',
  isRoot: true,
  sire: femaleSire,
  dam,
});

describe('lineage chart layout: generations', () => {
  it('puts the parents on one row above the root, sharing a baseline', () => {
    const layout = layoutOf(root);
    const [sireCard, damCard, rootCard] = [
      cardOf(layout, 'sire'),
      cardOf(layout, 'dam'),
      cardOf(layout, 'root'),
    ];

    expect(layout.cards).toHaveLength(3);
    // Same generation, same top: a flex row of uneven branches is what used to let these drift.
    expect(sireCard.y).toBe(damCard.y);
    expect(rootCard.y).toBe(sireCard.y + M.cardHeight + M.gapY);
    expect(sireCard.side).toBe('sire');
    expect(rootCard.side).toBe('root');
    expect(layout.ancestorRows).toBe(1);
    expect(layout.descendantRows).toBe(0);
  });

  it('centres the root between its two parents, and each parent over its own parents', () => {
    const grandsire = animal({ id: 'gs', tagNumber: 'TAG-0001' });
    const grandsire2 = animal({ id: 'gs2', tagNumber: 'TAG-0002' });
    const withGrandparents = { ...root, sire: { ...femaleSire, sire: grandsire, dam: grandsire2 } };

    const layout = layoutOf(withGrandparents);
    const rootCard = cardOf(layout, 'root');
    const sireCard = cardOf(layout, 'sire');
    const damCard = cardOf(layout, 'dam');

    expect(centreOf(rootCard)).toBe((centreOf(sireCard) + centreOf(damCard)) / 2);
    expect(centreOf(sireCard)).toBe(
      (centreOf(cardOf(layout, 'gs')) + centreOf(cardOf(layout, 'gs2'))) / 2,
    );
    expect(cardOf(layout, 'gs').y).toBe(cardOf(layout, 'gs2').y);
    expect(layout.ancestorRows).toBe(2);
  });

  it('stops at the depth the API was asked for', () => {
    const grandsire = animal({ id: 'gs', tagNumber: 'TAG-0001' });
    const threeDeep = { ...root, sire: { ...femaleSire, sire: grandsire } };

    expect(layoutOf(threeDeep, 1).cards.map((card) => card.node.id).sort()).toEqual([
      'dam',
      'root',
      'sire',
    ]);
    expect(layoutOf(threeDeep, 0).cards).toHaveLength(1);
    expect(layoutOf(threeDeep, 5).cards).toHaveLength(4);
  });

  it('lays the offspring out under the root, one row per generation', () => {
    const calfA = animal({ id: 'a', tagNumber: 'TAG-0200' });
    const calfB = animal({ id: 'b', tagNumber: 'TAG-0201' });
    const calfC = animal({ id: 'c', tagNumber: 'TAG-0202' });
    const grandCalf = animal({ id: 'gc', tagNumber: 'TAG-0300' });
    const breeder = { ...root, offspring: [calfA, { ...calfB, offspring: [grandCalf] }, calfC] };

    const layout = layoutOf(breeder, 0, 3);
    const rootCard = cardOf(layout, 'root');

    expect(layout.descendantRows).toBe(2);
    for (const id of ['a', 'b', 'c']) {
      expect(cardOf(layout, id).y).toBe(rootCard.y + M.cardHeight + M.gapY);
      expect(entering(layout, cardOf(layout, id))).toHaveLength(1);
    }
    expect(cardOf(layout, 'gc').y).toBe(rootCard.y + (M.cardHeight + M.gapY) * 2);
    expect(entering(layout, cardOf(layout, 'gc'))).toHaveLength(1);
    // Grandchildren sit under their own parent, not under the root.
    expect(centreOf(cardOf(layout, 'gc'))).toBe(centreOf(cardOf(layout, 'b')));
  });

  it('stops adding offspring at the requested depth', () => {
    const calf = animal({ id: 'a', tagNumber: 'TAG-0200' });
    const grandCalf = animal({ id: 'gc', tagNumber: 'TAG-0300' });
    const breeder = { ...root, offspring: [{ ...calf, offspring: [grandCalf] }] };

    expect(layoutOf(breeder, 0, 1).cards.map((card) => card.node.id).sort()).toEqual(['a', 'root']);
    expect(layoutOf(breeder, 0, 0).cards.map((card) => card.node.id)).toEqual(['root']);
  });

  it('draws the root once even though both halves are laid out from it', () => {
    const calf = animal({ id: 'a', tagNumber: 'TAG-0200' });
    const layout = layoutOf({ ...root, offspring: [calf] }, 5, 3);
    expect(layout.cards.filter((card) => card.node.id === 'root')).toHaveLength(1);
  });
});

describe('lineage chart layout: connectors', () => {
  it('draws exactly one stem into each card that has a loaded parent', () => {
    const layout = layoutOf(root);
    expect(entering(layout, cardOf(layout, 'root'))).toHaveLength(1);
    // The parents have none of their own, so nothing is drawn towards that generation.
    expect(entering(layout, cardOf(layout, 'sire'))).toHaveLength(0);
    expect(entering(layout, cardOf(layout, 'dam'))).toHaveLength(0);
  });

  it('joins both parents to the child with one bus, and leaves the child none of its own', () => {
    const layout = layoutOf(root);
    const sireCard = cardOf(layout, 'sire');
    const damCard = cardOf(layout, 'dam');
    const rootCard = cardOf(layout, 'root');

    // One stem descends from each parent into the band, and one bus spans between them.
    expect(leaving(layout, sireCard)).toHaveLength(1);
    expect(leaving(layout, damCard)).toHaveLength(1);
    const busses = layout.links.filter((link) => link.height === M.linkWidth && link.width > M.linkWidth);
    expect(busses).toHaveLength(1);
    expect(busses[0].x).toBe(centreOf(sireCard) - M.linkWidth / 2);
    expect(busses[0].x + busses[0].width).toBe(centreOf(damCard) + M.linkWidth / 2);
    // The bus sits in the band between the rows, never on a card's edge.
    expect(busses[0].y).toBeGreaterThan(sireCard.y + M.cardHeight);
    expect(busses[0].y).toBeLessThan(rootCard.y);

    // Every run wears the colour of the card it touches on its own end: the stem up to the
    // sire is the sire's blue, the stem up to the dam the dam's pink, and the bus plus the
    // stem into the root belong to the root. A single colour per family would paint this
    // whole chart gold, which is the one thing the accents on the cards already say.
    expect(leaving(layout, sireCard)[0].side).toBe('sire');
    expect(leaving(layout, damCard)[0].side).toBe('dam');
    expect(entering(layout, rootCard)[0].side).toBe('root');
    expect(busses[0].side).toBe('root');
  });

  it('draws a single straight line when only one parent was recorded', () => {
    const layout = layoutOf({ ...root, dam: undefined });
    const sireCard = cardOf(layout, 'sire');
    const rootCard = cardOf(layout, 'root');

    expect(layout.cards).toHaveLength(2);
    expect(leaving(layout, sireCard)).toHaveLength(1);
    expect(entering(layout, rootCard)).toHaveLength(1);
    // A one-parent family has no width to bridge, so there is no bus at all.
    expect(layout.links.filter((link) => link.height === M.linkWidth && link.width > M.linkWidth)).toHaveLength(0);
    expect(centreOf(rootCard)).toBe(centreOf(sireCard));
    expect(layout.links.every((link) => link.x === centreOf(sireCard) - M.linkWidth / 2)).toBe(true);
    expect(leaving(layout, sireCard)[0].side).toBe('sire');
    expect(entering(layout, rootCard)[0].side).toBe('root');
  });

  it('draws a stem out of the root only when offspring were recorded', () => {
    expect(leaving(layoutOf(root), cardOf(layoutOf(root), 'root'))).toHaveLength(0);

    const calf = animal({ id: 'a', tagNumber: 'TAG-0200' });
    const withCalf = layoutOf({ ...root, offspring: [calf] }, 5, 3);
    expect(leaving(withCalf, cardOf(withCalf, 'root'))).toHaveLength(1);
    expect(entering(withCalf, cardOf(withCalf, 'a'))).toHaveLength(1);
  });

  it('never emits a degenerate run, which would be a connector drawn to nothing', () => {
    const calf = animal({ id: 'a', tagNumber: 'TAG-0200' });
    const grandCalf = animal({ id: 'gc', tagNumber: 'TAG-0300' });
    const layout = layoutOf({ ...root, offspring: [{ ...calf, offspring: [grandCalf] }] }, 5, 3);
    expect(layout.links.length).toBeGreaterThan(0);
    for (const link of layout.links) {
      expect(link.width).toBeGreaterThan(0);
      expect(link.height).toBeGreaterThan(0);
    }
  });

  it('keeps every run between the two cards it joins', () => {
    const layout = layoutOf(root);
    const sireCard = cardOf(layout, 'sire');
    const rootCard = cardOf(layout, 'root');
    const top = Math.min(sireCard.y, rootCard.y);
    const bottom = Math.max(sireCard.y + M.cardHeight, rootCard.y + M.cardHeight);
    for (const link of layout.links) {
      expect(link.y).toBeGreaterThanOrEqual(top);
      expect(link.y + link.height).toBeLessThanOrEqual(bottom);
    }
  });
});

describe('lineage chart layout: the box only ever grows forwards', () => {
  it('leaves nothing at a negative offset, so the root can never be clipped off the edge', () => {
    const grandsire = animal({ id: 'gs', tagNumber: 'TAG-0001' });
    const calf = animal({ id: 'a', tagNumber: 'TAG-0200' });
    const layout = layoutOf(
      { ...root, sire: { ...femaleSire, sire: grandsire }, offspring: [calf] },
      5,
      3,
    );

    for (const card of layout.cards) {
      expect(card.x).toBeGreaterThanOrEqual(M.padX);
      expect(card.y).toBeGreaterThanOrEqual(M.padY);
    }
    for (const link of layout.links) {
      expect(link.x).toBeGreaterThanOrEqual(0);
      expect(link.y).toBeGreaterThanOrEqual(0);
    }
  });

  it('sizes the chart to its own contents, with the root in the middle', () => {
    const layout = layoutOf(root);
    const widest = Math.max(...layout.cards.map((card) => card.x + M.cardWidth));
    expect(layout.width).toBe(widest + M.padX);
    expect(layout.height).toBe(M.padY * 2 + M.cardHeight + M.gapY + M.cardHeight);
    // Opening the view on the root is the point of reporting it.
    expect(layout.rootX).toBe(centreOf(cardOf(layout, 'root')));
    expect(layout.rootX).toBeGreaterThan(0);
    expect(layout.rootX).toBeLessThan(layout.width);
  });

  it('lays out whatever sizes it is given, so the phone breakpoint stays a media query', () => {
    const phone = buildLineageLayout(root, 5, 0, {
      ...M,
      cardWidth: 200,
      gapX: 12,
      gapY: 32,
      padX: 8,
    });
    const sireCard = cardOf(phone, 'sire');
    const damCard = cardOf(phone, 'dam');
    const rootCard = cardOf(phone, 'root');

    // Two cards and the phone's gap, not the desktop's 252.
    expect(damCard.x - sireCard.x).toBe(212);
    expect(rootCard.y - sireCard.y).toBe(M.cardHeight + 32);
    expect(rootCard.x + 200 / 2).toBe(phone.rootX);
    expect(phone.width).toBe(428);
  });
});

describe('lineage chart layout: impossible records', () => {
  it('renders a female recorded as a sire rather than rejecting it', () => {
    const layout = layoutOf(root);
    const sireCard = cardOf(layout, 'sire');
    expect(sireCard.node.sex).toBe('Female');
    expect(sireCard.side).toBe('sire');
    expect(entering(layout, cardOf(layout, 'root'))).toHaveLength(1);
  });

  it('terminates when an animal is recorded as its own parent', () => {
    const selfParent = animal({ id: 'loop', tagNumber: 'TAG-0009' });
    const looping = { ...selfParent, sire: selfParent };
    const layout = layoutOf(looping, 10, 3);
    expect(layout.cards.map((card) => card.node.id)).toEqual(['loop']);
    expect(layout.links).toEqual([]);
  });

  it('terminates when two animals are each other\u2019s sire', () => {
    const first = animal({ id: 'first', tagNumber: 'TAG-0011' });
    const second = animal({ id: 'second', tagNumber: 'TAG-0012' });
    const looping = { ...first, sire: { ...second, sire: first } };
    const layout = layoutOf(looping, 10, 0);
    expect(layout.cards).toHaveLength(2);
    expect(entering(layout, cardOf(layout, 'first'))).toHaveLength(1);
  });

  it('ignores an offspring recorded as its own parent\u2019s parent', () => {
    const calf = animal({ id: 'a', tagNumber: 'TAG-0200' });
    const circular = { ...root, offspring: [{ ...calf, offspring: [root] }] };
    const layout = layoutOf(circular, 0, 3);
    expect(layout.cards.filter((card) => card.node.id === 'root')).toHaveLength(1);
  });
});

describe('lineage chart metrics', () => {
  const styles = (values: Record<string, string>) =>
    ({ getPropertyValue: (name: string) => values[name] ?? '' }) as unknown as CSSStyleDeclaration;

  it('reads the sizes the stylesheet publishes', () => {
    expect(
      metricsOf(styles({ '--fms-lineage-card-w': '200px', '--fms-lineage-gap-x': '12px' })),
    ).toEqual({ ...DEFAULT_LINEAGE_METRICS, cardWidth: 200, gapX: 12 });
  });

  it('falls back rather than laying the chart out against NaN', () => {
    expect(metricsOf(styles({}))).toEqual(DEFAULT_LINEAGE_METRICS);
    expect(metricsOf(styles({ '--fms-lineage-card-w': '', '--fms-lineage-card-h': '0px' }))).toEqual(
      DEFAULT_LINEAGE_METRICS,
    );
  });
});
