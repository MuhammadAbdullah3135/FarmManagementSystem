import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import api from '../api/axios';
import { getApiError } from '../api/farmApi';
import { clearOfflineDataOnSignOut } from '../offline/offlineData';
import { reconcileLocaleOnSignIn } from '../i18n/localeSync';
import { useFarmStore } from './farmStore';
import type { AuthResponse, User, LoginRequest, RegisterRequest, ResetPasswordRequest, ConfirmResetPasswordRequest } from '../types';

interface AuthState {
  user: User | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  error: string | null;
  login: (request: LoginRequest) => Promise<boolean>;
  register: (request: RegisterRequest) => Promise<boolean>;
  resetPassword: (request: ResetPasswordRequest) => Promise<boolean>;
  confirmResetPassword: (request: ConfirmResetPasswordRequest) => Promise<boolean>;
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
          const { accessToken, refreshToken, userId, accountId, email, firstName, lastName, roles, locale } = response.data;

          localStorage.setItem('accessToken', accessToken);
          localStorage.setItem('refreshToken', refreshToken);

          const user: User = {
            userId,
            accountId,
            email,
            firstName,
            lastName,
            roles: roles ?? [],
            locale: locale ?? null,
          };

          localStorage.setItem('user', JSON.stringify(user));
          set({ user, isAuthenticated: true, isLoading: false });

          // Deliberately not awaited, and after the session is usable: the account's
          // language arrives with the session, and a slow or unreachable preferences
          // endpoint must not hold up signing in.
          void reconcileLocaleOnSignIn(locale);

          return true;
        } catch (error: any) {
          const message = getApiError(error, 'Login failed');
          set({ error: message, isLoading: false });
          return false;
        }
      },

      register: async (request: RegisterRequest) => {
        set({ isLoading: true, error: null });
        try {
          const response = await api.post<AuthResponse>('/auth/register', request);
          const { accessToken, refreshToken, userId, accountId, email, firstName, lastName, roles, locale } = response.data;

          localStorage.setItem('accessToken', accessToken);
          localStorage.setItem('refreshToken', refreshToken);

          const user: User = {
            userId,
            accountId,
            email,
            firstName,
            lastName,
            roles: roles ?? [],
            locale: locale ?? null,
          };

          localStorage.setItem('user', JSON.stringify(user));
          set({ user, isAuthenticated: true, isLoading: false });

          // A brand-new account is exactly the case where adopting the device's language
          // matters: it has no preference to disagree with.
          void reconcileLocaleOnSignIn(locale);

          return true;
        } catch (error: any) {
          const message = getApiError(error, 'Registration failed');
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
          const message = getApiError(error, 'Reset failed');
          set({ error: message, isLoading: false });
          return false;
        }
      },

      confirmResetPassword: async (request: ConfirmResetPasswordRequest) => {
        set({ isLoading: true, error: null });
        try {
          await api.post('/auth/confirm-reset-password', request);
          set({ isLoading: false });
          return true;
        } catch (error: any) {
          const message = getApiError(error, 'Password reset failed');
          set({ error: message, isLoading: false });
          return false;
        }
      },

      logout: () => {
        localStorage.removeItem('accessToken');
        localStorage.removeItem('refreshToken');
        localStorage.removeItem('user');
        localStorage.removeItem('activeFarmId');

        // The persisted farm list survives this call otherwise, leaving the previous account's
        // farm name and id readable on the device after sign-out (and sent as X-Farm-Id by the
        // next session before it has fetched its own farms).
        useFarmStore.getState().reset();

        // The device must not keep farm data for a user who has signed out: the next
        // person to pick it up would otherwise be able to read it out of the cache with
        // no session at all. Deliberately not awaited — sign-out is a UI action and must
        // complete regardless of whether storage cooperates.
        void clearOfflineDataOnSignOut();

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
