import React, { useState, useEffect, useCallback } from 'react';
import { Table, Card, Select, Input, DatePicker, Tag, Button, Space, Typography } from 'antd';
import { ReloadOutlined, SearchOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import { auditLogsApi } from '../../api/auditLogs';
import type { AuditLogEntry, PagedResult } from '../../api/auditLogs';
import { useFarmStore } from '../../stores/farmStore';
import { useTranslation } from 'react-i18next';

const { RangePicker } = DatePicker;
const { Text } = Typography;

const actionColors: Record<string, string> = {
  Create: 'green',
  Update: 'blue',
  Delete: 'red',
};

const entityTypes = [
  'Animal', 'WeightRecord', 'FeedRecord', 'FeedType', 'DietPlan',
  'Employee', 'Expense', 'IncomeRecord', 'MedicalRecord', 'Medicine',
  'VaccinationRecord', 'BreedingRecord', 'BirthRecord', 'InventoryItem',
  'StockMovement', 'Supplier', 'Customer', 'FarmTask', 'SalaryPayment',
  'AttendanceRecord', 'PerformanceReview',
];

const AuditLogPage: React.FC = () => {const { t: translate } = useTranslation('admin'); 
  const { activeFarm } = useFarmStore();
  const [data, setData] = useState<PagedResult<AuditLogEntry> | null>(null);
  const [loading, setLoading] = useState(false);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [entityType, setEntityType] = useState<string | undefined>();
  const [action, setAction] = useState<string | undefined>();
  const [search, setSearch] = useState('');
  const [dateRange, setDateRange] = useState<[dayjs.Dayjs | null, dayjs.Dayjs | null] | null>(null);

  const fetchData = useCallback(async () => {
    if (!activeFarm) return;
    setLoading(true);
    try {
      const params: Record<string, unknown> = { page, pageSize };
      if (entityType) params.entityType = entityType;
      if (action) params.action = action;
      if (search) params.search = search;
      if (dateRange?.[0]) params.fromDate = dateRange[0].toISOString();
      if (dateRange?.[1]) params.toDate = dateRange[1].toISOString();
      const result = await auditLogsApi.getLogs(activeFarm.id, params);
      setData(result.data);
    } catch {
      // handled by axios interceptor
    } finally {
      setLoading(false);
    }
  }, [activeFarm, page, pageSize, entityType, action, search, dateRange]);

  useEffect(() => {
    fetchData();
  }, [fetchData]);

  const columns: ColumnsType<AuditLogEntry> = [
    {
      title: translate('timestamp'),
      dataIndex: 'timestamp',
      key: 'timestamp',
      width: 180,
      render: (v: string) => dayjs(v).format('YYYY-MM-DD HH:mm:ss'),
    },
    {
      title: translate('user'),
      dataIndex: 'userEmail',
      key: 'userEmail',
      width: 180,
      render: (v: string | null) => v || <Text type="secondary">{translate('system')}</Text>,
    },
    {
      title: translate('action'),
      dataIndex: 'action',
      key: 'action',
      width: 100,
      render: (v: string) => <Tag color={actionColors[v] || 'default'}>{v}</Tag>,
    },
    {
      title: translate('entityType'),
      dataIndex: 'entityType',
      key: 'entityType',
      width: 150,
    },
    {
      title: translate('entityId'),
      dataIndex: 'entityId',
      key: 'entityId',
      width: 120,
      ellipsis: true,
      render: (v: string) => <Text code copyable>{v}</Text>,
    },
  ];

  const expandedRowRender = (record: AuditLogEntry) => {
    let oldValues: Record<string, unknown> | null = null;
    let newValues: Record<string, unknown> | null = null;
    try { if (record.oldValues) oldValues = JSON.parse(record.oldValues); } catch { /* ignore */ }
    try { if (record.newValues) newValues = JSON.parse(record.newValues); } catch { /* ignore */ }

    const allKeys = new Set([
      ...Object.keys(oldValues || {}),
      ...Object.keys(newValues || {}),
    ]);

    if (allKeys.size === 0) return <Text type="secondary">{translate('noValueDetails')}</Text>;

    return (
      <table style={{ width: '100%', fontSize: 13, borderCollapse: 'collapse' }}>
        <thead>
          <tr style={{ borderBottom: '1px solid #f0f0f0' }}>
            <th style={{ textAlign: 'left', padding: '4px 8px' }}>{translate('field')}</th>
            <th style={{ textAlign: 'left', padding: '4px 8px' }}>{translate('oldValue')}</th>
            <th style={{ textAlign: 'left', padding: '4px 8px' }}>{translate('newValue')}</th>
          </tr>
        </thead>
        <tbody>
          {Array.from(allKeys).map((key) => (
            <tr key={key} style={{ borderBottom: '1px solid #f5f5f5' }}>
              <td style={{ padding: '4px 8px', fontWeight: 500 }}>{key}</td>
              <td style={{ padding: '4px 8px', color: '#ff4d4f' }}>
                {oldValues && key in oldValues ? String(oldValues[key] ?? '') : <Text type="secondary">—</Text>}
              </td>
              <td style={{ padding: '4px 8px', color: '#52c41a' }}>
                {newValues && key in newValues ? String(newValues[key] ?? '') : <Text type="secondary">—</Text>}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    );
  };

  return (
    <Card title={translate('auditLog')} extra={
      <Button icon={<ReloadOutlined />} onClick={fetchData}>{translate('refresh')}</Button>
    }>
      <Space wrap style={{ marginBottom: 16 }}>
        <Select
          placeholder={translate('entityType')}
          allowClear
          style={{ width: 180 }}
          value={entityType}
          onChange={setEntityType}
          options={entityTypes.map(t => ({ label: t, value: t }))}
        />
        <Select
          placeholder={translate('action')}
          allowClear
          style={{ width: 120 }}
          value={action}
          onChange={setAction}
          options={[
            { label: 'Create', value: 'Create' },
            { label: 'Update', value: 'Update' },
            { label: 'Delete', value: 'Delete' },
          ]}
        />
        <Input
          placeholder={translate('search')}
          prefix={<SearchOutlined />}
          style={{ width: 200 }}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          onPressEnter={fetchData}
          allowClear
        />
        <RangePicker
          value={dateRange as [dayjs.Dayjs, dayjs.Dayjs] | null}
          onChange={(dates) => setDateRange(dates as [dayjs.Dayjs | null, dayjs.Dayjs | null] | null)}
        />
      </Space>

      <Table
        rowKey="id"
        columns={columns}
        dataSource={data?.items || []}
        loading={loading}
        expandable={{ expandedRowRender }}
        pagination={{
          current: page,
          pageSize,
          total: data?.totalCount || 0,
          showSizeChanger: true,
          pageSizeOptions: ['10', '20', '50', '100'],
          showTotal: (total) => `Total ${total} records`,
          onChange: (p, ps) => { setPage(p); setPageSize(ps); },
        }}
        size="small"
      />
    </Card>
  );
};

export default AuditLogPage;
