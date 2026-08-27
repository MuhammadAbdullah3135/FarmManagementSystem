import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, Form, Input, Modal, Popconfirm, Select, Space, Table, message,
} from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { vaccineTypesApi, medicinesApi } from '../../api/health';
import { getApiError } from '../../api/farmApi';
import type { VaccineTypeListItem, MedicineListItem } from '../../types';

const VaccineTypesPage: React.FC = () => {
  const [types, setTypes] = useState<VaccineTypeListItem[]>([]);
  const [medicines, setMedicines] = useState<MedicineListItem[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [search, setSearch] = useState('');
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<VaccineTypeListItem | null>(null);
  const [form] = Form.useForm();

  const load = useCallback(async (p: number) => {
    setLoading(true);
    try {
      const [typesRes, medsRes] = await Promise.all([
        vaccineTypesApi.list({ page: p, pageSize: 10, search: search || undefined }),
        medicinesApi.list({ page: 1, pageSize: 100 }),
      ]);
      setTypes(typesRes.data.items);
      setTotal(typesRes.data.totalCount);
      setPage(p);
      setMedicines(medsRes.data.items);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, [search]);

  useEffect(() => {
    const timer = window.setTimeout(() => { load(1); }, 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    setModalOpen(true);
  };

  const openEdit = async (record: VaccineTypeListItem) => {
    try {
      const res = await vaccineTypesApi.get(record.id);
      const data = res.data;
      setEditing(record);
      form.setFieldsValue({
        name: data.name,
        defaultDosage: data.defaultDosage,
        notes: data.notes,
        linkedMedicineId: data.linkedMedicineId,
      });
      setModalOpen(true);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const handleSave = async () => {
    try {
      const values = await form.validateFields();
      const data = {
        name: values.name,
        defaultDosage: values.defaultDosage,
        notes: values.notes,
        linkedMedicineId: values.linkedMedicineId || undefined,
      };
      if (editing) {
        await vaccineTypesApi.update(editing.id, data);
        message.success('Vaccine type updated');
      } else {
        await vaccineTypesApi.create(data);
        message.success('Vaccine type created');
      }
      setModalOpen(false);
      load(page);
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await vaccineTypesApi.remove(id);
      message.success('Vaccine type deleted');
      load(page);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ColumnsType<VaccineTypeListItem> = [
    { title: 'Name', dataIndex: 'name', ellipsis: true },
    { title: 'Default Dosage', dataIndex: 'defaultDosage', render: (d?: string) => d || '-' },
    {
      title: 'Linked Medicine',
      dataIndex: 'linkedMedicineName',
      render: (n?: string) => n || '-',
    },
    { title: 'Vaccinations', dataIndex: 'vaccinationCount', width: 110 },
    {
      title: 'Actions',
      width: 140,
      render: (_, r) => (
        <Space size={4}>
          <Button size="small" onClick={() => openEdit(r)}>Edit</Button>
          <Popconfirm title="Delete this vaccine type?" onConfirm={() => handleDelete(r.id)}>
            <Button size="small" danger>Delete</Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <>
      <Card
        title="Vaccine Types"
        extra={
          <Space>
            <Input.Search
              placeholder="Search vaccines..."
              allowClear
              onSearch={(v) => setSearch(v)}
              style={{ width: 220 }}
            />
            <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>Add Vaccine Type</Button>
          </Space>
        }
      >
        <Table
          rowKey="id"
          columns={columns}
          dataSource={types}
          loading={loading}
          pagination={{ current: page, total, pageSize: 10, onChange: load }}
        />
      </Card>

      <Modal
        title={editing ? 'Edit Vaccine Type' : 'Add Vaccine Type'}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => setModalOpen(false)}
        destroyOnClose
      >
        <Form form={form} layout="vertical">
          <Form.Item name="name" label="Name" rules={[{ required: true, message: 'Vaccine name is required' }]}>
            <Input maxLength={200} placeholder="e.g. FMD Vaccine" />
          </Form.Item>
          <Form.Item name="defaultDosage" label="Default Dosage">
            <Input maxLength={200} placeholder="e.g. 5ml" />
          </Form.Item>
          <Form.Item name="linkedMedicineId" label="Linked Medicine (optional)">
            <Select
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder="Select medicine for stock tracking"
              options={medicines.map((m) => ({ value: m.id, label: m.name }))}
            />
          </Form.Item>
          <Form.Item name="notes" label="Notes">
            <Input.TextArea rows={2} maxLength={1000} />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
};

export default VaccineTypesPage;
