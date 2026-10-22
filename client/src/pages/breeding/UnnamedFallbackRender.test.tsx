import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const MISSING_KEY_PREFIX = '[i18n] missing translation key';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import i18n, { NAMESPACES } from '../../i18n/index';
import { applyDocumentLocale } from '../../i18n/locale';
import { useAuthStore } from '../../stores/authStore';
import { useFarmStore } from '../../stores/farmStore';
import { lookupsApi } from '../../api/attendance';
import BirthRecordingPage from './BirthRecordingPage';
import LineagePage from './LineagePage';
import type { LineageResponse } from '../../types';
vi.mock('../../api/attendance', () => ({
  lookupsApi: {
    animals: vi.fn(),
    sexOptions: vi.fn(() => Promise.resolve({ data: [] })),
  },
}));

vi.mock('../../api/breeding', () => ({
  birthsApi: {
    list: vi.fn(() => Promise.resolve({ data: { items: [], totalCount: 0 } })),
    delete: vi.fn(() => Promise.resolve({})),
    create: vi.fn(() => Promise.resolve({})),
  },
  gestationApi: {
    list: vi.fn(() => Promise.resolve({ data: { items: [] } })),
  },
  breedingRecordsApi: {
    list: vi.fn(() => Promise.resolve({ data: { items: [] } })),
  },
  lineageApi: {
    get: vi.fn(() =>
      Promise.resolve({
        data: {
          root: {
            id: 'root',
            tagNumber: 'TAG-0079',
            sex: 'Female',
            status: 'Active',
            isRoot: true,
            offspring: [],
          },
          ancestorDepth: 5,
          descendantDepth: 3,
        } as LineageResponse,
      }),
    ),
  },
}));

const EXPECTED = {
  es: 'Sin nombre',
  ar: 'بدون اسم',
  en: 'Unnamed',
} as const;

let KEY_IDS: string[] = [];

const recomputeKeyIds = () => {
  const resources = (i18n as unknown as { resources?: Record<string, Record<string, unknown>> }).resources;
  if (!resources) {
    KEY_IDS = [];
    return;
  }
  KEY_IDS = NAMESPACES.flatMap((namespace) =>
    Object.keys((resources as unknown as Record<string, Record<string, unknown>>)[namespace] ?? {}).map((key) => `${namespace}:${key}`),
  );
};

const textNodes = (container: HTMLElement): string[] => {
  const out: string[] = [];
  for (const element of container.querySelectorAll('*')) {
    for (const child of Array.from(element.childNodes)) {
      if (child.nodeType === Node.TEXT_NODE) {
        const text = (child.textContent ?? '').trim();
        if (text) out.push(text);
      }
    }
  }
  return out;
};

const expectNoRawKeys = (container: HTMLElement) => {
  const leaked = textNodes(container).filter((text) =>
    KEY_IDS.some((id) => text.includes(id)),
  );
  expect(leaked).toEqual([]);
};

const userState = {
  userId: 'user-1',
  accountId: 'account-1',
  email: 'owner@example.com',
  firstName: 'Owner',
  lastName: 'Person',
  roles: ['SystemOwner'],
};

const farm = {
  id: 'farm-a',
  name: 'Green Acres',
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  userFarmRole: 'SystemOwner',
};

let missingKeySpy: ReturnType<typeof vi.spyOn>;

beforeEach(() => {
  recomputeKeyIds();
  missingKeySpy = vi.spyOn(console, 'error').mockImplementation(() => {});
  localStorage.clear();
  useAuthStore.setState({ user: userState, isAuthenticated: true });
  useFarmStore.setState({
    farms: [farm],
    activeFarm: farm,
    isLoading: false,
    error: null,
  });
  window.history.pushState({}, '', '/dashboard/records/births');
  // Set up the animals mock to return two animals, one unnamed (name: '')
  vi.mocked(lookupsApi.animals).mockResolvedValue({
    data: {
      items: [
        { id: 'x', tagNumber: 'TAG-0001', name: '' },
        { id: 'y', tagNumber: 'TAG-0002', name: 'Doe' },
      ],
    },
  } as never);
  vi.mocked(lookupsApi.sexOptions).mockResolvedValue({
    data: [{ id: 'male', value: 'Male' }, { id: 'female', value: 'Female' }],
  } as never);
});

