import { useEffect, useMemo, useState, type CSSProperties } from 'react';
import { useTranslation } from 'react-i18next';
import LookupQuickAddSelect, { type CreatedLookup } from './LookupQuickAddSelect';
import type { LookupKind, QuickAddContext, QuickAddVariant } from './lookupQuickAdd';

export interface EntityOption {
  value: string;
  label: string;
}

interface EntityPickerProps {
  /**
   * Options the page already has. When `loadOptions` is supplied these are shown
   * immediately and replaced once the request lands, which keeps the control usable
   * on a slow connection.
   */
  options?: EntityOption[];
  /** Refetch the list. Ignored when `options` is the only source. */
  loadOptions?: () => Promise<EntityOption[]>;
  /**
   * Registry-backed quick-add. The kind decides the modal fields and the create
   * call, exactly like `LookupQuickAddSelect` on its own.
   */
  kind?: LookupKind;
  ctx?: QuickAddContext;
  variant?: QuickAddVariant;
  /** Noun shown on the quick-add button when `kind` is not registry-backed. */
  label?: string;
  value?: string;
  onChange?: (value?: string) => void;
  onCreated?: (created: CreatedLookup, kind?: LookupKind) => void | Promise<void>;
  /**
   * Enables the "recently used" group. Use a stable per-list key (a lookup kind
   * name is the obvious one); recents are stored per browser, not per farm.
   */
  recentStorageKey?: string;
  /** How many ids to remember. Defaults to 5. */
  maxRecent?: number;
  placeholder?: string;
  allowClear?: boolean;
  disabled?: boolean;
  style?: CSSProperties;
}

const STORAGE_PREFIX = 'fms.entityPicker.';

function readRecent(key: string, max: number): string[] {
  try {
    const raw = window.localStorage.getItem(STORAGE_PREFIX + key);
    if (!raw) return [];
    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [];
    return parsed.filter((id): id is string => typeof id === 'string').slice(0, max);
  } catch {
    // Private mode, a quota error, or a hand-edited value: recents are a convenience,
    // never a reason for the form to fail.
    return [];
  }
}

function writeRecent(key: string, ids: string[]): void {
  try {
    window.localStorage.setItem(STORAGE_PREFIX + key, JSON.stringify(ids));
  } catch {
    // See readRecent: losing the history is acceptable, breaking the form is not.
  }
}

/**
 * A searchable select for a farm-maintained list: the choices come from the server,
 * the footer can create a new entry through the shared quick-add registry, and the
 * list puts recently chosen entries first. The value is still the entity id, so the
 * stored shape never changes.
 */
const EntityPicker = ({
  options = [],
  loadOptions,
  kind,
  ctx,
  variant,
  label,
  value,
  onChange,
  onCreated,
  recentStorageKey,
  maxRecent = 5,
  placeholder,
  allowClear,
  disabled,
  style,
}: EntityPickerProps) => {
  const { t } = useTranslation('common');
  const [loaded, setLoaded] = useState<EntityOption[]>([]);
  const [loading, setLoading] = useState(Boolean(loadOptions));
  const [recentIds, setRecentIds] = useState<string[]>(() =>
    recentStorageKey ? readRecent(recentStorageKey, maxRecent) : [],
  );

  useEffect(() => {
    if (!loadOptions) return;
    let cancelled = false;
    loadOptions()
      .then((rows) => {
        if (cancelled) return;
        setLoaded(rows);
        setLoading(false);
      })
      .catch(() => {
        // The page's own error path reports failures; a picker that cannot load
        // should not blank the form or throw during render.
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [loadOptions]);

  const merged = useMemo(() => {
    const byValue = new Map<string, EntityOption>();
    for (const option of [...loaded, ...options]) byValue.set(option.value, option);
    return [...byValue.values()];
  }, [loaded, options]);

  const groupedOptions = useMemo(() => {
    if (!recentStorageKey || recentIds.length === 0) return merged;
    const byValue = new Map(merged.map((option) => [option.value, option]));
    const recent: EntityOption[] = [];
    for (const id of recentIds) {
      const option = byValue.get(id);
      // An entry deleted on another screen must not leave a permanent ghost.
      if (option) recent.push(option);
    }
    const recentValues = new Set(recent.map((option) => option.value));
    const rest = merged.filter((option) => !recentValues.has(option.value));
    return [
      { label: t('recentlyUsed'), options: recent },
      { label: t('allOptions'), options: rest },
    ];
  }, [merged, recentIds, recentStorageKey, t]);

  const remember = (chosen?: string) => {
    if (!recentStorageKey || !chosen) return;
    const next = [chosen, ...recentIds.filter((id) => id !== chosen)].slice(0, maxRecent);
    setRecentIds(next);
    writeRecent(recentStorageKey, next);
  };

  return (
    <LookupQuickAddSelect
      kind={kind}
      ctx={ctx}
      variant={variant}
      label={label}
      options={groupedOptions}
      value={value}
      loading={loading}
      onChange={(chosen) => {
        remember(chosen);
        onChange?.(chosen);
      }}
      onCreated={onCreated}
      placeholder={placeholder ?? t('searchOrSelect')}
      allowClear={allowClear}
      disabled={disabled}
      style={style}
    />
  );
};

export default EntityPicker;
