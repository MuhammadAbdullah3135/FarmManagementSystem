import type { FeedCategory, FeedUnit } from '../types';

/**
 * Option lists for a feed type.
 *
 * These live outside `FeedTypesPage` because the feed-type quick-add spec
 * (see `components/lookupQuickAdd.ts`) needs the same choices as the canonical
 * Add Feed Type form — a feed type cannot be created without both, so the two
 * definitions must not drift apart.
 */
export const FEED_CATEGORIES: FeedCategory[] = [
  'Forage',
  'Concentrate',
  'Mineral',
  'Supplement',
  'Additive',
  'Other',
];

export const FEED_UNITS: FeedUnit[] = [
  'Kilogram',
  'Gram',
  'Ton',
  'Liter',
  'Bale',
  'Bag',
  'Other',
];
