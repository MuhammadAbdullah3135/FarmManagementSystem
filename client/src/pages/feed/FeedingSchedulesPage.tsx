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
import { useTranslation } from 'react-i18next';

const FeedingSchedulesPage: React.FC = () => {const { t } = useTranslation('feed'); 
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
      message.success(t('scheduleCreated'));
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
      message.success(t('scheduleDeleted'));
      load();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const handleGenerate = async () => {
    if (!genDate) { message.warning(t('pickADate')); return; }
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
    { title: t('dietPlan'), dataIndex: 'dietPlanName' },
    { title: t('time'), dataIndex: 'timeOfDay' },
    { title: t('label'), dataIndex: 'label', render: (l?: string) => l ?? '-' },
    {
      title: t('active'),
      dataIndex: 'isActive',
      render: (v: boolean) => v ? <Tag color="green">{t('active')}</Tag> : <Tag>{t('inactive')}</Tag>,
    },
    {
      title: t('actions'),
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
          <Popconfirm title={t('deleteThisSchedule')} onConfirm={() => handleDelete(record.id)}>
            <Button size="small" danger>{t('delete')}</Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <Card
      title={t('feedingSchedules')}
      extra={
        <Space>
          <Space>
            <DatePicker value={genDate} onChange={setGenDate} />
            <Button
              icon={<ThunderboltOutlined />}
              loading={genLoading}
              onClick={handleGenerate}
            >
              {t('generateTasks')}
            </Button>
          </Space>
          <Button type="primary" icon={<PlusOutlined />} onClick={() => { form.resetFields(); setModalOpen(true); }}>
            {t('addSchedule')}
          </Button>
        </Space>
      }
    >
      <Table rowKey="id" columns={columns} dataSource={schedules} loading={loading} pagination={false} />

      <Modal title={t('newFeedingSchedule')} open={modalOpen} onOk={handleCreate} onCancel={() => setModalOpen(false)} destroyOnClose>
        <Form form={form} layout="vertical">
          <Form.Item name="dietPlanId" label={t('dietPlan')} rules={[{ required: true }]}>
            <Select options={plans.map((p) => ({ value: p.id, label: p.name }))} />
          </Form.Item>
          <Form.Item name="timeOfDay" label={t('timeOfDay')} rules={[{ required: true }]}>
            <Input placeholder={t('eG0700')} />
          </Form.Item>
          <Form.Item name="label" label={t('label')}>
            <Input maxLength={100} />
          </Form.Item>
        </Form>
      </Modal>
    </Card>
  );
};

export default FeedingSchedulesPage;
