/**
 * Turning a scanned code into an animal, without touching the network.
 *
 * <para>
 * This module is the client half of the QR contract whose other half is
 * `src/FMS.Application/Animal/AnimalQrCode.cs`: the route segment `/dashboard/animals/`, one
 * GUID after it, and nothing else. The two are asserted to agree by tests on both sides —
 * a printed label outlives a deployment, so the shape cannot be discovered at scan time.
 * </para>
 *
 * <para>
 * Nothing here fetches. A scanned code resolves against the animal rows the *device already
 * holds* — the `animals@lookup` collection 5.4 made the only source a work form may name an
 * animal from. That is what keeps 5.4's guarantee intact: there is no path on which a scan
 * invents an identity the server never confirmed for this farm, and a scan works in a pen
 * with no signal.
 * </para>
 */

/** The SPA route an animal's label points at. Mirrors `AnimalQrCode.RouteMarker`. */
export const ANIMAL_ROUTE_MARKER = '/dashboard/animals/';

/**
 * The parts of a cached animal row the scanner needs.
 *
 * Structural rather than imported from the API module on purpose: this file stays a pure
 * function over plain data, so it can be read (and tested) without the offline layer, the API
 * client or a store being involved.
 */
export interface ScannableAnimal {
  id: string;
  tagNumber: string;
  name?: string | null;
}

/**
 * What a scan resolved to.
 *
 * `notALabel` and `unknown` are deliberately different outcomes: the first is somebody
 * pointing the camera at a feed bag or a shipping label, the second is a label for an animal
 * this device cannot name. They need different words, and collapsing them would make the
 * offline case — "connect once and the code will work" — indistinguishable from a code that
 * will never work here.
 */
export type ScanOutcome =
  | { status: 'found'; animal: ScannableAnimal }
  | { status: 'notALabel' }
  | { status: 'unknown'; animalId: string };

/**
 * Reads the animal id out of a scanned string, or returns null for anything that is not one.
 *
 * Deliberately strict, and identical to the server's reader: the marker must be present and
 * exactly one segment must follow it, parseable as a GUID. A query string or fragment is
 * tolerated (a camera that appends one is not a different label) but a longer path is not —
 * `…/animals/{id}/weights` is a different address, and accepting it would mean guessing which
 * part was the identity.
 *
 * The origin is *not* compared: a label printed by the deployed app has to scan in a build
 * served from a different path, and a device is not the place to discover that a deployment
 * moved.
 */
export function parseAnimalScan(scanned: string | null | undefined): string | null {
  if (typeof scanned !== 'string') return null;

  let text = scanned.trim();
  if (!text) return null;

  const cut = text.search(/[?#]/);
  if (cut >= 0) text = text.slice(0, cut);

  const marker = text.toLowerCase().indexOf(ANIMAL_ROUTE_MARKER);
  if (marker < 0) return null;

  const idPart = text.slice(marker + ANIMAL_ROUTE_MARKER.length).replace(/^\/+|\/+$/g, '');
  if (!idPart || idPart.includes('/')) return null;

  // Case-insensitive and shape-checked like the server's `Guid.TryParse`: hex digits only,
  // in a GUID's groups. A near-miss (a typo'd letter, a short code) is not an animal.
  const match = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.exec(idPart);
  return match ? idPart.toLowerCase() : null;
}

/**
 * Resolves a scanned code against the animal rows on this device.
 *
 * Pure: the caller supplies what the device holds (see `lookupAnimalByIdentity` in
 * `useAnimalScan`), so resolution is the same function online and offline — the only thing
 * that differs is how complete that list is.
 */
export function resolveScan(scanned: string | null | undefined, animals: readonly ScannableAnimal[]): ScanOutcome {
  const animalId = parseAnimalScan(scanned);
  if (!animalId) return { status: 'notALabel' };

  // Another farm's label lands here too, and that is the point: the payload carries no farm,
  // so a stranger's code is compared against *this* farm's rows and matches nothing rather
  // than leading the reader across a farm boundary.
  const animal = animals.find((candidate) => candidate.id.toLowerCase() === animalId);
  return animal ? { status: 'found', animal } : { status: 'unknown', animalId };
}

/**
 * The cached identity of one animal, for a screen that cannot reach the API.
 *
 * This is the same lookup a scan performs, used the other way round: a detail page opened
 * offline can still say *which* animal it is, from the row the device holds, instead of
 * bouncing the user back to the list because the full record could not be fetched.
 */
export function cachedAnimalIdentity(
  animalId: string | null | undefined,
  animals: readonly ScannableAnimal[],
): ScannableAnimal | undefined {
  if (!animalId) return undefined;
  const wanted = animalId.toLowerCase();
  return animals.find((candidate) => candidate.id.toLowerCase() === wanted);
}

/** How an animal is written on screen: tag first, name when there is one. */
export function animalLabel(animal: ScannableAnimal): string {
  return animal.name ? `${animal.tagNumber} — ${animal.name}` : animal.tagNumber;
}
