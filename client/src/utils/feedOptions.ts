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

/**
 * Preset labels for a feeding schedule. The field stays a free-text string, so a
 * schedule can still be named something the farm has not used before.
 */
export const FEEDING_LABELS = [
  'Morning feed',
  'Midday feed',
  'Evening feed',
  'Night feed',
  'Supplement',
] as const;
