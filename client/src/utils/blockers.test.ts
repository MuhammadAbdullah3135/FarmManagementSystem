import { describe, expect, it } from 'vitest';
import { blockersOf } from './blockers';

/*
 * The toast is the one place a malformed body must not crash: every degenerate shape here
 * degrades to "no blockers" so the caller falls back to the plain sentence.
 */
const conflict = (body: unknown) => ({ response: { status: 409, data: body } });

const blocker = {
  id: '6f9619ff-8b86-d011-b42d-00c04fc964ff',
  kind: 'gestationRecord',
  label: '005 · expected 2026-11-30',
};

describe('blockersOf', () => {
  it('reads the blockers of a problem-detail conflict response', () => {
    expect(blockersOf(conflict({
      status: 409,
      title: 'Conflict',
      detail: 'Cannot delete breeding record …',
      blockers: [blocker],
    }))).toEqual([blocker]);
  });

  it('accepts an already-unwrapped body too', () => {
    expect(blockersOf({ blockers: [blocker] })).toEqual([blocker]);
  });

  it('reads none from the plain-string conflict every other endpoint answers', () => {
    expect(blockersOf(conflict('Breeding record not found'))).toEqual([]);
  });

  it('reads none when the refusal carries no blockers field', () => {
    expect(blockersOf(conflict({ status: 409, title: 'Conflict', detail: '…' }))).toEqual([]);
  });

  it('reads none when blockers is not an array', () => {
    expect(blockersOf(conflict({ detail: '…', blockers: { id: 'x' } }))).toEqual([]);
  });

  it('drops rows missing any of the three fields rather than half-rendering them', () => {
    const partial = blockersOf(conflict({
      blockers: [
        { id: 'a', kind: 'gestationRecord', label: 'ok' },
        { id: 'b', kind: 'gestationRecord' },
        { kind: 'gestationRecord', label: 'no id' },
        'a string',
        null,
      ],
    }));

    expect(partial).toEqual([{ id: 'a', kind: 'gestationRecord', label: 'ok' }]);
  });

  it('is safe on null, strings and numbers', () => {
    expect(blockersOf(null)).toEqual([]);
    expect(blockersOf('Conflict')).toEqual([]);
    expect(blockersOf(42)).toEqual([]);
  });
});
