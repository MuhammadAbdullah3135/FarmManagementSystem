import { useEffect, useMemo, useState, type CSSProperties } from 'react';
import { Input, Select, Space } from 'antd';
import { useTranslation } from 'react-i18next';
import { vetsApi } from '../api/health';
import type { EntityOption } from './EntityPicker';

/** The pair a clinical record keeps while `VetName` and `VetId` coexist. */
export interface VetLookupValue {
  vetId?: string;
  vetName?: string;
}

interface VetLookupOrTextProps {
  /** Injected by Form.Item. A selected vet writes both halves of the pair. */
  value?: VetLookupValue;
  onChange?: (value?: VetLookupValue) => void;
  /** Fetch the farm's active vets. Defaults to the shared API call. */
  loadOptions?: () => Promise<EntityOption[]>;
  placeholder?: string;
  textPlaceholder?: string;
  allowClear?: boolean;
  disabled?: boolean;
  style?: CSSProperties;
}

/**
 * Loads the farm's active vets as picker options. Kept at module scope so the
 * picker's effect does not refetch each render.
 */
const loadVetOptions = async (): Promise<EntityOption[]> => {
  const response = await vetsApi.list(false);
  return response.data.map((vet) => ({ value: vet.id, label: vet.name }));
};

/**
 * A vet picker backed by the master list, with the original free-text input kept
 * beside it. Selecting a vet stores both its id and its name; typing in the text
 * input clears the id and retains only the name, so an unlisted vet still saves
 * exactly the string it did before this list existed.
 */
const VetLookupOrText = ({
  value,
  onChange,
  loadOptions = loadVetOptions,
  placeholder,
  textPlaceholder,
  allowClear,
  disabled,
  style,
}: VetLookupOrTextProps) => {
  const { t } = useTranslation('common');
  const [options, setOptions] = useState<EntityOption[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    loadOptions()
      .then((rows) => {
        if (!cancelled) setOptions(rows);
      })
      .catch(() => {
        // The page owns the error path. A picker that cannot load still has the
        // free-text fallback and must not blank the form or throw during render.
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [loadOptions]);

  const merged = useMemo(() => {
    if (!value?.vetId || !value.vetName || options.some((option) => option.value === value.vetId)) {
      return options;
    }
    // A record can point at a vet that has since been deactivated. It is not in
    // the active list, but its saved name belongs on screen until the user changes it.
    return [{ value: value.vetId, label: value.vetName }, ...options];
  }, [options, value]);

  const selectVet = (vetId?: string) => {
    if (!vetId) {
      onChange?.(undefined);
      return;
    }
    const selected = merged.find((option) => option.value === vetId);
    onChange?.({
      vetId,
      vetName: selected?.label ?? value?.vetName,
    });
  };

  const typeName = (vetName: string) => {
    onChange?.(vetName ? { vetName } : undefined);
  };

  return (
    <Space.Compact block style={style}>
      <Select
        showSearch
        optionFilterProp="label"
        options={merged}
        value={value?.vetId}
        onChange={selectVet}
        loading={loading}
        placeholder={placeholder ?? t('searchOrSelect')}
        allowClear={allowClear}
        disabled={disabled}
        style={{ width: '60%' }}
      />
      <Input
        value={value?.vetName}
        onChange={(event) => typeName(event.target.value)}
        placeholder={textPlaceholder ?? t('custom')}
        allowClear={allowClear}
        disabled={disabled}
        style={{ width: '40%' }}
      />
    </Space.Compact>
  );
};

export default VetLookupOrText;
