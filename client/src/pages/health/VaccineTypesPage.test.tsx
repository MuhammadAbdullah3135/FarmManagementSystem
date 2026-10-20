import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import VaccineTypesPage from './VaccineTypesPage';
import { vaccineTypesApi, medicinesApi } from '../../api/health';

vi.mock('../../api/health', () => ({
  vaccineTypesApi: {
    list: vi.fn(),
    get: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    remove: vi.fn(),
  },
  medicinesApi: { list: vi.fn() },
}));

const res = (data: unknown) => ({ data }) as never;
const formItem = (modal: HTMLElement, label: string) =>
  modal.querySelector(`.ant-form-item-label label[title="${label}"]`)?.closest('.ant-form-item') as HTMLElement;

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(vaccineTypesApi.list).mockResolvedValue(
    res({
      items: [{ id: 'vt-1', name: 'FMD Vaccine', defaultDosage: '5ml', vaccinationCount: 0, scheduleCount: 0 }],
      totalCount: 1,
    }) as never,
  );
  vi.mocked(vaccineTypesApi.create).mockResolvedValue(res({ id: 'vt-2' }) as never);
  vi.mocked(medicinesApi.list).mockResolvedValue(
    res({
      items: [{ id: 'm-1', name: 'Ivermectin', unit: 'ml', totalQuantity: 10, batchCount: 1 }],
      totalCount: 1,
    }) as never,
  );
});

describe('VaccineTypesPage dosage picker', () => {
  it('offers existing dosages and a linked medicine dose, then saves the chosen value', async () => {
    const user = userEvent.setup();
    render(<MemoryRouter><VaccineTypesPage /></MemoryRouter>);

    await user.click(await screen.findByRole('button', { name: /Add Vaccine Type/i }));

    const modal = (
      await screen.findByText('Add Vaccine Type', { selector: '.ant-modal-title' })
    ).closest('.ant-modal') as HTMLElement;

    await user.type(within(formItem(modal, 'Name')).getByRole('textbox'), 'Rabies');

    const linkedMedicine = formItem(modal, 'Linked Medicine (optional)');
    await user.click(within(linkedMedicine).getByRole('combobox'));
    await user.click(await screen.findByText('Ivermectin', { selector: '.ant-select-item-option-content' }));

    const dosage = formItem(modal, 'Default Dosage');
    await user.click(within(dosage).getByRole('combobox'));
    expect(await screen.findByText('5ml', { selector: '.ant-select-item-option-content' })).toBeInTheDocument();
    await user.click(screen.getByText('1 ml', { selector: '.ant-select-item-option-content' }));

    await user.click(within(modal).getByRole('button', { name: 'OK' }));

    expect(vaccineTypesApi.create).toHaveBeenCalledWith(
      expect.objectContaining({ name: 'Rabies', defaultDosage: '1 ml', linkedMedicineId: 'm-1' }),
    );
  }, 30000);
});
