import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Farm } from '../types';

vi.mock('../api/axios', () => ({
  default: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}));

import { useFarmStore } from './farmStore';

const farm: Farm = {
  id: 'farm-1',
  name: 'Green Acres',
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  userFarmRole: 'SystemOwner',
};

describe('farmStore.clearActiveFarm', () => {
  beforeEach(() => {
    localStorage.clear();
    useFarmStore.setState({ farms: [], activeFarm: null, isLoading: false, error: null });
  });

  it('drops the selected farm and its persisted id', () => {
    useFarmStore.getState().setActiveFarm(farm);
    expect(localStorage.getItem('activeFarmId')).toBe(farm.id);
    expect(useFarmStore.getState().activeFarm?.id).toBe(farm.id);

    useFarmStore.getState().clearActiveFarm();

    expect(useFarmStore.getState().activeFarm).toBeNull();
    expect(localStorage.getItem('activeFarmId')).toBeNull();
  });

  it('leaves the farm list intact so the user can pick another', () => {
    useFarmStore.setState({ farms: [farm], activeFarm: farm });
    localStorage.setItem('activeFarmId', farm.id);

    useFarmStore.getState().clearActiveFarm();

    expect(useFarmStore.getState().farms).toHaveLength(1);
    expect(useFarmStore.getState().activeFarm).toBeNull();
  });
});
