/**
 * Which animals a Sire or Dam field may offer.
 *
 * The rule is the API's rule, applied to the same two words, because a picker that offers a
 * pairing the server refuses is worse than no picker at all: the user fills the form and is
 * told no on save. A sire is male and a dam is female (see `AnimalService.CheckParentSex`),
 * and an animal is never its own parent.
 *
 * Two deliberate softenings, both mirroring the server:
 *
 *   - the words this app reads are "Male"/"Female", the pair a new farm is seeded with. A farm
 *     that renamed its sexes ("Buck"/"Doe") gets an *unfiltered* list rather than an empty one:
 *     the form cannot tell its animals apart, and an empty Sire dropdown would look like a
 *     broken screen. The server likewise leaves a value it cannot read alone.
 *   - the parent already on the record stays in the list even when it fails the rule. The farm
 *     holds one such record (a female recorded as the sire of TAG-0079 Boocho); it has to keep
 *     showing that parent by tag so the mistake can be seen and corrected, rather than
 *     rendering a raw id or silently dropping the value on save.
 */

/** The side of the pedigree a field is for, as the two words this app reads. */
export type RequiredSex = 'male' | 'female';

/** The two words this app can read, the pair a new farm is seeded with. */
const READABLE_SEXES = ['male', 'female'];

const readSex = (sexValue: string | undefined) => (sexValue ?? '').trim().toLowerCase();

/** True when a recorded sex value is one this app can read at all. */
export const isReadableSex = (sexValue: string | undefined): boolean =>
  READABLE_SEXES.includes(readSex(sexValue));

/** True when a recorded sex value is the one this field requires. */
export const matchesRequiredSex = (sexValue: string | undefined, required: RequiredSex): boolean =>
  readSex(sexValue) === required;

export interface AnimalWithSex {
  id: string;
  sexValue?: string;
}

export interface ParentPickerContext {
  /** The animal being edited. Never offered as its own parent. */
  editingId?: string;
  /** The parent already stored on the record, kept on the list even when it fails the rule. */
  selectedId?: string;
}

/**
 * The animals a parent field may offer, in option order: the stored parent first when it fails
 * the rule, then everyone else of that sex.
 *
 * Whether the list narrows is decided by the farm's *vocabulary*, not by whether a match happens
 * to exist. One readable sex in the herd means the field can tell its animals apart, so it does
 * — including when that leaves it empty (a herd with no male yet offers no sire, which is the
 * truth, and better than offering the cows). Only a farm whose sexes are all unreadable gets the
 * whole herd, because there the field genuinely cannot choose.
 */
export const parentCandidates = <T extends AnimalWithSex>(
  animals: readonly T[],
  required: RequiredSex,
  { editingId, selectedId }: ParentPickerContext = {},
): T[] => {
  const others = animals.filter((animal) => animal.id !== editingId);
  const narrowed = others.some((animal) => isReadableSex(animal.sexValue))
    ? others.filter((animal) => matchesRequiredSex(animal.sexValue, required))
    : others;

  const selected = others.find((animal) => animal.id === selectedId);
  if (!selected || narrowed.some((animal) => animal.id === selected.id)) return narrowed;

  return [selected, ...narrowed];
};
