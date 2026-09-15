import { useFarmStore } from '../stores/farmStore';

export const getActiveFarmId = (): string => {
  const farmId = useFarmStore.getState().activeFarm?.id;
  if (!farmId) {
    throw new Error('Please select a farm first');
  }
  return farmId;
};

export const farmUrl = (path: string): string => `/farm/${getActiveFarmId()}${path}`;

export const getApiError = (err: unknown, fallback = 'Something went wrong'): string => {
  const e = err as { response?: { data?: unknown }; message?: string };
  const data = e?.response?.data;
  // String bodies (the common case)
  if (typeof data === 'string' && data.trim()) {
    return data;
  }
  // JSON error bodies, e.g. { error: "..." } or { message: "..." } or { title: "..." }.
  // Rendering such an object as UI text crashes React (minified error #31),
  // so always reduce to a plain string here.
  if (data && typeof data === 'object') {
    const obj = data as Record<string, unknown>;
    for (const key of ['error', 'message', 'title', 'detail']) {
      const val = obj[key];
      if (typeof val === 'string' && val.trim()) {
        return val;
      }
    }
    if (typeof obj.errors === 'object' && obj.errors !== null) {
      // ASP.NET-style validation errors: { errors: { Field: ["msg", ...] } }
      const msgs: string[] = [];
      for (const fieldMsgs of Object.values(obj.errors as Record<string, unknown>)) {
        if (Array.isArray(fieldMsgs)) {
          msgs.push(...fieldMsgs.filter((m): m is string => typeof m === 'string'));
        }
      }
      if (msgs.length > 0) return msgs.join(' ');
    }
  }
  return e?.message || fallback;
};
