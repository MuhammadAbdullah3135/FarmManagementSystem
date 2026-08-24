import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import api from '../api/axios';
import type { Farm } from '../types';

interface FarmState {
  farms: Farm[];
  activeFarm: Farm | null;
  isLoading: boolean;
  error: string | null;
  fetchFarms: () => Promise<void>;
  setActiveFarm: (farm: Farm) => void;
  createFarm: (name: string, description?: string) => Promise<Farm | null>;
}

export const useFarmStore = create<FarmState>()(
  persist(
    (set) => ({
      farms: [],
      activeFarm: null,
      isLoading: false,
      error: null,

      fetchFarms: async () => {
        set({ isLoading: true, error: null });
        try {
          const response = await api.get<Farm[]>('/farms');
          const farms = response.data;
          set({ farms, isLoading: false });

          const activeFarmId = localStorage.getItem('activeFarmId');
          if (activeFarmId) {
            const activeFarm = farms.find((f) => f.id === activeFarmId);
            if (activeFarm) {
              set({ activeFarm });
            }
          }
        } catch (error: any) {
          set({ error: error.message, isLoading: false });
        }
      },

      setActiveFarm: (farm: Farm) => {
        localStorage.setItem('activeFarmId', farm.id);
        set({ activeFarm: farm });
      },

      createFarm: async (name: string, description?: string) => {
        set({ isLoading: true, error: null });
        try {
          const response = await api.post<Farm>('/farms', { name, description });
          const farm = response.data;
          set((state) => ({
            farms: [...state.farms, farm],
            activeFarm: farm,
            isLoading: false,
          }));
          localStorage.setItem('activeFarmId', farm.id);
          return farm;
        } catch (error: any) {
          set({ error: error.message, isLoading: false });
          return null;
        }
      },
    }),
    {
      name: 'farm-storage',
      partialize: (state) => ({
        farms: state.farms,
        activeFarm: state.activeFarm,
      }),
    }
  )
);
