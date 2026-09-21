import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import MembersPage from './MembersPage';
import { membersApi } from '../../api/members';

vi.mock('../../api/members', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/members')>();
  return {
    ...actual,
    membersApi: {
      list: vi.fn(),
      updateRole: vi.fn(),
      remove: vi.fn(),
      listInvitations: vi.fn(),
      invite: vi.fn(),
      revokeInvitation: vi.fn(),
    },
  };
});

const members = [
  {
    userId: 'u1', email: 'owner@example.com', firstName: 'Owner', lastName: 'One',
    role: 'SystemOwner', joinedAt: '2026-01-01T00:00:00Z',
  },
  {
    userId: 'u2', email: 'viewer@example.com', firstName: 'Viewer', lastName: 'Two',
    role: 'Viewer', joinedAt: '2026-02-01T00:00:00Z',
  },
];

const invitations = [
  {
    id: 'i1', email: 'pending@example.com', role: 'Veterinarian',
    status: 'Pending', expiresAt: '2026-12-01T00:00:00Z', createdAt: '2026-03-01T00:00:00Z',
  },
];

beforeEach(() => {
  vi.mocked(membersApi.list).mockResolvedValue({ data: members } as never);
  vi.mocked(membersApi.listInvitations).mockResolvedValue({ data: invitations } as never);
  vi.mocked(membersApi.invite).mockResolvedValue({ data: {} } as never);
  vi.mocked(membersApi.updateRole).mockResolvedValue({ data: {} } as never);
  vi.mocked(membersApi.remove).mockResolvedValue({ data: {} } as never);
  vi.mocked(membersApi.revokeInvitation).mockResolvedValue({ data: {} } as never);
});

describe('MembersPage', () => {
  it('lists members with their farm roles and pending invitations', async () => {
    render(<MembersPage />);

    expect(await screen.findByText('owner@example.com')).toBeInTheDocument();
    expect(screen.getByText('viewer@example.com')).toBeInTheDocument();

    expect(await screen.findByText('pending@example.com')).toBeInTheDocument();
    expect(membersApi.listInvitations).toHaveBeenCalled();
  });

  it('sends an invitation with the chosen role', async () => {
    render(<MembersPage />);
    await screen.findByText('owner@example.com');

    await userEvent.click(screen.getByRole('button', { name: /invite member/i }));

    const emailInput = await screen.findByPlaceholderText('person@example.com');
    await userEvent.type(emailInput, 'newperson@example.com');

    // Role keeps its 'Viewer' default, so submit exercises the default path.
    await userEvent.click(screen.getByRole('button', { name: /send invitation/i }));

    await waitFor(() =>
      expect(membersApi.invite).toHaveBeenCalledWith('newperson@example.com', 'Viewer'));
  });

  it('removes a member after confirmation', async () => {
    render(<MembersPage />);
    await screen.findByText('viewer@example.com');

    const rowButtons = screen.getAllByRole('button', { name: 'Remove' });
    await userEvent.click(rowButtons[rowButtons.length - 1]);

    // antd Popconfirm renders its own "Remove" confirm button in a popover,
    // appended after the row buttons.
    // Generous timeout: the whole suite runs files in parallel and the popover
    // animation can outlast the 1s default under load.
    const allButtons = await screen.findAllByRole('button', { name: 'Remove' }, { timeout: 5000 });
    await userEvent.click(allButtons[allButtons.length - 1]);

    await waitFor(() => expect(membersApi.remove).toHaveBeenCalled());
    // The popover interaction is slow in jsdom under a fully parallel suite, so
    // this one test gets more headroom than the 5s default.
  }, 20000);

  it('revokes a pending invitation', async () => {
    render(<MembersPage />);
    await screen.findByText('pending@example.com');

    await userEvent.click(screen.getByRole('button', { name: 'Revoke' }));

    await waitFor(() => expect(membersApi.revokeInvitation).toHaveBeenCalledWith('i1'));
  });

  it('changes a member role through the row select', async () => {
    render(<MembersPage />);
    await screen.findByText('viewer@example.com');

    await userEvent.click(screen.getByLabelText('Change role for viewer@example.com'));

    const option = await screen.findByTitle('FarmManager');
    await userEvent.click(option);

    await waitFor(() =>
      expect(membersApi.updateRole).toHaveBeenCalledWith('u2', 'FarmManager'));
  });
});
