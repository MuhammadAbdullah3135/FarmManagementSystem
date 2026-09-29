import { describe, expect, it } from 'vitest';
import { isReadableSex, matchesRequiredSex, parentCandidates } from './animalSex';

/*
 * The picker's rule has to be the API's rule: what the form offers is what the server accepts.
 * These are the cases the two layers are known to disagree about when the predicate drifts —
 * the breeding page's older `value.includes('male')` heuristic also matched "Female", and a
 * farm with its own sex vocabulary would have had every animal filtered out of the field.
 */
const animal = (id: string, sexValue?: string) => ({ id, sexValue });

describe('matchesRequiredSex', () => {
  it('reads the two words a farm is seeded with, ignoring case and padding', () => {
    expect(matchesRequiredSex('Male', 'male')).toBe(true);
    expect(matchesRequiredSex(' female ', 'female')).toBe(true);
    expect(matchesRequiredSex('FEMALE', 'female')).toBe(true);
  });

  it('does not let "Female" satisfy a male field', () => {
    // The substring trap: "Female" contains "Male".
    expect(matchesRequiredSex('Female', 'male')).toBe(false);
    expect(matchesRequiredSex('Male', 'female')).toBe(false);
  });

  it('reports no match for a vocabulary it cannot read', () => {
    expect(matchesRequiredSex('Buck', 'male')).toBe(false);
    expect(matchesRequiredSex(undefined, 'male')).toBe(false);
    expect(matchesRequiredSex('', 'female')).toBe(false);
  });
});

describe('isReadableSex', () => {
  it('reads the seeded pair and rejects everything else', () => {
    expect(isReadableSex('Male')).toBe(true);
    expect(isReadableSex(' female ')).toBe(true);
    expect(isReadableSex('Buck')).toBe(false);
    expect(isReadableSex(undefined)).toBe(false);
  });
});

describe('parentCandidates', () => {
  const herd = [
    animal('cow-1', 'Female'),
    animal('bull-1', 'Male'),
    animal('cow-2', 'Female'),
    animal('bull-2', 'Male'),
  ];

  it('offers only male animals to a sire field and only females to a dam field', () => {
    expect(parentCandidates(herd, 'male').map((a) => a.id)).toEqual(['bull-1', 'bull-2']);
    expect(parentCandidates(herd, 'female').map((a) => a.id)).toEqual(['cow-1', 'cow-2']);
  });

  it('never offers the animal being edited, so nothing can be its own parent', () => {
    expect(parentCandidates(herd, 'male', { editingId: 'bull-1' }).map((a) => a.id)).toEqual(['bull-2']);
    expect(parentCandidates(herd, 'female', { editingId: 'cow-2' }).map((a) => a.id)).toEqual(['cow-1']);
  });

  it('leaves the list whole when the farm names its sexes something this app cannot read', () => {
    const renamed = [animal('buck-1', 'Buck'), animal('doe-1', 'Doe'), animal('unknown-1')];

    // Not narrowed — and not emptied, which is what an exact-match filter would have done.
    expect(parentCandidates(renamed, 'male').map((a) => a.id)).toEqual(['buck-1', 'doe-1', 'unknown-1']);
  });

  it('still narrows as soon as one animal carries a readable sex', () => {
    const mixed = [animal('buck-1', 'Buck'), animal('bull-1', 'Male')];

    expect(parentCandidates(mixed, 'male').map((a) => a.id)).toEqual(['bull-1']);
  });

  it('offers nothing when the vocabulary is readable and no animal is of that sex', () => {
    // A herd of cows only: the sire field is empty rather than offering the cows, which is what
    // the server would refuse. The dam field still offers them.
    const cowsOnly = [animal('cow-1', 'Female'), animal('cow-2', 'Female')];

    expect(parentCandidates(cowsOnly, 'male')).toEqual([]);
    expect(parentCandidates(cowsOnly, 'female').map((a) => a.id)).toEqual(['cow-1', 'cow-2']);
  });

  it('keeps a stored parent that fails the rule, first in the list', () => {
    // The farm's real record: TAG-0058-2, a female, stored as 079 Boocho's sire.
    const options = parentCandidates(herd, 'male', { editingId: 'cow-1', selectedId: 'cow-2' });

    expect(options.map((a) => a.id)).toEqual(['cow-2', 'bull-1', 'bull-2']);
  });

  it('does not duplicate a stored parent that already satisfies the rule', () => {
    const options = parentCandidates(herd, 'male', { editingId: 'cow-1', selectedId: 'bull-1' });

    expect(options.map((a) => a.id)).toEqual(['bull-1', 'bull-2']);
  });

  it('never resurrects a stored parent that is the animal being edited', () => {
    // Both halves matter: the record cannot be offered as its own parent even though it is the
    // stored value, which is how the field would end up with nothing to display.
    expect(parentCandidates(herd, 'male', { editingId: 'cow-2', selectedId: 'cow-2' }).map((a) => a.id))
      .toEqual(['bull-1', 'bull-2']);
  });
});
