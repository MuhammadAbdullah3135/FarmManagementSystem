import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, Form, Input, Modal, Popconfirm, Space, Switch, Table, Tag, message,
} from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { vetsApi } from '../../api/health';
import type { VetInput, VetUpdateInput } from '../../api/health';
import { getApiError } from '../../api/farmApi';
import type { Vet } from '../../types';
import { useTranslation } from 'react-i18next';

interface VetFormValues extends VetInput {
  isActive?: boolean;
}

const VetsPage: React.FC = () => {
  const { t } = useTranslation(['health', 'common']);
  const [vets, setVets] = useState<Vet[]>([]);
  const [loading, setLoading] = useState(false);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<Vet | null>(null);
  const [saving, setSaving] = useState(false);
  const [form] = Form.useForm<VetFormValues>();

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const response = await vetsApi.list(true);
      setVets(response.data);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    form.setFieldsValue({ isActive: true });
    setModalOpen(true);
  };

  const openEdit = (vet: Vet) => {
    setEditing(vet);
    form.setFieldsValue({
      name: vet.name,
      phone: vet.phone,
      email: vet.email,
      licenseNumber: vet.licenseNumber,
      notes: vet.notes,
      isActive: vet.isActive,
    });
    setModalOpen(true);
  };

  const save = async () => {
    let values: VetFormValues;
    try {
      values = await form.validateFields();
    } catch {
      return;
    }

    setSaving(true);
    try {
      const input: VetInput = {
        name: values.name.trim(),
        phone: values.phone?.trim() || undefined,
        email: values.email?.trim() || undefined,
        licenseNumber: values.licenseNumber?.trim() || undefined,
        notes: values.notes?.trim() || undefined,
      };

      if (editing) {
        const update: VetUpdateInput = {
          ...input,
          isActive: values.isActive ?? editing.isActive,
        };
        await vetsApi.update(editing.id, update);
        message.success(t('vetUpdated'));
      } else {
        await vetsApi.create(input);
        message.success(t('vetCreated'));
      }

      setModalOpen(false);
      setEditing(null);
      form.resetFields();
      await load();
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setSaving(false);
    }
  };

  const setActive = async (vet: Vet, isActive: boolean) => {
    try {
      await vetsApi.update(vet.id, {
        name: vet.name,
        phone: vet.phone,
        email: vet.email,
        licenseNumber: vet.licenseNumber,
        notes: vet.notes,
        isActive,
      });
      message.success(t(isActive ? 'vetActivated' : 'vetDeactivated'));
      await load();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const remove = async (vet: Vet) => {
    try {
      await vetsApi.remove(vet.id);
      message.success(t('vetDeleted'));
      await load();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ColumnsType<Vet> = [
    { title: t('name'), dataIndex: 'name', key: 'name' },
    { title: t('phone'), dataIndex: 'phone', key: 'phone', render: (v?: string) => v || '—' },
    { title: t('email'), dataIndex: 'email', key: 'email', render: (v?: string) => v || '—' },
    {
      title: t('licenseNumber'),
      dataIndex: 'licenseNumber',
      key: 'licenseNumber',
      render: (v?: string) => v || '—',
    },
    {
      title: t('active'),
      dataIndex: 'isActive',
      key: 'isActive',
      render: (isActive: boolean) => (
        <Tag color={isActive ? 'green' : 'default'}>
          {t(isActive ? 'active' : 'inactive')}
        </Tag>
      ),
    },
    { title: t('notes'), dataIndex: 'notes', key: 'notes', render: (v?: string) => v || '—' },
    {
      title: t('actions'),
      key: 'actions',
      render: (_, vet) => (
        <Space>
          <Button size="small" onClick={() => openEdit(vet)}>
            {t('edit')}
          </Button>
          <Button size="small" onClick={() => setActive(vet, !vet.isActive)}>
            {t(vet.isActive ? 'deactivate' : 'activate')}
          </Button>
          <Popconfirm title={t('deleteThisVet')} onConfirm={() => remove(vet)}>
            <Button size="small" danger>
              {t('delete')}
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <Card
      title={t('vets')}
      extra={(
        <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
          {t('addVet')}
        </Button>
      )}
    >
      <Table
        rowKey="id"
        columns={columns}
        dataSource={vets}
        loading={loading}
        pagination={false}
        size="middle"
      />

      <Modal
        open={modalOpen}
        title={editing ? t('editVet') : t('addVet')}
        onCancel={() => {
          setModalOpen(false);
          setEditing(null);
          form.resetFields();
        }}
        onOk={save}
        confirmLoading={saving}
        destroyOnClose
      >
        <Form form={form} layout="vertical">
          <Form.Item
            name="name"
            label={t('name')}
            rules={[{ required: true, whitespace: true, message: t('validation:validation.vetNameRequired') }]}
          >
            <Input maxLength={200} />
          </Form.Item>
          <Form.Item name="phone" label={t('phone')}>
            <Input maxLength={50} />
          </Form.Item>
          <Form.Item name="email" label={t('email')}>
            <Input maxLength={200} />
          </Form.Item>
          <Form.Item name="licenseNumber" label={t('licenseNumber')}>
            <Input maxLength={100} />
          </Form.Item>
          {editing && (
            <Form.Item name="isActive" label={t('active')} valuePropName="checked">
              <Switch />
            </Form.Item>
          )}
          <Form.Item name="notes" label={t('notes')}>
            <Input.TextArea rows={3} maxLength={2000} />
          </Form.Item>
        </Form>
      </Modal>
    </Card>
  );
};

export default VetsPage;
