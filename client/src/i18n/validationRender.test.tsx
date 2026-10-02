import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import i18n, { NAMESPACES, resources } from './index';
import { applyDocumentLocale } from './locale';
import { flatten } from '../../tools/i18n/check-locale-coverage.mjs';
import { inventoryApi } from '../api/inventory';
import { useAuthStore } from '../stores/authStore';
import { useFarmStore } from '../stores/farmStore';
import LoginPage from '../pages/LoginPage';
import InventoryItemsPage from '../pages/inventory/InventoryItemsPage';
import type { Farm } from '../types';

vi.mock('../api/inventory', () => ({
  inventoryApi: {
    list: vi.fn(),
    get: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    remove: vi.fn(),
    movements: vi.fn(),
    recordMovement: vi.fn(),
    report: vi.fn(),
  },
}));

vi.mock('../api/farmApi', () => ({ getApiError: vi.fn((error: unknown) => String(error)) }));

/**
 * Real pages, rendered in Spanish and Arabic, pinning the copy item 12 moved into
 * locale keys: form validation messages and empty states.
 *
 * Why this file exists when `localeCoverage.test.ts` already proves every key
 * resolves: coverage proves the *bundle* is complete. It cannot see whether a page asks
 * for the right key, and these are the two strings a user is guaranteed to read — an
 * empty table's "no items yet" is the first thing a new account sees, and a validation
 * message is the last thing a mistyped form says. Both are rendered here from the real
 * components, with only their API modules mocked.
 *
 * The claims per render:
 *   1. **No leak** — the reader's wording is on screen, the English one is not;
 *   2. **No raw key** — the generic scan every render test does;
 *   3. **Not a cognate** — where a locale's string legitimately equals the English, the
 *      second half is skipped rather than passing on a coincidence.
 */

type Locale = 'en' | 'es' | 'ar';
type FlatBundle = Record<string, string>;

const flat = (locale: Locale, namespace: string): FlatBundle =>
  flatten((resources as unknown as Record<string, Record<string, unknown>>)[locale][namespace]);

const wording = (locale: Locale, namespace: string, key: string): string =>
  flat(locale, namespace)[key];

const KEY_IDS: string[] = NAMESPACES.flatMap((namespace) =>
  Object.keys(flat('en', namespace)).map((key) => `${namespace}:${key}`),
);

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
  const leaked = textNodes(container).filter((text) => KEY_IDS.some((id) => text.includes(id)));
  expect(leaked).toEqual([]);
};

const expectTranslatedNotEnglish = (locale: Locale, namespace: string, key: string) => {
  const translated = wording(locale, namespace, key);
  expect(translated).toBeTruthy();
  expect(screen.queryAllByText(translated).length).toBeGreaterThan(0);

  const untranslated = wording('en', namespace, key);
  if (untranslated !== translated) expect(screen.queryByText(untranslated)).toBeNull();
};

const farm: Farm = {
  id: 'farm-a',
  name: 'Green Acres',
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  userFarmRole: 'SystemOwner',
};

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
  useAuthStore.setState({ user: null, isAuthenticated: false, error: null, isLoading: false });
  useFarmStore.setState({ farms: [farm], activeFarm: farm, isLoading: false, error: null });
  vi.mocked(inventoryApi.list).mockResolvedValue({ data: { items: [], totalCount: 0 } } as never);
});

afterEach(async () => {
  // The next test file must not inherit a non-English i18next instance.
  applyDocumentLocale('en');
  await i18n.changeLanguage('en');
});

describe('the login form in a translated language', () => {
  it("answers an empty submit with the reader's own copy in Spanish", async () => {
    await i18n.changeLanguage('es');
    const { container } = render(
      <MemoryRouter>
        <LoginPage />
      </MemoryRouter>,
    );

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: wording('es', 'auth', 'signIn2') }));

    // Both rule messages come from validation:common — the group item 12 introduced.
    expect(
      await screen.findByText(wording('es', 'validation', 'common.emailInvalid')),
    ).toBeInTheDocument();
    expect(screen.getByText(wording('es', 'validation', 'common.passwordRequired'))).toBeInTheDocument();
    expect(screen.queryByText('Please enter a valid email')).toBeNull();
    expect(screen.queryByText('Please enter your password')).toBeNull();

    expectNoRawKeys(container);
  });

  it('answers an empty submit in Arabic, with the document mirrored', async () => {
    await i18n.changeLanguage('ar');
    applyDocumentLocale('ar');
    const { container } = render(
      <MemoryRouter>
        <LoginPage />
      </MemoryRouter>,
    );

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: wording('ar', 'auth', 'signIn2') }));

    expect(
      await screen.findByText(wording('ar', 'validation', 'common.emailInvalid')),
    ).toBeInTheDocument();
    expect(screen.getByText(wording('ar', 'validation', 'common.passwordRequired'))).toBeInTheDocument();
    expect(document.documentElement.dir).toBe('rtl');

    expectNoRawKeys(container);
  });
});

