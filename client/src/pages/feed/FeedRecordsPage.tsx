import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, DatePicker, Form, Input, InputNumber, Modal, Popconfirm, Radio, Select, Space, Table, message,
} from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs, { Dayjs } from 'dayjs';
import { feedRecordsApi, feedTypesApi } from '../../api/feed';
import { lookupsApi } from '../../api/attendance';
import { getApiError } from '../../api/farmApi';
import type { FeedRecord, FeedType } from '../../types';

interface AnimalOption {
  id: string;
  tagNumber: string;
  name?: string;
}

interface LocationOption {
  id: string;
  name: string;
}

const FeedRecordsPage: React.FC = () => {
  const [records, setRecords] = useState<FeedRecord[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [feedTypes, setFeedTypes] = useState<FeedType[]>([]);
  const [animals, setAnimals] = useState<AnimalOption[]>([]);
  const [locations, setLocations] = useState<LocationOption[]>([]);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<FeedRecord | null>(null);
  const [targetMode, setTargetMode] = useState<'animal' | 'location'>('animal');
  const [filters, setFilters] = useState<{ feedTypeId?: string; range?: [Dayjs | null, Dayjs | null] }>({});
  const [form] = Form.useForm();

  const loadLookups = useCallback(async () => {
    try {
      const [typesRes, animalsRes, locationsRes] = await Promise.all([
        feedTypesApi.list(),
        lookupsApi.animals(),
        lookupsApi.locations(),
      ]);
      setFeedTypes(typesRes.data);
      setAnimals(animalsRes.data.items);
      setLocations(locationsRes.data);
    } catch (err) {
      message.error(getApiError(err));
    }
  }, []);

  const loadRecords = useCallback(async (p: number) => {
    setLoading(true);
    try {
      const res = await feedRecordsApi.list({
        feedTypeId: filters.feedTypeId,
        from: filters.range?.[0]?.toISOString(),
        to: filters.range?.[1]?.toISOString(),
        page: p,
        pageSize: 10,
      });
      setRecords(res.data.items);
      setTotal(res.data.totalCount);
      setPage(p);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, [filters]);

  useEffect(() => {
    loadLookups();
  }, [loadLookups]);

  useEffect(() => {
    loadRecords(1);
  }, [loadRecords]);

  const openCreate = () => {
    setEditing(null);
    setTargetMode('animal');
    form.resetFields();
    form.setFieldsValue({ fedAt: dayjs() });
    setModalOpen(true);
  };

  const openEdit = (record: FeedRecord) => {
    setEditing(record);
    setTargetMode(record.animalId ? 'animal' : 'location');
    form.resetFields();
    form.setFieldsValue({
      quantity: record.quantity,
      fedAt: dayjs(record.fedAt),
      notes: record.notes,
    });
    setModalOpen(true);
  };

  const handleSave = async () => {
    try {
      const values = await form.validateFields();
      if (editing) {
        await feedRecordsApi.update(editing.id, {
          quantity: values.quantity,
          fedAt: values.fedAt?.toISOString(),
          notes: values.notes,
        });
        message.success('Feed record updated');
      } else {
        await feedRecordsApi.create({
          feedTypeId: values.feedTypeId,
          animalId: targetMode === 'animal' ? values.animalId : undefined,
          locationId: targetMode === 'location' ? values.locationId : undefined,
          quantity: values.quantity,
          fedAt: values.fedAt?.toISOString(),
          notes: values.notes,
        });
        message.success('Feeding recorded');
      }
      setModalOpen(false);
      loadRecords(page);
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await feedRecordsApi.remove(id);
      message.success('Feed record deleted');
      loadRecords(page);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ColumnsType<FeedRecord> = [
    { title: 'Fed At', dataIndex: 'fedAt', render: (d: string) => dayjs(d).format('YYYY-MM-DD HH:mm') },
    { title: 'Feed Type', dataIndex: 'feedTypeName' },
    {
      title: 'Target',
      render: (_, record) =>
        record.animalId
          ? `${record.animalTagNumber}${record.animalName ? ` (${record.animalName})` : ''}`
          : record.locationName ?? '-',
    },
    { title: 'Quantity', dataIndex: 'quantity', align: 'right', render: (v: number, r) => `${v} ${r.unitName}` },
    { title: 'Cost', dataIndex: 'totalCost', align: 'right' },
    { title: 'Notes', dataIndex: 'notes', ellipsis: true },
    {
      title: 'Actions',
      render: (_, record) => (
        <Space>
          <Button size="small" onClick={() => openEdit(record)}>Edit</Button>
          <Popconfirm title="Restore stock and delete this record?" onConfirm={() => handleDelete(record.id)}>
            <Button size="small" danger>Delete</Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <Card
      title="Daily Feed Records"
      extra={
        <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
          Record Feeding
        </Button>
      }
    >
      <Space wrap style={{ marginBottom: 16 }}>
        <Select
          allowClear
          placeholder="Filter by feed type"
          style={{ width: 200 }}
          value={filters.feedTypeId}
          onChange={(v) => setFilters((f) => ({ ...f, feedTypeId: v }))}
          options={feedTypes.map((t) => ({ value: t.id, label: t.name }))}
        />
        <DatePicker.RangePicker
          value={filters.range}
          onChange={(range) => setFilters((f) => ({ ...f, range }))}
        />
      </Space>

      <Table
        rowKey="id"
        columns={columns}
        dataSource={records}
        loading={loading}
        pagination={{ current: page, total, pageSize: 10, onChange: loadRecords }}
      />

      <Modal
        title={editing ? 'Edit Feed Record' : 'Record Feeding'}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => setModalOpen(false)}
        destroyOnClose
      >
        <Form form={form} layout="vertical">
          {!editing && (
            <>
              <Form.Item label="Feed Target">
                <Radio.Group
                  value={targetMode}
                  onChange={(e) => setTargetMode(e.target.value)}
                  options={[
                    { value: 'animal', label: 'Individual animal' },
                    { value: 'location', label: 'Location (group)' },
                  ]}
                  optionType="button"
                  buttonStyle="solid"
                />
              </Form.Item>
              <Form.Item name="feedTypeId" label="Feed Type" rules={[{ required: true }]}>
                <Select options={feedTypes.map((t) => ({ value: t.id, label: `${t.name} (${t.unitName})` }))} />
              </Form.Item>
              {targetMode === 'animal' ? (
                <Form.Item name="animalId" label="Animal" rules={[{ required: true }]}>
                  <Select
                    showSearch
                    optionFilterProp="label"
                    options={animals.map((a) => ({
                      value: a.id,
                      label: a.name ? `${a.tagNumber} — ${a.name}` : a.tagNumber,
                    }))}
                  />
                </Form.Item>
              ) : (
                <Form.Item name="locationId" label="Location" rules={[{ required: true }]}>
                  <Select options={locations.map((l) => ({ value: l.id, label: l.name }))} />
                </Form.Item>
              )}
            </>
          )}
          <Form.Item name="quantity" label="Quantity" rules={[{ required: true }]}>
            <InputNumber min={0.01} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="fedAt" label="Fed At" rules={[{ required: true }]}>
            <DatePicker showTime style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="notes" label="Notes">
            <Input.TextArea rows={2} maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>
    </Card>
  );
};

export default FeedRecordsPage;
