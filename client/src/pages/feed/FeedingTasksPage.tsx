import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, DatePicker, Modal, Select, Space, Table, Tag, message,
} from 'antd';
import { ThunderboltOutlined, CheckOutlined, CloseOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs, { Dayjs } from 'dayjs';
import { feedingTasksApi } from '../../api/feed';
import { getApiError } from '../../api/farmApi';
import type { DietPlanItem, FeedingTask, FeedingTaskStatus } from '../../types';

const STATUS_COLORS: Record<FeedingTaskStatus, string> = {
  Pending: 'gold',
  Completed: 'green',
  Skipped: 'red',
};

const FeedingTasksPage: React.FC = () => {
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
    if (!date) { message.warning('Pick a date'); return; }
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
        message.success('Task completed');
      } else {
        await feedingTasksApi.skip(notesTarget.id, notesValue || undefined);
        message.success('Task skipped');
      }
      setNotesTarget(null);
      setNotesValue('');
      load(page);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ColumnsType<FeedingTask> = [
    { title: 'Diet Plan', dataIndex: 'dietPlanName' },
    { title: 'Date', dataIndex: 'taskDate', render: (d: string) => dayjs(d).format('YYYY-MM-DD') },
    { title: 'Time', dataIndex: 'timeOfDay' },
    { title: 'Target', dataIndex: 'targetAnimalCount', align: 'center' },
    {
      title: 'Status',
      dataIndex: 'status',
      render: (s: FeedingTaskStatus) => <Tag color={STATUS_COLORS[s]}>{s}</Tag>,
    },
    {
      title: 'Items',
      render: (_, record) => record.items.length,
    },
    {
      title: 'Actions',
      render: (_, record) =>
        record.status === 'Pending' ? (
          <Space>
            <Button
              size="small"
              type="primary"
              icon={<CheckOutlined />}
              onClick={() => setNotesTarget({ id: record.id, action: 'complete' })}
            >
              Complete
            </Button>
            <Button
              size="small"
              danger
              icon={<CloseOutlined />}
              onClick={() => setNotesTarget({ id: record.id, action: 'skip' })}
            >
              Skip
            </Button>
          </Space>
        ) : record.status === 'Completed' ? (
          <span style={{ color: '#52c41a' }}>Done at {dayjs(record.completedAt).format('HH:mm')}</span>
        ) : (
          <span style={{ color: '#999' }}>Skipped</span>
        ),
    },
  ];

  return (
    <Card
      title="Feeding Tasks"
      extra={
        <Space>
          <DatePicker value={date} onChange={setDate} />
          <Select
            allowClear
            placeholder="Status"
            style={{ width: 120 }}
            value={statusFilter}
            onChange={setStatusFilter}
            options={['Pending', 'Completed', 'Skipped'].map((s) => ({ value: s, label: s }))}
          />
          <Button icon={<ThunderboltOutlined />} loading={genLoading} onClick={handleGenerate}>
            Generate
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
        title={notesTarget?.action === 'complete' ? 'Complete Task' : 'Skip Task'}
        open={!!notesTarget}
        onOk={handleAction}
        onCancel={() => setNotesTarget(null)}
      >
        <p>Optional notes:</p>
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
