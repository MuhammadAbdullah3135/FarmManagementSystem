import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import MedicalRecordsPage from './MedicalRecordsPage';
import { medicalRecordsApi, medicinesApi } from '../../api/health';
import { lookupsApi } from '../../api/attendance';
import { suggestionsApi } from '../../api/suggestions';

vi.mock('../../api/health', () => ({
  medicalRecordsApi: {
    list: vi.fn(),
    get: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    remove: vi.fn(),
  },
  medicinesApi: { list: vi.fn() },
}));

vi.mock('../../api/attendance', () => ({
  lookupsApi: { animals: vi.fn() },
}));

vi.mock('../../api/suggestions', () => ({
  suggestionsApi: { list: vi.fn() },
}));

const res = (data: unknown) => ({ data }) as never;
const formItem = (modal: HTMLElement, label: string) =>
  modal.querySelector(`.ant-form-item-label label[title="${label}"]`)?.closest('.ant-form-item') as HTMLElement;

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(medicalRecordsApi.list).mockResolvedValue(res({ items: [], totalCount: 0 }));
  vi.mocked(medicalRecordsApi.create).mockResolvedValue(res({ id: 'mr-1' }));
  vi.mocked(lookupsApi.animals).mockResolvedValue(
    res({ items: [{ id: 'a-1', tagNumber: 'C-001', name: 'Bella' }], totalCount: 1 }),
  );
  vi.mocked(medicinesApi.list).mockResolvedValue(
    res({
      items: [{ id: 'm-1', name: 'Ivermectin', unit: 'ml', totalQuantity: 10, batchCount: 1 }],
      totalCount: 1,
    }),
  );
  vi.mocked(suggestionsApi.list).mockImplementation(async (field) =>
    res(field === 'medicalDiagnosis' ? ['Mastitis'] : ['10 ml']) as never,
  );
});

describe('MedicalRecordsPage medicine picker', () => {
  it('loads medicines, stores the selected name, and sends it on save', async () => {
    const user = userEvent.setup();
    render(<MemoryRouter><MedicalRecordsPage /></MemoryRouter>);

    await user.click(await screen.findByRole('button', { name: /New Record/i }));
    const modal = (
      await screen.findByText('New Medical Record', { selector: '.ant-modal-title' })
    ).closest('.ant-modal') as HTMLElement;

    // The picker asks the medicines endpoint for the farm's list.
    expect(medicinesApi.list).toHaveBeenCalledWith({ pageSize: 100 });

    const animal = formItem(modal, 'Animal');
    await user.click(within(animal).getByRole('combobox'));
    await user.click(await screen.findByText('C-001 - Bella', { selector: '.ant-select-item-option-content' }));

    const symptoms = formItem(modal, 'Symptoms');
    await user.type(within(symptoms).getByRole('textbox'), 'Limping');

    const medicine = formItem(modal, 'Medicine Used');
    await user.click(within(medicine).getByRole('combobox'));
    await user.click(await screen.findByText('Ivermectin', { selector: '.ant-select-item-option-content' }));

    await user.click(within(modal).getByRole('button', { name: 'OK' }));

    expect(medicalRecordsApi.create).toHaveBeenCalledWith(
      expect.objectContaining({ medicineUsed: 'Ivermectin', symptoms: 'Limping' }),
    );
  }, 30000);

  it('loads diagnosis and dosage suggestions, stores the chosen values, and sends them on save', async () => {
    const user = userEvent.setup();
    render(<MemoryRouter><MedicalRecordsPage /></MemoryRouter>);

    await user.click(await screen.findByRole('button', { name: /New Record/i }));
    const modal = (
      await screen.findByText('New Medical Record', { selector: '.ant-modal-title' })
    ).closest('.ant-modal') as HTMLElement;

    const animal = formItem(modal, 'Animal');
    await user.click(within(animal).getByRole('combobox'));
    await user.click(await screen.findByText('C-001 - Bella', { selector: '.ant-select-item-option-content' }));

    const symptoms = formItem(modal, 'Symptoms');
    await user.type(within(symptoms).getByRole('textbox'), 'Swollen udder');

    expect(suggestionsApi.list).toHaveBeenCalledWith('medicalDiagnosis');
    expect(suggestionsApi.list).toHaveBeenCalledWith('medicalDosage');

    const diagnosis = formItem(modal, 'Diagnosis');
    await user.click(within(diagnosis).getByRole('combobox'));
    await user.click(await screen.findByText('Mastitis', { selector: '.ant-select-item-option-content' }));

    const dosage = formItem(modal, 'Dosage');
    await user.click(within(dosage).getByRole('combobox'));
    await user.click(await screen.findByText('10 ml', { selector: '.ant-select-item-option-content' }));

    await user.click(within(modal).getByRole('button', { name: 'OK' }));

    expect(medicalRecordsApi.create).toHaveBeenCalledWith(
      expect.objectContaining({ diagnosis: 'Mastitis', dosage: '10 ml', symptoms: 'Swollen udder' }),
    );
  }, 30000);
});