afterEach(async () => {
  await i18n.changeLanguage('en');
  applyDocumentLocale('en');
  expect(missingKeySpy).not.toHaveBeenCalledWith(
    expect.stringContaining(MISSING_KEY_PREFIX),
  );
  missingKeySpy.mockRestore();
});

const openSelectAndGetFirstOptionText = async () => {
  // The dam Select is the first combobox on the page. antd renders options into a portal,
  // so they live on document.body, not the test container. We take the first combobox
  // because there may be multiple (dam select + offspring sex selects).
  const comboboxes = await screen.findAllByRole('combobox');
  expect(comboboxes.length).toBeGreaterThan(0);
  const combobox = comboboxes[0];
  // Click to open the dropdown
  await userEvent.setup().click(combobox);
  // Wait for the dropdown to open and options to appear
  await waitFor(
    () => {
      const options = document.querySelectorAll<HTMLElement>('.ant-select-item-option-content');
      expect(options.length).toBeGreaterThan(0);
    },
    { timeout: 5000 },
  );
  const options = document.querySelectorAll<HTMLElement>('.ant-select-item-option-content');
  return options[0].textContent ?? '';
};

const openRecordBirthModal = async () => {
  // The button text is translated (Registrar parto / تسجيل ولادة / Record Birth),
  // so match by the plus icon label that antd always renders, then the visible text.
  const openButton = screen.getByRole('button', { name: /plus/i });
  await userEvent.setup().click(openButton);
  const dialog = await screen.findByRole('dialog');
  // Wait for the dialog to have substantial content (form rendered)
  await waitFor(
    () => {
      const dialogContent = dialog.textContent ?? '';
      // Dialog should have multiple form labels (at least 50 chars of content)
      expect(dialogContent.length).toBeGreaterThan(50);
    },
    { timeout: 10000 },
  );
  // The Select dropdown options only appear when a Select is clicked.
  // We don't wait for options here - that's done in openSelectAndGetFirstOptionText
};

describe('the unnamed animal fallback is translated, not the English literal', () => {
  it('renders the Spanish word and not the English one on the birth-recording form', async () => {
    await i18n.changeLanguage('es');
    const { container } = render(
      <MemoryRouter initialEntries={['/dashboard/records/births']}>
        <BirthRecordingPage />
      </MemoryRouter>,
    );

    await openRecordBirthModal();
    const firstOptionText = await openSelectAndGetFirstOptionText();

    expect(firstOptionText).toContain(EXPECTED.es);
    expect(firstOptionText).not.toContain(EXPECTED.en);
    expectNoRawKeys(container);
  }, 30000);

  it('renders the Arabic word and not the English one on the birth-recording form, with a mirrored document', async () => {
    await i18n.changeLanguage('ar');
    applyDocumentLocale('ar');
    const { container } = render(
      <MemoryRouter initialEntries={['/dashboard/records/births']}>
        <BirthRecordingPage />
      </MemoryRouter>,
    );

    await openRecordBirthModal();
    const firstOptionText = await openSelectAndGetFirstOptionText();

    expect(firstOptionText).toContain(EXPECTED.ar);
    expect(firstOptionText).not.toContain(EXPECTED.en);
    expect(document.documentElement.dir).toBe('rtl');
    expectNoRawKeys(container);
  }, 30000);

  it('renders the Spanish word and not the English one on the lineage picker', async () => {
    await i18n.changeLanguage('es');
    const { container } = render(
      <MemoryRouter initialEntries={['/lineage']}>
        <LineagePage />
      </MemoryRouter>,
    );

    const firstOptionText = await openSelectAndGetFirstOptionText();

    expect(firstOptionText).toContain(EXPECTED.es);
    expect(firstOptionText).not.toContain(EXPECTED.en);
    expectNoRawKeys(container);
  }, 60000);

  it('renders the Arabic word and not the English one on the lineage picker, with a mirrored document', async () => {
    await i18n.changeLanguage('ar');
    applyDocumentLocale('ar');
    const { container } = render(
      <MemoryRouter initialEntries={['/lineage']}>
        <LineagePage />
      </MemoryRouter>,
    );

    const firstOptionText = await openSelectAndGetFirstOptionText();

    expect(firstOptionText).toContain(EXPECTED.ar);
    expect(firstOptionText).not.toContain(EXPECTED.en);
    expect(document.documentElement.dir).toBe('rtl');
    expectNoRawKeys(container);
  }, 60000);
});