describe('the inventory items page in a translated language', () => {
  const openAddModal = async (locale: Locale) => {
    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <InventoryItemsPage />
      </MemoryRouter>,
    );
    // A regex, not the bare string: antd's icons contribute their own aria-label, so
    // the button's accessible name is "<icon> Add item".
    await user.click(
      await screen.findByRole('button', { name: new RegExp(wording(locale, 'inventory', 'addItem')) }),
    );
    return (
      await screen.findByText(wording(locale, 'inventory', 'addInventoryItem'), {
        selector: '.ant-modal-title',
      })
    ).closest('.ant-modal') as HTMLElement;
  };

  /** The page's modal sets no okText, so its confirm button carries antd's own label. */
  const confirm = (modal: HTMLElement) => {
    const button = modal.querySelector('.ant-modal-footer .ant-btn-primary');
    expect(button).toBeTruthy();
    return button as HTMLElement;
  };

  it('shows a new account the Spanish empty state instead of an English table', async () => {
    await i18n.changeLanguage('es');
    const { container } = render(
      <MemoryRouter>
        <InventoryItemsPage />
      </MemoryRouter>,
    );

    expect(
      await screen.findByText(wording('es', 'inventory', 'noInventoryItemsYet')),
    ).toBeInTheDocument();
    expectTranslatedNotEnglish('es', 'inventory', 'inventoryItems');

    expectNoRawKeys(container);
  }, 30000);

  it('shows the Arabic empty state, and the add-item form validates in Arabic', async () => {
    await i18n.changeLanguage('ar');
    applyDocumentLocale('ar');

    const modal = await openAddModal('ar');
    expect(
      await screen.findByText(wording('ar', 'inventory', 'noInventoryItemsYet')),
    ).toBeInTheDocument();

    // Submitting the item form empty raises the item's own required messages.
    await userEvent.setup().click(confirm(modal));
    expect(
      await screen.findByText(wording('ar', 'validation', 'common.itemNameRequired')),
    ).toBeInTheDocument();
    expect(
      within(modal).getByText(wording('ar', 'validation', 'common.unitRequired')),
    ).toBeInTheDocument();

    // Arabic's "name is required" and "item name is required" are two different
    // sentences, so this pins that the *item* form asks for the item-flavoured one.
    expect(screen.queryByText(wording('ar', 'validation', 'common.nameRequired'))).toBeNull();
    expect(document.documentElement.dir).toBe('rtl');
  }, 30000);

  it('raises the Spanish required messages on the add-item form', async () => {
    await i18n.changeLanguage('es');
    const modal = await openAddModal('es');

    await userEvent.setup().click(confirm(modal));
    expect(
      await screen.findByText(wording('es', 'validation', 'common.itemNameRequired')),
    ).toBeInTheDocument();
    expect(
      within(modal).getByText(wording('es', 'validation', 'common.unitRequired')),
    ).toBeInTheDocument();
    expect(screen.queryByText('Item name is required')).toBeNull();
  }, 30000);
});

describe('the shared validation group', () => {
  const SRC = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

  it('has no key no page asks for, so none can rot unnoticed', () => {
    // The other `validation:*` groups are addressed by the *server* through
    // `serverMessage` — `validation.employee.firstNameMaxLength` — so they never appear
    // as a `t()` call and are excluded here. `common` is the group this app renders
    // itself, so each of its keys must have a call site. A guard over the whole
    // namespace would report ~40 keys as dead that are in fact perfectly alive.
    const files: string[] = [];
    const walk = (dir: string) => {
      for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        const p = path.join(dir, entry.name);
        if (entry.isDirectory()) walk(p);
        else if (/\.tsx?$/.test(p) && !/\.test\.tsx?$/.test(p)) files.push(p);
      }
    };
    walk(SRC);

    // Comments mention `t('…')` as prose; none of them is a usage.
    const source = files
      .map((file) =>
        fs
          .readFileSync(file, 'utf8')
          .replace(/\/\*[\s\S]*?\*\//g, '')
          .replace(/(^|\s)\/\/[^\n]*/g, '$1'),
      )
      .join('\n');

    // `flatten` collapses nesting into dotted keys, so the group arrives as `common.x`.
    const declared = Object.keys(flat('en', 'validation'))
      .filter((key) => key.startsWith('common.'))
      .map((key) => key.slice('common.'.length));
    // A call site is `t('validation:common.someKey')`, so the key is terminated by a
    // quote. Matching on the terminator rather than with a regex keeps `required` from
    // counting as used by some longer key that merely ends in it.
    const orphans = declared.filter(
      (key) =>
        !["'", '"', '`'].some((quote) => source.includes(`validation:common.${key}${quote}`)),
    );

    expect(declared.length).toBeGreaterThan(30);
    expect(orphans).toEqual([]);
  });

  it('keeps the income form on the income group, which a render assertion cannot see', () => {
    // The three locales' expense/income description wording is byte-identical today, so
    // rendering the income form proves nothing: both groups put the same string on
    // screen. The bug this pins — an income field reading `validation:expense.…` — is
    // only visible in the source, and only becomes a user-visible one the moment a
    // translator gives the two sentences different wording.
    const incomes = fs.readFileSync(path.join(SRC, 'pages/finance/IncomesPage.tsx'), 'utf8');

    expect(incomes).toContain('validation:income.descriptionMaxLength');
    expect(incomes).not.toContain('validation:expense.descriptionMaxLength');
  });
});
