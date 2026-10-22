import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import FeedingSchedulesPage from './FeedingSchedulesPage';
import { feedingSchedulesApi, dietPlansApi } from '../../api/feed';

vi.mock('../../api/feed', () => ({
  feedingSchedulesApi: {
    list: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    remove: vi.fn(),
  },
  feedingTasksApi: { generate: vi.fn() },
  dietPlansApi: { list: vi.fn() },
}));

const res = (data: unknown) => ({ data }) as never;

const formItem = (modal: HTMLElement, label: string) =>
  modal.querySelector(`.ant-form-item-label label[title="${label}"]`)?.closest('.ant-form-item') as HTMLElement;

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(feedingSchedulesApi.list).mockResolvedValue(res([]));
  vi.mocked(feedingSchedulesApi.create).mockResolvedValue(res({ id: 'sch-1' }));
  vi.mocked(dietPlansApi.list).mockResolvedValue(res([{ id: 'dp-1', name: 'Starter ration', isActive: true }]));
});

describe('FeedingSchedulesPage time picker', () => {
  it('fills the HH:mm value from a Morning preset and sends that string', async () => {
    const user = userEvent.setup();
    render(<FeedingSchedulesPage />);

    await user.click(await screen.findByRole('button', { name: /Add Schedule/i }));
    const modal = (
      await screen.findByText('New Feeding Schedule', { selector: '.ant-modal-title' })
    ).closest('.ant-modal') as HTMLElement;

    const dietPlan = formItem(modal, 'Diet Plan');
    await user.click(within(dietPlan).getByRole('combobox'));
    await user.click(await screen.findByText('Starter ration', { selector: '.ant-select-item-option-content' }));

    const timeOfDay = formItem(modal, 'Time of Day');
    await user.click(within(timeOfDay).getByPlaceholderText('e.g. 07:00'));
    await user.click(await screen.findByText('Morning'));

    await user.click(within(modal).getByRole('button', { name: 'OK' }));

    expect(feedingSchedulesApi.create).toHaveBeenCalledWith(
      expect.objectContaining({ dietPlanId: 'dp-1', timeOfDay: '07:00' }),
    );
  }, 90000);

  it('stores a preset feeding label and sends it on create', async () => {
    const user = userEvent.setup();
    render(<FeedingSchedulesPage />);

    await user.click(await screen.findByRole('button', { name: /Add Schedule/i }));
    const modal = (
      await screen.findByText('New Feeding Schedule', { selector: '.ant-modal-title' })
    ).closest('.ant-modal') as HTMLElement;

    const dietPlan = formItem(modal, 'Diet Plan');
    await user.click(within(dietPlan).getByRole('combobox'));
    await user.click(await screen.findByText('Starter ration', { selector: '.ant-select-item-option-content' }));

    const timeOfDay = formItem(modal, 'Time of Day');
    await user.click(within(timeOfDay).getByPlaceholderText('e.g. 07:00'));
    await user.click(await screen.findByText('Morning'));

    const label = formItem(modal, 'Label');
    await user.click(within(label).getByRole('combobox'));
    await user.click(await screen.findByText('Morning feed', { selector: '.ant-select-item-option-content' }));

    await user.click(within(modal).getByRole('button', { name: 'OK' }));

    expect(feedingSchedulesApi.create).toHaveBeenCalledWith(
      expect.objectContaining({ dietPlanId: 'dp-1', timeOfDay: '07:00', label: 'Morning feed' }),
    );
  }, 90000);
});
