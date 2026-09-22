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
  const e = err as { response?: { data?: unknown; status?: number }; message?: string };
  const data = e?.response?.data;
  const status = e?.response?.status;
  // String bodies (the common case)
  if (typeof data === 'string' && data.trim()) {
    return data;
  }
  // JSON error bodies, e.g. { error: "..." } or { message: "..." } or { title: "..." }.
  // Rendering such an object as UI text crashes React (minified error #31),
  // so always reduce to a plain string here.
  if (data && typeof data === 'object') {
    const obj = data as Record<string, unknown>;
    // `detail` before `title`: an RFC 7807 body says "Bad Request" in the title and the
    // actual reason in the detail, so reading the title first told the user nothing. The
    // traceId travels with it because it is the only handle on the server-side log line.
    for (const key of ['error', 'message', 'detail', 'title']) {
      const val = obj[key];
      if (typeof val === 'string' && val.trim()) {
        const traceId = typeof obj.traceId === 'string' ? obj.traceId : undefined;
        return traceId ? `${val} (trace ${traceId})` : val;
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
  // A body with nothing readable in it still leaves the status, which is what tells a user
  // (and a bug report) whether the request was rejected or the server failed.
  if (status) {
    return `${fallback} (HTTP ${status})`;
  }

  return e?.message || fallback;
};
