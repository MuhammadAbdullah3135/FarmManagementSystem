import type { CSSProperties } from 'react';
import { Input, Select, Space } from 'antd';
import { useTranslation } from 'react-i18next';

export interface ReasonChoice {
  value: string;
  label: string;
}

export interface ReasonValue {
  /** The preset code, or `other` when the user is writing their own reason. */
  code?: string;
  /** Only meaningful when `code` is `other`; never stored for a preset. */
  note?: string;
}

interface ReasonSelectProps {
  options: ReasonChoice[];
  value?: ReasonValue;
  onChange?: (value: ReasonValue) => void;
  /** Code that reveals the free-text note. Defaults to `other`. */
  otherValue?: string;
  noteLabel?: string;
  notePlaceholder?: string;
  placeholder?: string;
  disabled?: boolean;
  /** Applied to the select and the note, so it can sit in a Form.Item. */
  style?: CSSProperties;
}

/**
 * A reason list with an "Other" escape hatch. Choosing a preset clears any note, which
 * keeps a later report from counting a stale sentence under a real code.
 */
const ReasonSelect = ({
  options,
  value,
  onChange,
  otherValue = 'other',
  noteLabel,
  notePlaceholder,
  placeholder,
  disabled,
  style,
}: ReasonSelectProps) => {
  const { t } = useTranslation('common');
  const isOther = value?.code === otherValue;

  return (
    <Space orientation="vertical" size={8} style={{ width: '100%', ...style }}>
      <Select
        value={value?.code}
        onChange={(code?: string) =>
          onChange?.({
            code,
            note: code === otherValue ? value?.note : undefined,
          })
        }
        options={options}
        placeholder={placeholder ?? t('selectReason')}
        disabled={disabled}
        allowClear
        style={{ width: '100%' }}
      />
      {isOther && (
        <Input.TextArea
          value={value?.note ?? ''}
          onChange={(event) => onChange?.({ code: otherValue, note: event.target.value })}
          rows={2}
          maxLength={500}
          disabled={disabled}
          placeholder={notePlaceholder ?? t('describeReason')}
          aria-label={noteLabel ?? t('reason')}
        />
      )}
    </Space>
  );
};

export default ReasonSelect;
