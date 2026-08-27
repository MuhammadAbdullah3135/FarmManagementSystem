import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, DatePicker, Form, Input, Modal, Popconfirm, Select, Space, Switch, Table, Tag, message,
} from 'antd';
import { PlusOutlined, ThunderboltOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs, { Dayjs } from 'dayjs';
import { feedingSchedulesApi, feedingTasksApi, dietPlansApi } from '../../api/feed';
import { getApiError } from '../../api/farmApi';
import type { DietPlan, FeedingSchedule } from '../../types';

const FeedingSchedulesPage: React.FC = () => {
  const [schedules, setSchedules] = useState<FeedingSchedule[]>([]);
  const [plans, setPlans] = useState<DietPlan[]>([]);
  const [loading, setLoading] = useState(false);
  const [modalOpen, setModalOpen] = useState(false);
  const [genDate, setGenDate] = useState<Dayjs | null>(dayjs());
  const [genLoading, setGenLoading] = useState(false);
  const [form] = Form.useForm();

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [schRes, planRes] = await Promise.all([
        feedingSchedulesApi.list(),
        dietPlansApi.list(),
      ]);
      setSchedules(schRes.data);
      setPlans(planRes.data.filter((p) => p.isActive));
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(() => { load(); }, 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  const handleCreate = async () => {
    try {
      const values = await form.validateFields();
      await feedingSchedulesApi.create(values);
      message.success('Schedule created');
      setModalOpen(false);
      load();
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await feedingSchedulesApi.remove(id);
      message.success('Schedule deleted');
      load();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const handleGenerate = async () => {
    if (!genDate) { message.warning('Pick a date'); return; }
    setGenLoading(true);
    try {
      await feedingTasksApi.generate(genDate.format('YYYY-MM-DD'));
      message.success(`Tasks generated for ${genDate.format('YYYY-MM-DD')}`);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setGenLoading(false);
    }
  };

  const columns: ColumnsType<FeedingSchedule> = [
    { title: 'Diet Plan', dataIndex: 'dietPlanName' },
    { title: 'Time', dataIndex: 'timeOfDay' },
    { title: 'Label', dataIndex: 'label', render: (l?: string) => l ?? '-' },
    {
      title: 'Active',
      dataIndex: 'isActive',
      render: (v: boolean) => v ? <Tag color="green">Active</Tag> : <Tag>Inactive</Tag>,
    },
    {
      title: 'Actions',
      render: (_, record) => (
        <Space>
          <Switch
            size="small"
            checked={record.isActive}
            onChange={async (v) => {
              try {
                await feedingSchedulesApi.update(record.id, { isActive: v });
                load();
              } catch (err) {
                message.error(getApiError(err));
              }
            }}
          />
          <Popconfirm title="Delete this schedule?" onConfirm={() => handleDelete(record.id)}>
            <Button size="small" danger>Delete</Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <Card
      title="Feeding Schedules"
      extra={
        <Space>
          <Space>
            <DatePicker value={genDate} onChange={setGenDate} />
            <Button
              icon={<ThunderboltOutlined />}
              loading={genLoading}
              onClick={handleGenerate}
            >
              Generate Tasks
            </Button>
          </Space>
          <Button type="primary" icon={<PlusOutlined />} onClick={() => { form.resetFields(); setModalOpen(true); }}>
            Add Schedule
          </Button>
        </Space>
      }
    >
      <Table rowKey="id" columns={columns} dataSource={schedules} loading={loading} pagination={false} />

      <Modal title="New Feeding Schedule" open={modalOpen} onOk={handleCreate} onCancel={() => setModalOpen(false)} destroyOnClose>
        <Form form={form} layout="vertical">
          <Form.Item name="dietPlanId" label="Diet Plan" rules={[{ required: true }]}>
            <Select options={plans.map((p) => ({ value: p.id, label: p.name }))} />
          </Form.Item>
          <Form.Item name="timeOfDay" label="Time of Day" rules={[{ required: true }]}>
            <Input placeholder="e.g. 07:00" />
          </Form.Item>
          <Form.Item name="label" label="Label">
            <Input maxLength={100} />
          </Form.Item>
        </Form>
      </Modal>
    </Card>
  );
};

export default FeedingSchedulesPage;
