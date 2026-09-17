import { useState, type CSSProperties, type ReactElement } from 'react';
import { Button, Divider, Form, Input, InputNumber, Modal, Select, Switch, message } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { getApiError } from '../api/farmApi';

export interface QuickAddFieldSpec {
  name: string;
  label: string;
  widget: 'input' | 'textarea' | 'number' | 'select' | 'switch';
  required?: boolean;
  maxLength?: number;
  placeholder?: string;
  min?: number;
  max?: number;
  options?: { value: string | number; label: string }[];
  initialValue?: unknown;
}

interface LookupQuickAddSelectProps {
  /** Shown on the footer button ("Add Breed") and as the modal title. */
  label: string;
  options: { value: string; label: string }[];
  /** Injected by Form.Item. */
  value?: string;
  onChange?: (value?: string) => void;
  /** Modal form fields (with initial values). */
  fields: QuickAddFieldSpec[];
  /**
   * Create the lookup on the server. Must return the new option id.
   * The parent is responsible for adding the created option to `options`.
   * Throwing keeps the modal open and surfaces the error as a toast.
   */
  onQuickAdd: (values: Record<string, unknown>) => Promise<string>;
  /** Called when the select value changes (user pick or quick-add). */
  onValueSelected?: (value?: string) => void;
  disabled?: boolean;
  /** Disables only the inline quick-add (the select itself stays openable). */
  addDisabled?: boolean;
  /** Shown in the popup footer instead of the Add button when addDisabled. */
  disabledHint?: string;
  placeholder?: string;
  allowClear?: boolean;
  popupMatchSelectWidth?: boolean;
  style?: CSSProperties;
}

const LookupQuickAddSelect = ({
  label,
  options,
  value,
  onChange,
  fields,
  onQuickAdd,
  onValueSelected,
  disabled = false,
  addDisabled = false,
  disabledHint,
  placeholder,
  allowClear = false,
  popupMatchSelectWidth,
  style,
}: LookupQuickAddSelectProps) => {
  const [modalOpen, setModalOpen] = useState(false);
  const [creating, setCreating] = useState(false);
  const [form] = Form.useForm();

  const canAdd = !disabled && !addDisabled;
  const initialValues = Object.fromEntries(
    fields.filter((f) => f.initialValue !== undefined).map((f) => [f.name, f.initialValue]),
  );

  const openModal = () => {
    form.resetFields();
    form.setFieldsValue(initialValues);
    setModalOpen(true);
  };

  const handleCreate = async () => {
    try {
      const values = await form.validateFields();
      setCreating(true);
      // Throws on API failure — the modal stays open with the input intact.
      const createdId = await onQuickAdd(values as Record<string, unknown>);
      onChange?.(createdId);
      onValueSelected?.(createdId);
      message.success(`${label} "${values[fields[0].name]}" created`);
      setModalOpen(false);
      form.resetFields();
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return; // client validation
      message.error(getApiError(err));
    } finally {
      setCreating(false);
    }
  };

  return (
    <>
      <Select
        value={value}
        onChange={(v?: string) => {
          onChange?.(v);
          onValueSelected?.(v);
        }}
        options={options}
        showSearch
        optionFilterProp="label"
        allowClear={allowClear}
        disabled={disabled}
        placeholder={placeholder ?? `Select ${label.toLowerCase()}`}
        popupMatchSelectWidth={popupMatchSelectWidth}
        style={{ width: '100%', ...style }}
        popupRender={(menu: ReactElement) => (
          <>
            {menu}
            <Divider style={{ margin: '4px 0' }} />
            <div style={{ padding: '0 8px 4px' }}>
              {canAdd ? (
                <Button
                  type="link"
                  size="small"
                  icon={<PlusOutlined />}
                  onClick={openModal}
                  data-testid={`quick-add-${label}`}
                >
                  Add {label}
                </Button>
              ) : (
                <span style={{ color: '#999', fontSize: 12 }}>{disabledHint}</span>
              )}
            </div>
          </>
        )}
      />

      <Modal
        title={`Add ${label}`}
        open={modalOpen}
        onOk={handleCreate}
        onCancel={() => setModalOpen(false)}
        confirmLoading={creating}
        okText={`Add ${label}`}
        destroyOnClose
        width={420}
        maskClosable={false}
      >
        <Form form={form} layout="vertical">
          {fields.map((f) => (
            <Form.Item
              key={f.name}
              name={f.name}
              label={f.label}
              valuePropName={f.widget === 'switch' ? 'checked' : 'value'}
              rules={f.required ? [{ required: true, message: `${f.label} is required` }] : undefined}
            >
              {f.widget === 'input' && <Input maxLength={f.maxLength ?? 100} placeholder={f.placeholder} />}
              {f.widget === 'textarea' && <Input.TextArea rows={2} maxLength={f.maxLength ?? 2000} placeholder={f.placeholder} />}
              {f.widget === 'number' && <InputNumber min={f.min} max={f.max} style={{ width: '100%' }} />}
              {f.widget === 'select' && (
                <Select options={f.options ?? []} placeholder={f.placeholder} style={{ width: '100%' }} />
              )}
              {f.widget === 'switch' && <Switch />}
            </Form.Item>
          ))}
        </Form>
      </Modal>
    </>
  );
};

export default LookupQuickAddSelect;
