import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import api from '../api/axios';
import type { AuthResponse, User, LoginRequest, RegisterRequest, ResetPasswordRequest } from '../types';

interface AuthState {
  user: User | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  error: string | null;
  login: (request: LoginRequest) => Promise<boolean>;
  register: (request: RegisterRequest) => Promise<boolean>;
  resetPassword: (request: ResetPasswordRequest) => Promise<boolean>;
  logout: () => void;
  clearError: () => void;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      user: null,
      isAuthenticated: false,
      isLoading: false,
      error: null,

      login: async (request: LoginRequest) => {
        set({ isLoading: true, error: null });
        try {
          const response = await api.post<AuthResponse>('/auth/login', request);
          const { accessToken, refreshToken, userId, accountId, email, firstName, lastName } = response.data;

          localStorage.setItem('accessToken', accessToken);
          localStorage.setItem('refreshToken', refreshToken);

          const user: User = {
            userId,
            accountId,
            email,
            firstName,
            lastName,
            roles: [],
          };

          localStorage.setItem('user', JSON.stringify(user));
          set({ user, isAuthenticated: true, isLoading: false });
          return true;
        } catch (error: any) {
          const message = error.response?.data || 'Login failed';
          set({ error: message, isLoading: false });
          return false;
        }
      },

      register: async (request: RegisterRequest) => {
        set({ isLoading: true, error: null });
        try {
          const response = await api.post<AuthResponse>('/auth/register', request);
          const { accessToken, refreshToken, userId, accountId, email, firstName, lastName } = response.data;

          localStorage.setItem('accessToken', accessToken);
          localStorage.setItem('refreshToken', refreshToken);

          const user: User = {
            userId,
            accountId,
            email,
            firstName,
            lastName,
            roles: ['SystemOwner'],
          };

          localStorage.setItem('user', JSON.stringify(user));
          set({ user, isAuthenticated: true, isLoading: false });
          return true;
        } catch (error: any) {
          const message = error.response?.data || 'Registration failed';
          set({ error: message, isLoading: false });
          return false;
        }
      },

      resetPassword: async (request: ResetPasswordRequest) => {
        set({ isLoading: true, error: null });
        try {
          await api.post('/auth/reset-password', request);
          set({ isLoading: false });
          return true;
        } catch (error: any) {
          const message = error.response?.data || 'Reset failed';
          set({ error: message, isLoading: false });
          return false;
        }
      },

      logout: () => {
        localStorage.removeItem('accessToken');
        localStorage.removeItem('refreshToken');
        localStorage.removeItem('user');
        localStorage.removeItem('activeFarmId');
        set({ user: null, isAuthenticated: false });
      },

      clearError: () => set({ error: null }),
    }),
    {
      name: 'auth-storage',
      partialize: (state) => ({
        user: state.user,
        isAuthenticated: state.isAuthenticated,
      }),
    }
  )
);
