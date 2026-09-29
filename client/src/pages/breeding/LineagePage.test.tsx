import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { message } from 'antd';
import LineagePage from './LineagePage';
import { DEFAULT_LINEAGE_METRICS } from './lineageLayout';
import { lineageApi } from '../../api/breeding';
import { lookupsApi } from '../../api/attendance';
import type { LineageNode, LineageResponse } from '../../types';

vi.mock('../../api/breeding', () => ({ lineageApi: { get: vi.fn() } }));
vi.mock('../../api/attendance', () => ({ lookupsApi: { animals: vi.fn() } }));

const animal = (overrides: Partial<LineageNode> & Pick<LineageNode, 'id' | 'tagNumber'>): LineageNode => ({
  sex: 'Female',
  status: 'Active',
  isRoot: false,
  offspring: [],
  ...overrides,
});

/*
 * TAG-0058-2 ("Seed Calf B") is the record the farm actually holds: a female recorded as the
 * sire of TAG-0079, born a month before her. It is the fixture rather than a neutral pair of
 * parents because this view is the one place the mistake becomes visible, and it must show it
 * instead of refusing to draw the tree.
 */
const femaleSire = animal({
  id: 'sire',
  tagNumber: 'TAG-0058-2',
  name: 'Seed Calf B',
  sex: 'Female',
  breed: 'Rajanpuri',
  dateOfBirth: '2026-08-24',
});
const dam = animal({ id: 'dam', tagNumber: 'TAG-0100', name: 'Boocho Dam', sex: 'Female' });
const root = animal({
  id: 'root',
  tagNumber: 'TAG-0079',
  name: 'Boocho',
  isRoot: true,
  sex: 'Female',
  breed: 'Rajanpuri',
  dateOfBirth: '2026-09-29',
  sire: femaleSire,
  dam,
});

const response = (rootCard: LineageNode, ancestorDepth = 5, descendantDepth = 3): LineageResponse => ({
  root: rootCard,
  ancestorDepth,
  descendantDepth,
});

const cards = () => [...document.querySelectorAll<HTMLElement>('.fms-lineage-card')];
const cardFor = (tagNumber: string) => {
  const card = cards().find((element) => element.textContent?.includes(tagNumber));
  if (!card) throw new Error(`no card rendered for ${tagNumber}`);
  return card;
};

/** The positioned box a card sits in: the offsets belong to the slot, not to the card itself. */
const slotFor = (tagNumber: string) => cardFor(tagNumber).closest('.fms-lineage-card-slot') as HTMLElement;
const offset = (element: HTMLElement, property: string) =>
  parseFloat(element.style.getPropertyValue(property));
const links = () => [...document.querySelectorAll<HTMLElement>('.fms-lineage-link')];
const linksOfSide = (side: string) =>
  [...document.querySelectorAll(`.fms-lineage-link[data-side='${side}']`)];

/** antd v6 renders select options as hidden a11y mirrors; click the content element. */
const selectOption = (label: string) => {
  const content = [...document.querySelectorAll('.ant-select-item-option-content')].find(
    (element) => element.textContent === label,
  );
  expect(content).toBeTruthy();
  fireEvent.click(content!.closest('.ant-select-item-option') as HTMLElement);
};

const viewLineageOf = async (user: ReturnType<typeof userEvent.setup>, label: string) => {
  await user.click(screen.getByRole('combobox'));
  await waitFor(() => expect(document.querySelectorAll('.ant-select-item-option-content').length).toBeGreaterThan(0));
  selectOption(label);
  await user.click(screen.getByRole('button', { name: /View Lineage/ }));
};

beforeEach(() => {
  vi.mocked(lookupsApi.animals).mockResolvedValue({
    data: {
      items: [
        { id: 'root', tagNumber: 'TAG-0079', name: 'Boocho' },
        { id: 'sire', tagNumber: 'TAG-0058-2', name: 'Seed Calf B' },
      ],
    },
  } as never);
});

