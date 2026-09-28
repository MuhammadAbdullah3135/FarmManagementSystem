import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useParams } from 'react-router-dom';
import 'fake-indexeddb/auto';
import { IDBFactory } from 'fake-indexeddb';

/**
 * The camera is replaced by a button that hands the flow a code.
 *
 * Not a shortcut around the scanner: what a device's camera returns is not something this suite
 * can produce, and `QrScanner.test.tsx` covers the camera's own behaviour (no device, refused
 * permission). What is asserted here is everything downstream of "a code was read" — which is
 * the part that has to hold offline, across farms, and for codes that are not tags at all.
 */
const scanner = vi.hoisted(() => ({ payload: '' }));

vi.mock('./QrScanner', () => ({
  default: ({ onDecode, active }: { onDecode: (text: string) => void; active: boolean }) => (
    <button type="button" disabled={!active} onClick={() => onDecode(scanner.payload)}>
      stub-scan
    </button>
  ),
}));

vi.mock('../api/animals', () => ({
  animalsApi: { get: vi.fn(), qrLabel: vi.fn(), qrLabels: vi.fn(), list: vi.fn() },
}));
vi.mock('../api/attendance', () => ({
  lookupsApi: { animals: vi.fn() },
}));

import { AnimalScanButton } from './AnimalScanModal';
import { animalsApi } from '../api/animals';
import { lookupsApi } from '../api/attendance';
import { replaceCollection, resetOfflineDbConnection } from '../offline/db';
import { useOfflineStore } from '../offline/connectivity';
import { useAuthStore } from '../stores/authStore';
import { useFarmStore } from '../stores/farmStore';
import type { Farm, User } from '../types';

const ANIMAL_ID = '6f9619ff-8b86-d011-b42d-00c04fc964ff';
const STRANGER_ID = '11111111-2222-3333-4444-555555555555';
const BESSIE = { id: ANIMAL_ID, tagNumber: 'C-001', name: 'Bessie' };

const farm: Farm = {
  id: 'farm-a',
  name: 'Farm A',
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  userFarmRole: 'FarmManager',
};
const scope = { accountId: 'acct-1', farmId: farm.id };
const user: User = {
  userId: 'user-1',
  accountId: scope.accountId,
  email: 'owner@example.com',
  firstName: 'Owner',
  lastName: 'Person',
  roles: ['FarmManager'],
};

const payload = (id: string) => `https://farm.example.com/dashboard/animals/${id}`;

const DetailMarker = () => <div>animal page {useParams().id}</div>;

const renderButton = () =>
  render(
    <MemoryRouter initialEntries={['/dashboard/animals']}>
      <Routes>
        <Route
          path="/dashboard/animals"
          element={
            <div>
              animals list
              <AnimalScanButton />
            </div>
          }
        />
        <Route path="/dashboard/animals/:id" element={<DetailMarker />} />
      </Routes>
    </MemoryRouter>,
  );

// A regex, not the exact string: antd gives the icon its own `role="img"` label, so the
// button's accessible name is "qrcode Scan animal tag".
const SCAN_BUTTON = /Scan animal tag/;

const openScanner = async () => {
  await userEvent.click(screen.getByRole('button', { name: SCAN_BUTTON }));
  // Waiting for the stub means the cached lookup has been read: until it has, the dialog is in
  // its "nothing stored on this device" state and there is no scanner at all.
  return screen.findByRole('button', { name: 'stub-scan' });
};

const scan = async (code: string) => {
  scanner.payload = code;
  await userEvent.click(await openScanner());
};

beforeEach(async () => {
  (globalThis as { indexedDB: IDBFactory }).indexedDB = new IDBFactory();
  resetOfflineDbConnection();
  vi.clearAllMocks();
  scanner.payload = '';
  useAuthStore.setState({ user, isAuthenticated: true });
  useFarmStore.setState({ farms: [farm], activeFarm: farm, isLoading: false, error: null });
  useOfflineStore.setState({
    isOnline: false,
    storageAvailable: true,
    stats: { recordCount: 0, collectionCount: 0, lastSyncedAt: null },
  });
});

