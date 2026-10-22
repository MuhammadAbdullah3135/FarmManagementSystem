import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import DietPlansPage from './DietPlansPage';
import { dietPlansApi, feedTypesApi } from '../../api/feed';
import { lookupsApi } from '../../api/attendance';
import type { DietPlan, FeedType } from '../../types';

vi.mock('../../api/feed', () => ({
  dietPlansApi: {
    list: vi.fn(),
    update: vi.fn(),
    create: vi.fn(),
    remove: vi.fn(),
    addItem: vi.fn(),
    removeItem: vi.fn(),
  },
  feedTypesApi: { list: vi.fn() },
}));

vi.mock('../../api/attendance', () => ({
  lookupsApi: {
    animalTypes: vi.fn(),
    ageCategories: vi.fn(),
  },
}));

const feedTypes: FeedType[] = [
  {
    id: 'hay',
    name: 'Hay',
    category: 'Forage',
    categoryName: 'Forage',
    unit: 'Kilogram',
    unitName: 'Kilogram',
    costPerUnit: 5,
  },
  {
    id: 'concentrate',
    name: 'Concentrate',
    category: 'Concentrate',
    categoryName: 'Concentrate',
    unit: 'Kilogram',
    unitName: 'Kilogram',
    costPerUnit: 10,
  },
];

const plan: DietPlan = {
  id: 'plan-1',
  name: 'Beef ration',
  isActive: true,
  items: [
    { id: 'item-1', feedTypeId: 'hay', feedTypeName: 'Hay', unitName: 'Kilogram', quantityPerFeeding: 2 },
    {
      id: 'item-2',
      feedTypeId: 'concentrate',
      feedTypeName: 'Concentrate',
      unitName: 'Kilogram',
      quantityPerFeeding: 1,
    },
  ],
  scheduleCount: 0,
};

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(dietPlansApi.list).mockResolvedValue({ data: [plan] } as never);
  vi.mocked(feedTypesApi.list).mockResolvedValue({ data: feedTypes } as never);
  vi.mocked(lookupsApi.animalTypes).mockResolvedValue({ data: [] } as never);
  vi.mocked(lookupsApi.ageCategories).mockResolvedValue({ data: [] } as never);
});

describe('DietPlansPage ration builder', () => {
  it('edits the ration inline and shows shares, cost, and weight', async () => {
    const user = userEvent.setup();
    render(<DietPlansPage />);

    await user.click(await screen.findByRole('button', { name: 'Edit' }));

    // The old flow opened a separate "Diet Items" drawer; the rows now live in the modal.
    expect(screen.getByText('Edit Diet Plan')).toBeInTheDocument();
    expect(screen.queryByText(/Diet Items/)).not.toBeInTheDocument();

    await waitFor(() => expect(screen.getByText('Hay (Kilogram)')).toBeInTheDocument());
    expect(screen.getByText('Concentrate (Kilogram)')).toBeInTheDocument();

    // 2 kg at 5 + 1 kg at 10 = 20, and the two rows carry 100% of the ration.
    const statistic = (title: string) =>
      screen.getByText(title).closest('.ant-statistic') as HTMLElement;
    expect(statistic('Total weight (kg)')).toHaveTextContent('3.00');
    expect(statistic('Total cost')).toHaveTextContent('$20.00');
    expect(statistic('Total share')).toHaveTextContent('100.0%');
  }, 20_000);

  it('refuses to save a row that has no feed or quantity', async () => {
    const user = userEvent.setup();
    render(<DietPlansPage />);

    await user.click(await screen.findByRole('button', { name: 'Edit' }));
    await user.click(screen.getByRole('button', { name: /Add feed/i }));

    expect(
      await screen.findByText('Choose a feed and a positive quantity for every row.'),
    ).toBeInTheDocument();
    const footer = screen.getByRole('button', { name: /^OK$/i });
    expect(footer).toBeDisabled();
  }, 20_000);
});
