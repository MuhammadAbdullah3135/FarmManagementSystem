import { useSearchParams } from 'react-router-dom';

/** The page a list's URL asks for; anything unreadable falls back to the first page. */
export const resolvePage = (raw: string | null) => {
  const parsed = Number(raw);
  return Number.isInteger(parsed) && parsed > 1 ? parsed : 1;
};

/**
 * A list's query string as its view state, and the one way to change it.
 *
 * The write is deliberately `replace`: paging and filtering should not fill the Back button with
 * views the user never meant to return to. An `undefined` value removes its key, which is how a
 * list returns to its default view (page one, every filter off) instead of writing `page=1` and
 * `statusId=` into every URL.
 */
export const useListParams = () => {
  const [searchParams, setSearchParams] = useSearchParams();

  const updateParams = (patch: Record<string, string | number | undefined>) => {
    setSearchParams((previous) => {
      const next = new URLSearchParams(previous);
      for (const [key, value] of Object.entries(patch)) {
        if (value === undefined) next.delete(key);
        else next.set(key, String(value));
      }
      return next;
    }, { replace: true });
  };

  return { searchParams, updateParams };
};