describe('scanning an animal offline', () => {
  it('opens the animal from the device’s own rows, without asking the server', async () => {
    await replaceCollection(scope, 'animals@lookup', [{ id: BESSIE.id, data: BESSIE }]);

    renderButton();
    await scan(payload(ANIMAL_ID));

    // The whole point of 5.4's cache-only lookup: a scanned tag resolves in a pen with no
    // signal, and it resolves to an animal the server has already confirmed for this farm.
    expect(await screen.findByText(`animal page ${ANIMAL_ID}`)).toBeInTheDocument();
    expect(animalsApi.get).not.toHaveBeenCalled();
    expect(lookupsApi.animals).not.toHaveBeenCalled();
  });

  it('refuses a tag that is not stored on this device, and stays open to try another', async () => {
    await replaceCollection(scope, 'animals@lookup', [{ id: BESSIE.id, data: BESSIE }]);

    renderButton();
    await scan(payload(STRANGER_ID));

    expect(
      await screen.findByText(
        'That tag is not stored on this device yet. Connect once so it can be downloaded, then scan again.',
      ),
    ).toBeInTheDocument();
    // Still on the list: nothing was opened, and the scanner is still offered.
    expect(screen.getByText('animals list')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'stub-scan' })).toBeInTheDocument();
    expect(animalsApi.get).not.toHaveBeenCalled();
  }, 20_000);

  it('says so — rather than scanning — when this device holds no animals at all', async () => {
    renderButton();
    await userEvent.click(screen.getByRole('button', { name: SCAN_BUTTON }));

    expect(
      await screen.findByText('No animals are stored on this device yet.'),
    ).toBeInTheDocument();
    // No camera starts for a scan that cannot resolve anything.
    expect(screen.queryByRole('button', { name: 'stub-scan' })).not.toBeInTheDocument();
  });
});

describe('scanning with a connection', () => {
  beforeEach(() => {
    useOfflineStore.setState({ isOnline: true });
  });

  it('verifies a tag the cache does not hold before opening it', async () => {
    // The device's lookup is a bounded first page: a farm with more animals than that must not
    // have its tags silently stop working. Online, the server decides.
    await replaceCollection(scope, 'animals@lookup', [{ id: BESSIE.id, data: BESSIE }]);
    vi.mocked(animalsApi.get).mockResolvedValue({ data: { id: STRANGER_ID } } as never);

    renderButton();
    await scan(payload(STRANGER_ID));

    expect(await screen.findByText(`animal page ${STRANGER_ID}`)).toBeInTheDocument();
    expect(animalsApi.get).toHaveBeenCalledWith(STRANGER_ID);
  }, 20_000);

  it('refuses another farm’s tag: the server scopes the read, so it is not found', async () => {
    await replaceCollection(scope, 'animals@lookup', [{ id: BESSIE.id, data: BESSIE }]);
    vi.mocked(animalsApi.get).mockRejectedValue({ response: { status: 404 } });

    renderButton();
    await scan(payload(STRANGER_ID));

    expect(
      await screen.findByText('No animal in this farm carries that tag.'),
    ).toBeInTheDocument();
    expect(screen.getByText('animals list')).toBeInTheDocument();
  }, 20_000);

  it('does not claim a tag is unknown when the request itself failed', async () => {
    await replaceCollection(scope, 'animals@lookup', [{ id: BESSIE.id, data: BESSIE }]);
    // A failure that is not the server saying "no such animal": a dropped request, a timeout, a
    // 500. Reporting those as "no animal in this farm carries that tag" would be a wrong answer
    // stated as a fact, and would send somebody to reprint a tag that is perfectly good.
    vi.mocked(animalsApi.get).mockRejectedValue(new Error('network unreachable'));

    renderButton();
    await scan(payload(STRANGER_ID));

    expect(
      await screen.findByText(
        'That tag could not be checked with the server. If you are offline, connect and try again.',
      ),
    ).toBeInTheDocument();
  }, 20_000);
});

describe('scanning something that is not a tag', () => {
  it('says what it read instead of failing silently', async () => {
    await replaceCollection(scope, 'animals@lookup', [{ id: BESSIE.id, data: BESSIE }]);

    renderButton();
    await scan('https://example.com/feed-bag-promo');

    expect(await screen.findByText('That code is not an animal tag.')).toBeInTheDocument();
    expect(screen.getByText('animals list')).toBeInTheDocument();
    await waitFor(() => expect(animalsApi.get).not.toHaveBeenCalled());
  });
});
