import { useId, useState, type CSSProperties, type ReactElement } from 'react';
import { Button, Divider, Form, Input, InputNumber, Modal, Select, Switch, message } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { getApiError } from '../api/farmApi';
import {
  getQuickAddSpec,
  type LookupKind,
  type QuickAddContext,
  type QuickAddFieldSpec,
  type QuickAddVariant,
} from './lookupQuickAdd';
import { useTranslation } from 'react-i18next';

export type { QuickAddFieldSpec } from './lookupQuickAdd';

/** The created lookup, normalised so every consumer sees one shape. */
export interface CreatedLookup {
  id: string;
  label: string;
}

interface LookupQuickAddSelectProps {
  /**
   * Registry-backed creation (preferred). When set, the modal fields, the
   * create call and any gating hint come from `lookupQuickAdd`, and `label`
   * is only a fallback.
   */
  kind?: LookupKind;
  /** Values the create depends on (see `QuickAddContext`). */
  ctx?: QuickAddContext;
  /** Only the location chain varies; defaults to the top-level form. */
  variant?: QuickAddVariant;
  /** Shown on the footer button ("Add Breed") and as the modal title. */
  label?: string;
  options: { value: string; label: string }[];
  /** Injected by Form.Item. */
  value?: string;
  onChange?: (value?: string) => void;
  /** Modal form fields (with initial values). Required without `kind`. */
  fields?: QuickAddFieldSpec[];
  /**
   * Create the lookup on the server, returning the new option id. Required
   * without `kind`. The parent is responsible for adding the created option
   * to `options`. Throwing keeps the modal open and surfaces the error.
   */
  onQuickAdd?: (values: Record<string, unknown>) => Promise<string>;
  /**
   * Called with the normalised created lookup *before* it is selected, so the
   * parent can append it or refetch its options. Awaited, so a refetch has
   * landed by the time the value is set. `kind` is the lookup the caller's
   * switch should act on, which also lets nested quick-adds (a Location Type
   * created inside Add Location) reach the same handler.
   */
  onCreated?: (created: CreatedLookup, kind?: LookupKind) => void | Promise<void>;
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
  kind,
  ctx,
  variant,
  label,
  options,
  value,
  onChange,
  fields,
  onQuickAdd,
  onCreated,
  onValueSelected,
  disabled = false,
  addDisabled = false,
  disabledHint,
  placeholder,
  allowClear = false,
  popupMatchSelectWidth,
  style,
}: LookupQuickAddSelectProps) => {const { t } = useTranslation('common'); 
  const [modalOpen, setModalOpen] = useState(false);
  const [creating, setCreating] = useState(false);
  const [form] = Form.useForm();
  // Forms are always mounted (see `forceRender` below), so a page with several
  // quick-adds would otherwise ship duplicate ids: antd derives each field's id
  // from its name, and every one of these forms has a field called `name`.
  const formId = useId();

  const quickAddCtx = ctx ?? {};
  const spec = kind ? getQuickAddSpec(kind, quickAddCtx, variant) : undefined;
  const resolvedLabel = spec?.label ?? label ?? '';
  const resolvedFields = spec?.fields ?? fields ?? [];
  // A dependent lookup (a breed without its animal type) cannot be created yet:
  // the select stays openable, only the Add affordance is replaced by a hint.
  const specReason = spec?.disabledReason?.(quickAddCtx);
  const canAdd = !disabled && !addDisabled && !specReason;
  const hint = addDisabled ? disabledHint : specReason;

  const initialValues = Object.fromEntries(
    resolvedFields
      .filter((f) => f.initialValue !== undefined)
      .map((f) => [f.name, f.initialValue]),
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
      let created: CreatedLookup;
      if (spec) {
        // Throws on API failure — the modal stays open with the input intact.
        created = await spec.create(values as Record<string, unknown>, quickAddCtx);
      } else if (onQuickAdd) {
        const id = await onQuickAdd(values as Record<string, unknown>);
        created = { id, label: String(values[resolvedFields[0]?.name] ?? '') };
      } else {
        throw new Error(`No quick-add handler configured for ${resolvedLabel}`);
      }
      // Awaited before selecting: a parent that refetches its options (the
      // location tree, say) must have the new option in hand first.
      await onCreated?.(created, kind);
      onChange?.(created.id);
      onValueSelected?.(created.id);
      message.success(`${resolvedLabel} "${values[resolvedFields[0]?.name]}" created`);
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
        placeholder={placeholder ?? `Select ${resolvedLabel.toLowerCase()}`}
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
                  data-testid={`quick-add-${resolvedLabel}`}
                >
                  {t('add')} {resolvedLabel}
                </Button>
              ) : (
                <span style={{ color: '#999', fontSize: 12 }}>{hint}</span>
              )}
            </div>
          </>
        )}
      />

      <Modal
        title={`Add ${resolvedLabel}`}
        open={modalOpen}
        onOk={handleCreate}
        onCancel={() => setModalOpen(false)}
        confirmLoading={creating}
        okText={`Add ${resolvedLabel}`}
        destroyOnClose
        // Always mounted so the Form stays connected and openModal's prefill
        // (setFieldsValue) reliably applies even on the first open.
        forceRender
        width={420}
        maskClosable={false}
      >
        <Form form={form} name={formId} layout="vertical">
          {resolvedFields.map((f) => (
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
              {f.widget === 'select' && f.nestedQuickAdd && (
                <LookupQuickAddSelect
                  kind={'kind' in f.nestedQuickAdd ? f.nestedQuickAdd.kind : undefined}
                  ctx={'kind' in f.nestedQuickAdd ? f.nestedQuickAdd.ctx : undefined}
                  variant={'kind' in f.nestedQuickAdd ? f.nestedQuickAdd.variant : undefined}
                  label={f.nestedQuickAdd.label}
                  fields={'kind' in f.nestedQuickAdd ? undefined : f.nestedQuickAdd.fields}
                  onQuickAdd={'kind' in f.nestedQuickAdd ? undefined : f.nestedQuickAdd.onQuickAdd}
                  onCreated={onCreated}
                  options={(f.options ?? []).map((o) => ({ value: String(o.value), label: o.label }))}
                  placeholder={f.placeholder}
                />
              )}
              {f.widget === 'select' && !f.nestedQuickAdd && (
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
