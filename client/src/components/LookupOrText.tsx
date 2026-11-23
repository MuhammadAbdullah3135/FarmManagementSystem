import type { CSSProperties } from 'react';
import { Input, Space } from 'antd';
import { useTranslation } from 'react-i18next';
import EntityPicker, { type EntityOption } from './EntityPicker';
import type { LookupKind } from './lookupQuickAdd';

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
  /**
   * Registry-backed quick-add. Only pass this when the stored string names a real
   * record that can be created from here (a supplier, say); a field that keeps a
   * free-form note should stay list-only.
   */
  kind?: LookupKind;
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
 *
 * `kind` extends that to creating a real record inline (a supplier from the feed
 * stock movement form). The created record's name is stored, and a name the list
 * already shows is selected instead of being created a second time.
 */
const LookupOrText = ({
  value,
  onChange,
  options = [],
  loadOptions,
  kind,
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
        kind={kind}
        // This field stores a name, not an id, so a quick-add writes the new
        // record's label here — and picks an existing entry of the same name
        // rather than creating a duplicate.
        selectCreatedLabel={Boolean(kind)}
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
