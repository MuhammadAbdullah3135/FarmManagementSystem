import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen, waitFor } from '@testing-library/react';
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

const printPortal = () => document.body.querySelector('.fms-print-sheet');

const dispatchPrint = (type: 'beforeprint' | 'afterprint') => {
  act(() => {
    window.dispatchEvent(new Event(type));
  });
};

beforeEach(() => {
  vi.clearAllMocks();
  vi.spyOn(window, 'print').mockImplementation(() => undefined);
  document.body.classList.remove('fms-labels-print');
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

  it('mounts a standalone copy under body only while the browser is printing', async () => {
    sheetOf([BESSIE, STAR]);

    render(<AnimalQrLabelsPage />);
    await screen.findByText('C-001');

    expect(printPortal()).toBeNull();
    dispatchPrint('beforeprint');

    // A second sheet, outside the viewport-height app shell that used to clip the print job.
    const portal = printPortal();
    expect(portal).not.toBeNull();
    expect(portal!.parentElement).toBe(document.body);
    expect(portal!.querySelectorAll('.fms-label')).toHaveLength(2);
    expect(portal!.querySelectorAll('svg')).toHaveLength(2);
    expect(portal!.textContent).toContain('C-001');
    expect(portal!.textContent).toContain('C-002');
    expect(document.body).toHaveClass('fms-labels-print');

    // The preview stays on screen while the dialog is open; hiding #root is print-only CSS.
    expect(document.querySelectorAll('.fms-label-sheet:not(.fms-print-sheet) .fms-label')).toHaveLength(2);
    expect(screen.getByRole('button', { name: /Print$/ })).toBeEnabled();

    dispatchPrint('afterprint');
    expect(document.body).not.toHaveClass('fms-labels-print');
    expect(printPortal()).toBeNull();
  });

  it('prepares the print sheet before opening the dialog', async () => {
    sheetOf([BESSIE]);

    render(<AnimalQrLabelsPage />);
    const printButton = await screen.findByRole('button', { name: /Print$/ });

    await userEvent.click(printButton);

    // The browser's own dialog is where paper size and a physical printer are chosen; the app
    // hands it a page rather than a generated file. The sheet is mounted before the dialog opens,
    // so even a browser that fires `beforeprint` late has something to print.
    expect(window.print).toHaveBeenCalledTimes(1);
    expect(document.body).toHaveClass('fms-labels-print');
    expect(printPortal()!.querySelectorAll('.fms-label')).toHaveLength(1);
  });

  it('does not hide the app when there is nothing to print', () => {
    render(<AnimalQrLabelsPage />);

    dispatchPrint('beforeprint');

    expect(document.body).not.toHaveClass('fms-labels-print');
    expect(printPortal()).toBeNull();
  });

  it('removes the print sheet and body class when leaving the page mid-print', async () => {
    sheetOf([BESSIE]);

    const { unmount } = render(<AnimalQrLabelsPage />);
    await screen.findByText('C-001');
    dispatchPrint('beforeprint');
    expect(printPortal()).not.toBeNull();

    unmount();

    expect(printPortal()).toBeNull();
    expect(document.body).not.toHaveClass('fms-labels-print');
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
