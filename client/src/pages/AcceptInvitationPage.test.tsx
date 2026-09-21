import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import AcceptInvitationPage from './AcceptInvitationPage';
import { invitationsApi } from '../api/members';
import { useAuthStore } from '../stores/authStore';
import { useFarmStore } from '../stores/farmStore';
import type { User } from '../types';

vi.mock('../api/members', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/members')>();
  return {
    ...actual,
    invitationsApi: {
      pendingForMe: vi.fn(),
      accept: vi.fn(),
      decline: vi.fn(),
    },
  };
});

// The page refreshes the farm list after accepting; keep axios out of the test.
vi.mock('../api/axios', () => ({
  default: {
    get: vi.fn(() => Promise.resolve({ data: [] })),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
  setFarmAccessDeniedHandler: vi.fn(),
}));

const user: User = {
  userId: 'u1',
  accountId: 'a1',
  email: 'invitee@example.com',
  firstName: 'Invitee',
  lastName: 'Person',
  roles: ['Viewer'],
};

const renderAt = (search = '?token=tok-123') =>
  render(
    <MemoryRouter initialEntries={[`/accept-invitation${search}`]}>
      <AcceptInvitationPage />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.mocked(invitationsApi.pendingForMe).mockResolvedValue({ data: [] } as never);
  vi.mocked(invitationsApi.accept).mockResolvedValue({ data: {} } as never);
  vi.mocked(invitationsApi.decline).mockResolvedValue({ data: {} } as never);
  localStorage.clear();
  useAuthStore.setState({ user: null, isAuthenticated: false, isLoading: false, error: null });
  useFarmStore.setState({ farms: [], activeFarm: null, isLoading: false, error: null });
});

describe('AcceptInvitationPage', () => {
  it('asks an unauthenticated visitor to sign in', async () => {
    renderAt();

    expect(await screen.findByText('Sign in to accept your invitation')).toBeInTheDocument();
    expect(invitationsApi.accept).not.toHaveBeenCalled();
  });

  it('accepts the invitation and refreshes the farm list', async () => {
    useAuthStore.setState({ user, isAuthenticated: true });
    renderAt();

    await userEvent.click(await screen.findByRole('button', { name: /accept invitation/i }));

    await waitFor(() => expect(invitationsApi.accept).toHaveBeenCalledWith('tok-123'));
    expect(await screen.findByText('Invitation accepted')).toBeInTheDocument();
  });

  it('declines the invitation without joining', async () => {
    useAuthStore.setState({ user, isAuthenticated: true });
    renderAt();

    await userEvent.click(await screen.findByRole('button', { name: /decline/i }));

    await waitFor(() => expect(invitationsApi.decline).toHaveBeenCalledWith('tok-123'));
    expect(await screen.findByText('Invitation declined')).toBeInTheDocument();
  });

  it('lists invitations waiting for the signed-in user', async () => {
    useAuthStore.setState({ user, isAuthenticated: true });
    vi.mocked(invitationsApi.pendingForMe).mockResolvedValue({
      data: [{
        id: 'i1', farmId: 'f1', farmName: 'Green Acres', role: 'Veterinarian',
        invitedByName: 'Owner One', expiresAt: '2026-12-01T00:00:00Z',
      }],
    } as never);

    renderAt();

    expect(await screen.findByText('Green Acres')).toBeInTheDocument();
  });
});
