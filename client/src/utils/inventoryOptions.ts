/**
 * Canonical inventory units. The field remains a string, so old values that are not in
 * this list still round-trip; the picker simply offers the common ones first.
 */
export const INVENTORY_UNITS: string[] = [
  'Piece',
  'Roll',
  'Box',
  'Bag',
  'Bottle',
  'Litre',
  'Millilitre',
  'Kilogram',
  'Gram',
  'Metre',
  'Pack',
  'Set',
];

/**
 * Preset reasons for an inventory stock adjustment. `ReasonSelect` adds the "Other"
 * path, so a farm-specific reason is still possible without a list migration.
 */
export const INVENTORY_ADJUSTMENT_REASONS: { value: string; label: string }[] = [
  { value: 'Damaged', label: 'Damaged' },
  { value: 'Expired', label: 'Expired' },
  { value: 'Lost', label: 'Lost' },
  { value: 'Theft', label: 'Theft' },
  { value: 'Stock count correction', label: 'Stock count correction' },
  { value: 'Returned to supplier', label: 'Returned to supplier' },
  { value: 'Used for maintenance', label: 'Used for maintenance' },
  { value: 'other', label: 'Other' },
];
