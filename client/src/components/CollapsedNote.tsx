import { useState } from 'react';
import { Button, Input, Space } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';

interface CollapsedNoteProps {
  value?: string;
  onChange?: (value: string) => void;
  /** Accessible name for the textarea; a Form.Item label usually covers this. */
  label?: string;
  placeholder?: string;
  rows?: number;
  maxLength?: number;
  disabled?: boolean;
}

/**
 * An optional note that stays out of the way until it is wanted. Forms keep the same
 * string value, so a note field can move behind this control without changing the
 * payload or the place it is stored.
 *
 * The note opens itself when it already has text: hiding an existing explanation
 * behind a "+ Add note" link would make old records look emptier than they are.
 */
const CollapsedNote = ({
  value,
  onChange,
  label,
  placeholder,
  rows = 2,
  maxLength = 1000,
  disabled = false,
}: CollapsedNoteProps) => {
  const { t } = useTranslation('common');
  const [open, setOpen] = useState(Boolean(value && value.trim()));
  const expanded = open || Boolean(value && value.trim());

  if (!expanded) {
    return (
      <Button
        type="link"
        size="small"
        icon={<PlusOutlined />}
        onClick={() => setOpen(true)}
        disabled={disabled}
        style={{ paddingInline: 0 }}
      >
        {t('addNote')}
      </Button>
    );
  }

  return (
    <Space orientation="vertical" size={4} style={{ width: '100%' }}>
      <Input.TextArea
        value={value ?? ''}
        onChange={(event) => onChange?.(event.target.value)}
        rows={rows}
        maxLength={maxLength}
        disabled={disabled}
        placeholder={placeholder}
        aria-label={label ?? t('noteOptional')}
      />
      {!value && (
        <Button type="link" size="small" onClick={() => setOpen(false)} style={{ paddingInline: 0 }}>
          {t('hideEmptyNote')}
        </Button>
      )}
    </Space>
  );
};

export default CollapsedNote;
