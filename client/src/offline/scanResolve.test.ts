import { describe, expect, it } from 'vitest';
import {
  ANIMAL_ROUTE_MARKER,
  animalLabel,
  cachedAnimalIdentity,
  parseAnimalScan,
  resolveScan,
  type ScannableAnimal,
} from './scanResolve';

const ANIMAL_ID = '6f9619ff-8b86-d011-b42d-00c04fc964ff';
const OTHER_ID = '11111111-2222-3333-4444-555555555555';

const BESSIE: ScannableAnimal = { id: ANIMAL_ID, tagNumber: 'C-001', name: 'Bessie' };

const payload = (id: string, origin = 'https://farm.example.com') =>
  `${origin}${ANIMAL_ROUTE_MARKER}${id}`;

/**
 * The reader half of the QR contract whose writer is `AnimalQrCode` on the server.
 *
 * The payload is asserted as an exact shape rather than "contains the id" because the code is
 * printed, glued to an animal and read months later by a camera that knows nothing about this
 * codebase: the shape *is* the interface. The server's `AnimalQrLabelTests` asserts the same
 * grammar, and the two suites are what keep a printed tag resolvable.
 */
describe('parseAnimalScan', () => {
  it('reads the animal out of the URL the server prints', () => {
    expect(parseAnimalScan(payload(ANIMAL_ID))).toBe(ANIMAL_ID);
  });

  it('reads the same route segment whatever origin it was printed from', () => {
    // A redeployment must not invalidate a tag that is already on an animal's ear.
    expect(parseAnimalScan(payload(ANIMAL_ID, 'https://old.example.com/'))).toBe(ANIMAL_ID);
    expect(parseAnimalScan(payload(ANIMAL_ID, 'http://192.168.1.20:3000'))).toBe(ANIMAL_ID);
  });

  it('tolerates what a camera or a scanner decorates a label with', () => {
    expect(parseAnimalScan(`  ${payload(ANIMAL_ID)}  `)).toBe(ANIMAL_ID);
    expect(parseAnimalScan(`${payload(ANIMAL_ID)}?utm_source=ear-tag`)).toBe(ANIMAL_ID);
    expect(parseAnimalScan(`${payload(ANIMAL_ID)}#notes`)).toBe(ANIMAL_ID);
    expect(parseAnimalScan(payload(ANIMAL_ID).toUpperCase())).toBe(ANIMAL_ID);
  });

  it('lower-cases the id, so comparing it with a cached row cannot be case-sensitive', () => {
    expect(parseAnimalScan(payload(ANIMAL_ID.toUpperCase()))).toBe(ANIMAL_ID);
  });

  it('refuses anything that is not one animal’s route', () => {
    const notALabel = [
      'C-001',
      'https://farm.example.com/dashboard',
      'https://farm.example.com/dashboard/animals/',
      'https://farm.example.com/dashboard/animals/not-a-guid',
      // A different address: accepting it would mean guessing which part was the identity.
      `${payload(ANIMAL_ID)}/weights`,
      'https://farm.example.com/api/farm/1/animals/6f9619ff-8b86-d011-b42d-00c04fc964ff',
      '',
      '   ',
    ];

    for (const scanned of notALabel) {
      expect(parseAnimalScan(scanned), scanned).toBeNull();
    }

    expect(parseAnimalScan(null)).toBeNull();
    expect(parseAnimalScan(undefined)).toBeNull();
  });

  it('uses the route segment the server builds its payload with', () => {
    // Deliberately a literal, mirroring the server's own assertion: a change on one side
    // without the other is a printed label that stops resolving.
    expect(ANIMAL_ROUTE_MARKER).toBe('/dashboard/animals/');
  });
});

describe('resolveScan', () => {
  it('finds the animal the device holds', () => {
    const outcome = resolveScan(payload(ANIMAL_ID), [BESSIE]);

    expect(outcome).toEqual({ status: 'found', animal: BESSIE });
  });

  it('matches the cached row whatever case the code carries', () => {
    const outcome = resolveScan(payload(ANIMAL_ID.toUpperCase()), [BESSIE]);

    expect(outcome.status).toBe('found');
  });

  it('separates “not a tag” from “not an animal on this farm”', () => {
    // The two need different words: one is a feed bag, the other is a tag that may simply not
    // be on this device yet.
    expect(resolveScan('https://example.com/promo', [BESSIE]).status).toBe('notALabel');
    expect(resolveScan(payload(OTHER_ID), [BESSIE])).toEqual({
      status: 'unknown',
      animalId: OTHER_ID,
    });
  });

  it('resolves nothing against an empty device', () => {
    // Offline with no cached lookup. A scan cannot invent an identity the server has not
    // confirmed for this farm — it fails, rather than opening something by guesswork.
    expect(resolveScan(payload(ANIMAL_ID), [])).toEqual({
      status: 'unknown',
      animalId: ANIMAL_ID,
    });
  });
});

describe('cachedAnimalIdentity', () => {
  it('names the animal this device holds, for a page that cannot reach the API', () => {
    expect(cachedAnimalIdentity(ANIMAL_ID, [BESSIE])).toBe(BESSIE);
    expect(cachedAnimalIdentity(ANIMAL_ID.toUpperCase(), [BESSIE])).toBe(BESSIE);
  });

  it('finds nothing for an id this scope does not hold', () => {
    expect(cachedAnimalIdentity(OTHER_ID, [BESSIE])).toBeUndefined();
    expect(cachedAnimalIdentity(null, [BESSIE])).toBeUndefined();
  });
});

describe('animalLabel', () => {
  it('writes the tag first, with the name when there is one', () => {
    expect(animalLabel(BESSIE)).toBe('C-001 — Bessie');
    expect(animalLabel({ id: OTHER_ID, tagNumber: 'C-002' })).toBe('C-002');
  });
});
