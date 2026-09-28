import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

vi.mock('../../api/animals', () => ({
  animalsApi: { qrLabels: vi.fn(), qrLabel: vi.fn(), get: vi.fn(), list: vi.fn() },
}));

import AnimalQrLabelsPage from './AnimalQrLabelsPage';
import { animalsApi } from '../../api/animals';
import { ANIMAL_ROUTE_MARKER } from '../../offline/scanResolve';

const label = (id: string, tagNumber: string, name?: string) => ({
  animalId: id,
  tagNumber,
  name: name ?? null,
  animalTypeName: 'Cattle',
  url: `https://farm.example.com${ANIMAL_ROUTE_MARKER}${id}`,
});

const BESSIE = label('6f9619ff-8b86-d011-b42d-00c04fc964ff', 'C-001', 'Bessie');
const STAR = label('11111111-2222-3333-4444-555555555555', 'C-002');

const sheetOf = (items: typeof BESSIE[]) =>
  vi.mocked(animalsApi.qrLabels).mockResolvedValue({
    data: { items, totalCount: items.length, page: 1, pageSize: 50 },
  } as never);

beforeEach(() => {
  vi.clearAllMocks();
  vi.spyOn(window, 'print').mockImplementation(() => undefined);
});

/**
 * The sheet is the printable half of the QR feature: physical tags, printed in batches, from
 * codes the *server* built — so what is on the label is what this deployment's scannable route
 * resolves, rather than something the browser assembled from whatever origin it happened to be
 * served from.
 */
describe('AnimalQrLabelsPage', () => {
  it('prints one label per animal, with the tag number readable without a scanner', async () => {
    sheetOf([BESSIE, STAR]);

    render(<AnimalQrLabelsPage />);

    expect(await screen.findByText('C-001')).toBeInTheDocument();
    expect(screen.getByText('Bessie')).toBeInTheDocument();
    expect(screen.getByText('C-002')).toBeInTheDocument();
    // The code itself is the server's payload, drawn as an SVG so it stays sharp on paper.
    expect(document.querySelectorAll('.fms-label-sheet svg')).toHaveLength(2);
  });

  it('asks the server for the filter and page size the user chose', async () => {
    sheetOf([]);

    render(<AnimalQrLabelsPage />);
    await waitFor(() => expect(animalsApi.qrLabels).toHaveBeenCalledTimes(1));

    // `{enter}` because this is a submitted search, not a filter that runs on every keystroke:
    // each character would otherwise be a server read of its own.
    await userEvent.type(screen.getByPlaceholderText('Search by tag or name'), 'COW{enter}');

    // The sheet is a server read, not a client-side filter: the same filter the animal list
    // answers, so the two cannot disagree about which animals exist.
    await waitFor(() =>
      expect(animalsApi.qrLabels).toHaveBeenLastCalledWith(
        expect.objectContaining({ search: 'COW', page: 1 }),
      ),
    );
    expect(await screen.findByText('No animals match this filter.')).toBeInTheDocument();
  });

  it('prints the page it is showing', async () => {
    sheetOf([BESSIE]);

    render(<AnimalQrLabelsPage />);
    const printButton = await screen.findByRole('button', { name: /Print$/ });

    await userEvent.click(printButton);

    // The browser's own dialog is where paper size and a physical printer are chosen; the app
    // hands it a page rather than a generated file.
    expect(window.print).toHaveBeenCalledTimes(1);
  });

  it('says why it cannot build a sheet instead of drawing codes it cannot vouch for', async () => {
    vi.mocked(animalsApi.qrLabels).mockRejectedValue(new Error('network unreachable'));

    render(<AnimalQrLabelsPage />);

    expect(await screen.findByText('Printing labels needs a connection.')).toBeInTheDocument();
    // Nothing printable, and the print button is not offered for an empty sheet.
    expect(document.querySelectorAll('.fms-label-sheet .fms-label')).toHaveLength(0);
    expect(screen.getByRole('button', { name: /Print$/ })).toBeDisabled();
  });
});
