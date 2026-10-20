import type { CSSProperties } from 'react';
import { Select } from 'antd';
import { useTranslation } from 'react-i18next';

export interface FormTemplate<T> {
  key: string;
  label: string;
  /** Optional second line in the dropdown, for what the template is for. */
  description?: string;
  values: Partial<T>;
}

interface TemplatePickerProps<T> {
  templates: FormTemplate<T>[];
  value?: string;
  /**
   * Receives the template's values, or `undefined` when the user chooses Custom.
   * The page decides how to apply them (usually `form.setFieldsValue`), so a
   * template never has to know which form library is in use.
   */
  onApply?: (values: Partial<T> | undefined, template?: FormTemplate<T>) => void;
  onChange?: (key?: string) => void;
  placeholder?: string;
  disabled?: boolean;
  style?: CSSProperties;
}

const CUSTOM = '__custom__';

/**
 * Prefill a form from a named template, with Custom as an explicit way out.
 *
 * The picker is deliberately just a select: templates are data the page owns, not a
 * wizard. Applying values is left to the caller so this stays usable on every form.
 */
function TemplatePicker<T>({
  templates,
  value,
  onApply,
  onChange,
  placeholder,
  disabled,
  style,
}: TemplatePickerProps<T>) {
  const { t } = useTranslation('common');

  return (
    <Select
      value={value}
      placeholder={placeholder ?? t('startFromTemplate')}
      disabled={disabled}
      style={{ width: '100%', ...style }}
      options={[
        ...templates.map((template) => ({
          value: template.key,
          label: template.description
            ? `${template.label} — ${template.description}`
            : template.label,
        })),
        { value: CUSTOM, label: t('custom') },
      ]}
      onChange={(key?: string) => {
        onChange?.(key);
        if (!key || key === CUSTOM) {
          onApply?.(undefined);
          return;
        }
        const template = templates.find((candidate) => candidate.key === key);
        onApply?.(template?.values, template);
      }}
    />
  );
}

export default TemplatePicker;
