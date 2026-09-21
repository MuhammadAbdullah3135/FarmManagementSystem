import api from './axios';
import { farmUrl } from './farmApi';

/**
 * The farm-scoped role vocabulary, mirroring the backend `FarmRoles` constants
 * (which in turn mirror the `[Authorize(Roles = "…")]` strings). Kept in one
 * place so the invite dialog and the role editor cannot drift.
 */
export const FARM_ROLES = [
  'SystemOwner',
  'FarmManager',
  'Veterinarian',
  'Employee',
  'Accountant',
  'Viewer',
] as const;

export type FarmRole = (typeof FARM_ROLES)[number];

export interface FarmMember {
  userId: string;
  email: string;
  firstName: string;
  lastName: string;
  role: string;
  joinedAt: string;
}

export interface FarmInvitation {
  id: string;
  email: string;
  role: string;
  status: string;
  expiresAt: string;
  createdAt: string;
}

export interface PendingInvitation {
  id: string;
  farmId: string;
  farmName: string;
  role: string;
  invitedByName: string;
  expiresAt: string;
}

export const membersApi = {
  list: () => api.get<FarmMember[]>(farmUrl('/members')),
  updateRole: (userId: string, role: string) =>
    api.put<FarmMember>(farmUrl(`/members/${userId}/role`), { role }),
  remove: (userId: string) => api.delete(farmUrl(`/members/${userId}`)),

  listInvitations: () => api.get<FarmInvitation[]>(farmUrl('/invitations')),
  invite: (email: string, role: string) =>
    api.post<FarmInvitation>(farmUrl('/invitations'), { email, role }),
  revokeInvitation: (id: string) => api.delete(farmUrl(`/invitations/${id}`)),
};

export const invitationsApi = {
  /** Invitations addressed to the signed-in user, across all farms. */
  pendingForMe: () => api.get<PendingInvitation[]>('/invitations/pending-for-me'),
  accept: (token: string) => api.post('/invitations/accept', { token }),
  decline: (token: string) => api.post('/invitations/decline', { token }),
};
