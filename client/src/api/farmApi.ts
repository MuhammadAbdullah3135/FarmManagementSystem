import { useFarmStore } from '../stores/farmStore';

export const getActiveFarmId = (): string => {
  const farmId = useFarmStore.getState().activeFarm?.id;
  if (!farmId) {
    throw new Error('Please select a farm first');
  }
  return farmId;
};

export const farmUrl = (path: string): string => `/farm/${getActiveFarmId()}${path}`;

export const getApiError = (err: unknown): string => {
  const e = err as { response?: { data?: unknown }; message?: string };
  if (e?.response?.data && typeof e.response.data === 'string') {
    return e.response.data;
  }
  return e?.message || 'Something went wrong';
};
