import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import RouteErrorBoundary from './RouteErrorBoundary';
import AppLayout from './AppLayout';
import api from '../api/axios';
import { useAuthStore } from '../stores/authStore';
import { useFarmStore } from '../stores/farmStore';
import type { Farm, User } from '../types';

vi.mock('../api/axios', () => ({
  default: {
    get: vi.fn(() => Promise.resolve({ data: [] })),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
  setFarmAccessDeniedHandler: vi.fn(),
}));

const farm: Farm = {
  id: 'farm-a',
  name: 'Farm A',
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  userFarmRole: 'SystemOwner',
};

const user: User = {
  userId: 'user-1',
  accountId: 'account-1',
  email: 'owner@example.com',
  firstName: 'Owner',
  lastName: 'Person',
  roles: ['SystemOwner'],
};

/** Stands in for a page that throws inside render, e.g. formatting a null field as money. */
const Boom: React.FC<{ armed: { current: boolean } }> = ({ armed }) => {
  if (armed.current) {
    throw new TypeError("Cannot read properties of null (reading 'toLocaleString')");
  }
  return <div>Page content</div>;
};

describe('RouteErrorBoundary', () => {
  it('contains the failure in the page and names the route', () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => {});
    const armed = { current: true };

    render(
      <MemoryRouter>
        <RouteErrorBoundary routePath="/dashboard/reports/cost-per-animal">
          <Boom armed={armed} />
        </RouteErrorBoundary>
      </MemoryRouter>,
    );

    expect(screen.getByText('This screen hit an error')).toBeInTheDocument();
    expect(screen.getByText(/cost-per-animal/)).toBeInTheDocument();
    expect(screen.getByText(/toLocaleString/)).toBeInTheDocument();
    error.mockRestore();
  });

  it('re-renders the page when the user tries again, without a reload', async () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => {});
    const armed = { current: true };
    const userEvent_ = userEvent.setup();

    render(
      <MemoryRouter>
        <RouteErrorBoundary routePath="/dashboard/whatever">
          <Boom armed={armed} />
        </RouteErrorBoundary>
      </MemoryRouter>,
    );

    expect(screen.getByText('This screen hit an error')).toBeInTheDocument();

    // The payload that broke the screen is not necessarily the one that comes back.
    armed.current = false;
    await userEvent_.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByText('Page content')).toBeInTheDocument();
    error.mockRestore();
  });

  it('leaves the app shell standing when a page throws', async () => {
    const error = vi.spyOn(console, 'error').mockImplementation(() => {});
    // The layout refetches farms on mount, so the API has to report the farm too.
    vi.mocked(api.get).mockResolvedValue({ data: [farm] } as never);
    localStorage.setItem('activeFarmId', farm.id);
    useAuthStore.setState({ user, isAuthenticated: true });
    useFarmStore.setState({ farms: [farm], activeFarm: farm, isLoading: false, error: null });

    render(
      <MemoryRouter initialEntries={['/dashboard']}>
        <Routes>
          <Route path="/dashboard" element={<AppLayout />}>
            <Route index element={<Boom armed={{ current: true }} />} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );

    expect(await screen.findByText('This screen hit an error')).toBeInTheDocument();

    // The point of the whole change: header, farm selector and navigation still work, so the
    // user can move to another screen instead of being left with "Reload app".
    expect(screen.getByText('Farm A')).toBeInTheDocument();
    expect(screen.getByText('Dashboard')).toBeInTheDocument();
    expect(screen.getByText('Farm Tasks')).toBeInTheDocument();
    error.mockRestore();
  });
});
