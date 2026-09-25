import React, { useCallback, useEffect, useState } from 'react';
import { formatDate } from '../../i18n/format';
import {
  Button, Card, DatePicker, Modal, Select, Space, Table, Tag, message,
} from 'antd';
import { ThunderboltOutlined, CheckOutlined, CloseOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs, { Dayjs } from 'dayjs';
import { feedingTasksApi } from '../../api/feed';
import { getApiError } from '../../api/farmApi';
import type { DietPlanItem, FeedingTask, FeedingTaskStatus } from '../../types';
import { useTranslation } from 'react-i18next';

const STATUS_COLORS: Record<FeedingTaskStatus, string> = {
  Pending: 'gold',
  Completed: 'green',
  Skipped: 'red',
};

const FeedingTasksPage: React.FC = () => {const { t } = useTranslation('feed'); 
  const [tasks, setTasks] = useState<FeedingTask[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [date, setDate] = useState<Dayjs | null>(dayjs());
  const [statusFilter, setStatusFilter] = useState<string | undefined>();
  const [genLoading, setGenLoading] = useState(false);
  const [notesTarget, setNotesTarget] = useState<{ id: string; action: 'complete' | 'skip' } | null>(null);
  const [notesValue, setNotesValue] = useState('');

  const load = useCallback(async (p: number) => {
    setLoading(true);
    try {
      const res = await feedingTasksApi.list({
        date: date?.format('YYYY-MM-DD'),
        status: statusFilter,
        page: p,
        pageSize: 10,
      });
      setTasks(res.data.items);
      setTotal(res.data.totalCount);
      setPage(p);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, [date, statusFilter]);

  useEffect(() => {
    const timer = window.setTimeout(() => { load(1); }, 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  const handleGenerate = async () => {
    if (!date) { message.warning(t('pickADate')); return; }
    setGenLoading(true);
    try {
      await feedingTasksApi.generate(date.format('YYYY-MM-DD'));
      message.success(`Tasks generated for ${date.format('YYYY-MM-DD')}`);
      load(1);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setGenLoading(false);
    }
  };

  const handleAction = async () => {
    if (!notesTarget) return;
    try {
      if (notesTarget.action === 'complete') {
        await feedingTasksApi.complete(notesTarget.id, notesValue || undefined);
        message.success(t('taskCompleted'));
      } else {
        await feedingTasksApi.skip(notesTarget.id, notesValue || undefined);
        message.success(t('taskSkipped'));
      }
      setNotesTarget(null);
      setNotesValue('');
      load(page);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ColumnsType<FeedingTask> = [
    { title: t('dietPlan'), dataIndex: 'dietPlanName' },
    { title: t('date'), dataIndex: 'taskDate', render: (d: string) => formatDate(d) },
    { title: t('time'), dataIndex: 'timeOfDay' },
    { title: t('target'), dataIndex: 'targetAnimalCount', align: 'center' },
    {
      title: t('status'),
      dataIndex: 'status',
      render: (s: FeedingTaskStatus) => <Tag color={STATUS_COLORS[s]}>{s}</Tag>,
    },
    {
      title: t('items'),
      render: (_, record) => record.items.length,
    },
    {
      title: t('actions'),
      render: (_, record) =>
        record.status === 'Pending' ? (
          <Space>
            <Button
              size="small"
              type="primary"
              icon={<CheckOutlined />}
              onClick={() => setNotesTarget({ id: record.id, action: 'complete' })}
            >
              {t('complete')}
            </Button>
            <Button
              size="small"
              danger
              icon={<CloseOutlined />}
              onClick={() => setNotesTarget({ id: record.id, action: 'skip' })}
            >
              {t('skip')}
            </Button>
          </Space>
        ) : record.status === 'Completed' ? (
          <span style={{ color: '#52c41a' }}>{t('doneAt')} {dayjs(record.completedAt).format('HH:mm')}</span>
        ) : (
          <span style={{ color: '#999' }}>{t('skipped')}</span>
        ),
    },
  ];

  return (
    <Card
      title={t('feedingTasks')}
      extra={
        <Space>
          <DatePicker value={date} onChange={setDate} />
          <Select
            allowClear
            placeholder={t('status')}
            style={{ width: 120 }}
            value={statusFilter}
            onChange={setStatusFilter}
            options={['Pending', 'Completed', 'Skipped'].map((s) => ({ value: s, label: s }))}
          />
          <Button icon={<ThunderboltOutlined />} loading={genLoading} onClick={handleGenerate}>
            {t('generate')}
          </Button>
        </Space>
      }
    >
      <Table
        rowKey="id"
        columns={columns}
        dataSource={tasks}
        loading={loading}
        pagination={{ current: page, total, pageSize: 10, onChange: load }}
        expandable={{
          expandedRowRender: (record) => (
            <Table
              rowKey="id"
              size="small"
              pagination={false}
              columns={[
                { title: 'Feed Type', dataIndex: 'feedTypeName' },
                {
                  title: 'Quantity per Feeding',
                  dataIndex: 'quantityPerFeeding',
                  render: (v: number, r: DietPlanItem) => `${v} ${r.unitName}`,
                },
              ]}
              dataSource={record.items}
            />
          ),
        }}
      />

      <Modal
        title={notesTarget?.action === 'complete' ? t('completeTask') : t('skipTask')}
        open={!!notesTarget}
        onOk={handleAction}
        onCancel={() => setNotesTarget(null)}
      >
        <p>{t('optionalNotes')}</p>
        <textarea
          value={notesValue}
          onChange={(e) => setNotesValue(e.target.value)}
          style={{ width: '100%', minHeight: 80 }}
          maxLength={500}
        />
      </Modal>
    </Card>
  );
};

export default FeedingTasksPage;
