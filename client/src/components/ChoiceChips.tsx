import { useMemo, useState } from 'react';
import { Button, Input, Space, Tag } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';

export interface ChoiceOption {
  value: string;
  label: string;
}

interface ChoiceChipsProps {
  options: ChoiceOption[];
  /** The selected values, in the order they were chosen. */
  value?: string[];
  onChange?: (value: string[]) => void;
  /** Let a user add a value the list does not contain yet. */
  allowCustom?: boolean;
  customPlaceholder?: string;
  disabled?: boolean;
}

/**
 * Multi-select as toggleable chips. The stored value is still a string array, so a
 * page can switch from a free-text comma list without changing its payload.
 */
const ChoiceChips = ({
  options,
  value = [],
  onChange,
  allowCustom = false,
  customPlaceholder,
  disabled = false,
}: ChoiceChipsProps) => {
  const { t } = useTranslation('common');
  const [custom, setCustom] = useState('');

  // Values that arrived from the server but are not in the option list stay visible,
  // otherwise opening an old record would silently drop data on the next save.
  const choices = useMemo(() => {
    const byValue = new Map(options.map((option) => [option.value, option.label]));
    for (const selected of value) if (!byValue.has(selected)) byValue.set(selected, selected);
    return [...byValue.entries()].map(([optionValue, label]) => ({ value: optionValue, label }));
  }, [options, value]);

  const toggle = (chip: string, checked: boolean) => {
    if (disabled) return;
    const next = checked ? [...value, chip] : value.filter((selected) => selected !== chip);
    onChange?.(next);
  };

  const addCustom = () => {
    const trimmed = custom.trim();
    if (!trimmed) return;
    if (!value.includes(trimmed)) onChange?.([...value, trimmed]);
    setCustom('');
  };

  return (
    <Space orientation="vertical" size={8} style={{ width: '100%' }}>
      <Space size={[0, 8]} wrap>
        {choices.map((choice) => (
          <Tag.CheckableTag
            key={choice.value}
            checked={value.includes(choice.value)}
            onChange={(checked) => toggle(choice.value, checked)}
          >
            {choice.label}
          </Tag.CheckableTag>
        ))}
      </Space>
      {allowCustom && !disabled && (
        <Space.Compact style={{ width: '100%' }}>
          <Input
            value={custom}
            onChange={(event) => setCustom(event.target.value)}
            onPressEnter={addCustom}
            placeholder={customPlaceholder ?? t('addAnotherValue')}
            maxLength={100}
            aria-label={customPlaceholder ?? t('addAnotherValue')}
          />
          <Button icon={<PlusOutlined />} onClick={addCustom} aria-label={t('add')}>
            {t('add')}
          </Button>
        </Space.Compact>
      )}
    </Space>
  );
};

export default ChoiceChips;