describe('LineagePage chart', () => {
  it('draws the root, both parents and their connectors', async () => {
    const user = userEvent.setup();
    vi.mocked(lineageApi.get).mockResolvedValue({ data: response(root) } as never);

    render(<LineagePage />);
    await viewLineageOf(user, 'TAG-0079 - Boocho');

    await waitFor(() => expect(cards()).toHaveLength(3));
    expect(cardFor('TAG-0079').textContent).toContain('Boocho');
    expect(cardFor('TAG-0079').textContent).toContain('ROOT');
    expect(screen.getByText('ROOT')).toBeInTheDocument();

    // The grandparents were never returned, so nothing at all is drawn above the parents: the
    // old chart hung a stub over every card whether or not a generation existed.
    expect(linksOfSide('sire')).toHaveLength(1);
    expect(linksOfSide('dam')).toHaveLength(1);
    expect(linksOfSide('root')).toHaveLength(2);
    expect(links()).toHaveLength(4);
  });

  it('renders the female recorded as a sire as recorded, in the sire slot', async () => {
    const user = userEvent.setup();
    vi.mocked(lineageApi.get).mockResolvedValue({ data: response(root) } as never);

    render(<LineagePage />);
    await viewLineageOf(user, 'TAG-0079 - Boocho');

    await waitFor(() => expect(cards()).toHaveLength(3));
    const sireCard = cardFor('TAG-0058-2');
    expect(sireCard.dataset.side).toBe('sire');
    expect(sireCard.textContent).toContain('Seed Calf B');
    expect(sireCard.textContent).toContain('Female');
    expect(sireCard.textContent).toContain('DOB');
  });

  it('draws offspring below the root, on the generation under it', async () => {
    const user = userEvent.setup();
    const calf = animal({ id: 'calf', tagNumber: 'TAG-0200', name: 'Calf One' });
    vi.mocked(lineageApi.get).mockResolvedValue({ data: response({ ...root, offspring: [calf] }) } as never);

    render(<LineagePage />);
    await viewLineageOf(user, 'TAG-0079 - Boocho');

    await waitFor(() => expect(cards()).toHaveLength(4));
    expect(linksOfSide('child')).toHaveLength(1);
    expect(cardFor('TAG-0200').dataset.side).toBe('child');
    // One row below the root and one row above it: both halves are in the same box, so the
    // root sits between its parents and its offspring rather than on top of either.
    const rootTop = offset(slotFor('TAG-0079'), 'top');
    expect(offset(slotFor('TAG-0200'), 'top')).toBeGreaterThan(rootTop);
    expect(offset(slotFor('TAG-0058-2'), 'top')).toBeLessThan(rootTop);
  });

  it('says so when nothing is recorded above or below instead of drawing an empty frame', async () => {
    const user = userEvent.setup();
    const lonely = animal({ id: 'root', tagNumber: 'TAG-0079', name: 'Boocho', isRoot: true });
    vi.mocked(lineageApi.get).mockResolvedValue({ data: response(lonely) } as never);

    render(<LineagePage />);
    await viewLineageOf(user, 'TAG-0079 - Boocho');

    expect(await screen.findByText('No ancestors recorded')).toBeInTheDocument();
    expect(screen.getByText('No offspring recorded')).toBeInTheDocument();
    expect(cards()).toHaveLength(1);
    expect(links()).toHaveLength(0);
  });

  it('opens the chart on the animal that was asked about', async () => {
    const user = userEvent.setup();
    const greatGrandSire = animal({ id: 'ggs', tagNumber: 'TAG-0001' });
    const deep = {
      ...root,
      sire: { ...femaleSire, sire: { ...animal({ id: 'gs', tagNumber: 'TAG-0002' }), sire: greatGrandSire } },
    };
    vi.mocked(lineageApi.get).mockResolvedValue({ data: response(deep, 5, 0) } as never);

    render(<LineagePage />);
    await viewLineageOf(user, 'TAG-0079 - Boocho');

    await waitFor(() => expect(cards()).toHaveLength(5));
    // Every offset is measured from the inline-start edge and none of them is negative, which
    // is what keeps the root inside a box that only scrolls forwards.
    const slots = [...document.querySelectorAll<HTMLElement>('.fms-lineage-card-slot')];
    for (const slot of slots) {
      expect(offset(slot, 'inset-inline-start')).toBeGreaterThanOrEqual(0);
    }
    // The root is nowhere near the inline-start edge of a four-generation chart, and the
    // farthest-away card still clears the chart's own padding rather than sitting on the edge.
    expect(offset(slotFor('TAG-0079'), 'inset-inline-start')).toBeGreaterThan(DEFAULT_LINEAGE_METRICS.padX);
    expect(offset(slotFor('TAG-0001'), 'inset-inline-start')).toBe(DEFAULT_LINEAGE_METRICS.padX);
  });
});

describe('LineagePage controls', () => {
  it('asks for the depths shown beside the button', async () => {
    const user = userEvent.setup();
    vi.mocked(lineageApi.get).mockResolvedValue({ data: response(root) } as never);

    render(<LineagePage />);
    await viewLineageOf(user, 'TAG-0079 - Boocho');
    expect(lineageApi.get).toHaveBeenCalledWith('root', 5, 3);

    const [ancestors] = screen.getAllByRole('spinbutton');
    await user.clear(ancestors);
    await user.type(ancestors, '2');
    await user.click(screen.getByRole('button', { name: /View Lineage/ }));
    expect(lineageApi.get).toHaveBeenLastCalledWith('root', 2, 3);
  });

  it('warns instead of asking for an animal that was not chosen', async () => {
    const user = userEvent.setup();
    const warn = vi.spyOn(message, 'warning').mockImplementation(() => undefined as never);

    render(<LineagePage />);
    await user.click(screen.getByRole('button', { name: /View Lineage/ }));

    expect(warn).toHaveBeenCalledWith('Select an animal first');
    expect(lineageApi.get).not.toHaveBeenCalled();
    warn.mockRestore();
  });

  it('reports a failed lookup and leaves the chart empty', async () => {
    const user = userEvent.setup();
    const fail = vi.spyOn(message, 'error').mockImplementation(() => undefined as never);
    vi.mocked(lineageApi.get).mockRejectedValue({ response: { status: 500, data: 'Lineage is unavailable' } });

    render(<LineagePage />);
    await viewLineageOf(user, 'TAG-0079 - Boocho');

    await waitFor(() => expect(fail).toHaveBeenCalledWith('Lineage is unavailable'));
    expect(cards()).toHaveLength(0);
    expect(screen.getByText(/Select an animal and click/)).toBeInTheDocument();
    fail.mockRestore();
  });
});
