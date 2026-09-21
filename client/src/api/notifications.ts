import api from './axios';
import { farmUrl } from './farmApi';

/**
 * A persisted alert for the signed-in user on the active farm.
 *
 * Distinct from the dashboard's transient alert cards: these survive a page
 * load, carry read/dismissed state, and are the record of what the farm's
 * conditions were when the scheduled dispatch last ran.
 */
export interface Notification {
  id: string;
  alertType: string;
  severity: string;
  title: string;
  message: string;
  link?: string | null;
  dueDate?: string | null;
  createdAt: string;
  isRead: boolean;
  readAtUtc?: string | null;
  isDismissed: boolean;
  isResolved: boolean;
  deliveredAtUtc?: string | null;
}

export interface NotificationList {
  items: Notification[];
  totalCount: number;
  /** Unread, undismissed, unresolved — what the header badge shows. */
  unreadCount: number;
}

export interface NotificationPreference {
  alertType: string;
  inAppEnabled: boolean;
  emailEnabled: boolean;
}

export interface NotificationPreferenceSettings {
  preferences: NotificationPreference[];
  /** Channel names; the server owns the vocabulary so the UI never guesses. */
  channels: string[];
  emailMinSeverityOnByDefault: string;
}

export interface NotificationListParams {
  unreadOnly?: boolean;
  includeDismissed?: boolean;
  includeResolved?: boolean;
  page?: number;
  pageSize?: number;
}

export const notificationsApi = {
  list: (params: NotificationListParams = {}) =>
    api.get<NotificationList>(farmUrl('/notifications'), { params }),

  unreadCount: () => api.get<number>(farmUrl('/notifications/unread-count')),

  markRead: (id: string) => api.post<Notification>(farmUrl(`/notifications/${id}/read`)),

  markAllRead: () => api.post<number>(farmUrl('/notifications/read-all')),

  dismiss: (id: string) => api.post<Notification>(farmUrl(`/notifications/${id}/dismiss`)),

  getPreferences: () => api.get<NotificationPreferenceSettings>(farmUrl('/notifications/preferences')),

  updatePreferences: (preferences: NotificationPreference[]) =>
    api.put<NotificationPreferenceSettings>(farmUrl('/notifications/preferences'), { preferences }),
};

/** Display names for the API's alert-type keys, falling back to the raw key. */
export const ALERT_TYPE_LABELS: Record<string, string> = {
  OverdueVaccination: 'Overdue vaccinations',
  OverdueWeightCheck: 'Overdue weight checks',
  Medicine: 'Medicine (expiry & low stock)',
  OverdueTask: 'Overdue farm tasks',
  DueBirth: 'Upcoming births',
  LowInventory: 'Low inventory stock',
};

export const alertTypeLabel = (alertType: string): string =>
  ALERT_TYPE_LABELS[alertType] ?? alertType;

/** Severity → antd tag/alert colour, matching the dashboard's alert cards. */
export const severityColor = (severity: string): string => {
  if (severity === 'Critical') return 'red';
  if (severity === 'Warning') return 'gold';
  return 'blue';
};
