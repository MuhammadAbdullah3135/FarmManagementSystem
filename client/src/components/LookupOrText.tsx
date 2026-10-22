import type { CSSProperties } from 'react';
import { Input, Space } from 'antd';
import { useTranslation } from 'react-i18next';
import EntityPicker, { type EntityOption } from './EntityPicker';

interface LookupOrTextProps {
  /**
   * Injected by Form.Item. The value is the label stored in the existing string
   * field - pickers never replace that storage with an id.
   */
  value?: string;
  onChange?: (value?: string) => void;
  /** Options the page already has, shown until `loadOptions` resolves. */
  options?: EntityOption[];
  /** Fetch the farm-maintained list. Keep this stable (useCallback). */
  loadOptions?: () => Promise<EntityOption[]>;
  placeholder?: string;
  /** Placeholder for the retained free-text input. */
  textPlaceholder?: string;
  recentStorageKey?: string;
  allowClear?: boolean;
  disabled?: boolean;
  style?: CSSProperties;
}

/**
 * A lookup picker with the original free-text input kept beside it.
 *
 * Phase C wires pickers into fields whose DTO still stores a label string. The
 * list lets people pick a farm-maintained value without taking away the ability
 * to record something the list does not contain yet. Both controls write the
 * same value, so saving sends exactly what it did before.
 */
const LookupOrText = ({
  value,
  onChange,
  options = [],
  loadOptions,
  placeholder,
  textPlaceholder,
  recentStorageKey,
  allowClear,
  disabled,
  style,
}: LookupOrTextProps) => {
  const { t } = useTranslation('common');

  return (
    <Space.Compact block style={style}>
      <EntityPicker
        options={options}
        loadOptions={loadOptions}
        value={value}
        onChange={onChange}
        recentStorageKey={recentStorageKey}
        placeholder={placeholder ?? t('searchOrSelect')}
        allowClear={allowClear}
        disabled={disabled}
        style={{ width: '60%' }}
      />
      <Input
        value={value}
        onChange={(event) => onChange?.(event.target.value)}
        placeholder={textPlaceholder ?? t('custom')}
        allowClear={allowClear}
        disabled={disabled}
        style={{ width: '40%' }}
      />
    </Space.Compact>
  );
};

export default LookupOrText;
