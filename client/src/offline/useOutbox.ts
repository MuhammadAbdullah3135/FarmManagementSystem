import { useEffect, useState } from 'react';
import { listAccountMutations, listScopeMutations } from './outbox';
import { useQueueStore } from './queueEvents';
import type { OfflineScope, OutboxItem } from './db';

/**
 * The queue, as a page sees it.
 *
 * Re-read on a scope change and on every queue change (enqueue, an outcome, a retry, a
 * dismissal), so a screen that showed "waiting to sync" a moment ago is not still claiming it
 * after the flush finished. Nothing here polls: the change counter is bumped by the writers
 * themselves.
 *
 * `kinds` narrows it to the workflows a page owns — the weight page must not list a check-in,
 * and the task list must not count a weight. The account-wide view (the sync screen) passes
 * nothing and sees everything, which is the only place where "everything on this device" is
 * the right answer.
 */
export function useOutboxItems(
  scope: OfflineScope | null,
  options: { kinds?: readonly string[] } = {},
): OutboxItem[] {
  const revision = useQueueStore((state) => state.revision);
  const accountId = scope?.accountId ?? null;
  const farmId = scope?.farmId ?? null;
  const [items, setItems] = useState<OutboxItem[]>([]);

  // The filter is compared as text, so an inline array literal does not re-run the read on every
  // render — the queue is read from storage, and re-reading it per render for an unchanged set
  // of kinds would be work with no answer that differs.
  const kindKey = options.kinds ? [...options.kinds].sort().join('|') : '';

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      const rows = accountId && farmId
        ? await listScopeMutations({ accountId, farmId })
        : [];
      const wanted = kindKey === '' ? null : new Set(kindKey.split('|'));
      const visible = wanted ? rows.filter((row) => wanted.has(row.kind)) : rows;
      if (!cancelled) setItems(visible);
    })();

    return () => {
      cancelled = true;
    };
  }, [accountId, farmId, revision, kindKey]);

  return items;
}

/** Every farm's queue for one account, oldest first — what the sync screen shows. */
export function useAccountOutboxItems(accountId: string | null): OutboxItem[] {
  const revision = useQueueStore((state) => state.revision);
  const [items, setItems] = useState<OutboxItem[]>([]);

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      const rows = accountId ? await listAccountMutations(accountId) : [];
      if (!cancelled) setItems(rows);
    })();

    return () => {
      cancelled = true;
    };
  }, [accountId, revision]);

  return items;
}
