import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import GestationPage from './GestationPage';
import { gestationApi } from '../../api/breeding';
import { employeesApi } from '../../api/hr';
import type { GestationRecord } from '../../types';

vi.mock('../../api/breeding', () => ({
  gestationApi: {
    list: vi.fn(),
    getHealthChecks: vi.fn(),
    logHealthCheck: vi.fn(),
    confirm: vi.fn(),
    revert: vi.fn(),
  },
  breedingRecordsApi: { list: vi.fn() },
}));

vi.mock('../../api/hr', () => ({
  employeesApi: { list: vi.fn() },
}));

const res = (data: unknown) => ({ data }) as never;
const formItem = (container: HTMLElement, label: string) =>
  container.querySelector(`.ant-form-item-label label[title="${label}"]`)?.closest('.ant-form-item') as HTMLElement;

const gestation = {
  id: 'g-1',
  farmId: 'farm-1',
  breedingRecordId: 'br-1',
  animalId: 'a-1',
  animalTagNumber: 'COW-1',
  animalName: 'Bella',
  sireId: 's-1',
  sireTagNumber: 'BULL-1',
  breedingDate: '2026-05-01T00:00:00Z',
  expectedDeliveryDate: '2027-02-01T00:00:00Z',
  daysUntilDue: 100,
  daysElapsed: 50,
  currentStage: 1,
  healthCheckCount: 0,
  createdAt: '2026-05-01T00:00:00Z',
} as GestationRecord;

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(gestationApi.list).mockResolvedValue(res({ items: [gestation], totalCount: 1 }) as never);
  vi.mocked(gestationApi.getHealthChecks).mockResolvedValue(res([]) as never);
  vi.mocked(gestationApi.logHealthCheck).mockResolvedValue(res({ id: 'hc-1' }) as never);
  vi.mocked(employeesApi.list).mockResolvedValue(
    res({ items: [{ id: 'e-1', firstName: 'Amina', lastName: 'Khan' }], totalCount: 1 }) as never,
  );
});

describe('GestationPage performed-by picker', () => {
  it('loads employees, stores the selected name, and sends it with the health check', async () => {
    const user = userEvent.setup();
    render(<MemoryRouter><GestationPage /></MemoryRouter>);

    await user.click(await screen.findByRole('button', { name: /health/i }));

    expect(employeesApi.list).toHaveBeenCalledWith({ page: 1, pageSize: 100 });
    const drawer = (await screen.findByText('Health Checks - COW-1')).closest('.ant-drawer') as HTMLElement;

    const performedBy = formItem(drawer, 'Performed By');
    await user.click(within(performedBy).getByRole('combobox'));
    await user.click(await screen.findByText('Amina Khan', { selector: '.ant-select-item-option-content' }));

    const checkDate = formItem(drawer, 'Check Date').querySelector('input') as HTMLInputElement;
    await user.click(checkDate);
    await user.type(checkDate, '2026-10-20');
    await user.keyboard('{Enter}');

    await user.click(within(drawer).getByRole('button', { name: /Log Check/i }));

    expect(gestationApi.logHealthCheck).toHaveBeenCalledWith(
      'g-1',
      expect.objectContaining({ performedBy: 'Amina Khan' }),
    );
  }, 30000);
});
