import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import DepartmentsRolesPage from './DepartmentsRolesPage';
import { departmentsApi, employeeRolesApi } from '../../api/hr';

vi.mock('../../api/hr', () => ({
  departmentsApi: { list: vi.fn(), create: vi.fn(), remove: vi.fn() },
  employeeRolesApi: { list: vi.fn(), create: vi.fn(), remove: vi.fn() },
}));

const DEPARTMENTS = [
  { id: 'd1', name: 'Dairy', description: 'Milking and herd care', employeeCount: 4 },
  { id: 'd2', name: 'Field', description: 'Crops and fodder', employeeCount: 6 },
];
const ROLES = [{ id: 'r1', name: 'Herdsman', description: 'Handles the herd', employeeCount: 3 }];

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(departmentsApi.list).mockResolvedValue({ data: DEPARTMENTS } as never);
  vi.mocked(employeeRolesApi.list).mockResolvedValue({ data: ROLES } as never);
});

/** Columns sized by a fixed `span={n}` keep their desktop width at every viewport. */
const fixedSpanColumns = (root: HTMLElement) =>
  [...root.querySelectorAll('.ant-col')].filter((col) =>
    [...col.classList].some((cls) => /^ant-col-\d+$/.test(cls)),
  );

describe('DepartmentsRolesPage mobile layout', () => {
  // Two `span={12}` cards side by side gave each table about 156px on a phone: the same squeeze
  // as finance/categories, so the same fix — one card per row below 768px.
  it('sizes both cards through a breakpoint instead of a fixed span', async () => {
    const { container } = render(<DepartmentsRolesPage />);
    await waitFor(() => expect(screen.getByText('Dairy')).toBeInTheDocument());

    expect(fixedSpanColumns(container)).toHaveLength(0);
    const cardColumns = [...container.querySelectorAll('.ant-col')];
    expect(cardColumns).toHaveLength(2);
    for (const col of cardColumns) {
      expect(col.classList.contains('ant-col-xs-24')).toBe(true);
      expect(col.classList.contains('ant-col-md-12')).toBe(true);
    }
  });

  // Every media query reads as unmatched (a phone) in the test setup, so Description — the widest
  // column — is the one that gives way; the employee count and Delete stay in view.
  it('drops only the description on a phone and keeps the count and actions', async () => {
    const { container } = render(<DepartmentsRolesPage />);
    await waitFor(() => expect(screen.getByText('Dairy')).toBeInTheDocument());

    // Plain DOM queries rather than role queries: the columns are what matters, and role lookups
    // drag testing-library's accessible-name computation over both tables.
    const headers = [...container.querySelectorAll('.ant-table-thead th')].map((th) => th.textContent?.trim());
    expect(headers).not.toContain('Description');
    expect(headers.filter((h) => h === 'Employees')).toHaveLength(2);

    const actions = [...container.querySelectorAll('.ant-table-tbody .ant-btn')].map((b) => b.textContent?.trim());
    expect(actions).toContain('Delete');
    // The count is what disables Delete; it must not be the column that disappears.
    expect(screen.getByText('Herdsman')).toBeInTheDocument();
  });
});
