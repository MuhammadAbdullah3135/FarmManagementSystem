import React, { useCallback, useEffect, useState } from 'react';
import { Card, Table, Tag, message } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { formatDate } from '../../i18n/format';
import dayjs from 'dayjs';
import { medicinesApi } from '../../api/health';
import { getApiError } from '../../api/farmApi';
import type { MedicineAlert, MedicineAlertType } from '../../types';
import { useTranslation } from 'react-i18next';

const ALERT_COLORS: Record<MedicineAlertType, string> = {
  Expired: 'red',
  ExpiringSoon: 'orange',
  LowStock: 'gold',
};

const MedicineAlertsPage: React.FC = () => {const { t: translate } = useTranslation('health'); 
  const [alerts, setAlerts] = useState<MedicineAlert[]>([]);
  const [loading, setLoading] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const res = await medicinesApi.alerts();
      setAlerts(res.data);
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

  const columns: ColumnsType<MedicineAlert> = [
    {
      title: translate('alert'),
      dataIndex: 'alertType',
      width: 130,
      render: (t: MedicineAlertType) => <Tag color={ALERT_COLORS[t]}>{t === 'LowStock' ? translate('lowStock') : t === 'ExpiringSoon' ? translate('expiringSoon') : translate('expired')}</Tag>,
    },
    { title: translate('medicine2'), dataIndex: 'medicineName', ellipsis: true },
    {
      title: translate('stock'),
      width: 120,
      render: (_, r) => (
        <span>
          {r.currentQuantity} {r.unit}
          {r.alertType === 'LowStock' && (
            <span style={{ color: '#999' }}> / {r.lowStockThreshold}</span>
          )}
        </span>
      ),
    },
    { title: translate('batch'), dataIndex: 'batchNumber', width: 120 },
    {
      title: translate('expiryDate'),
      dataIndex: 'expiryDate',
      width: 130,
      render: (d: string, r) => {
        if (r.alertType === 'LowStock') return '-';
        const isExpired = dayjs(d).isBefore(dayjs(), 'day');
        return (
          <span style={isExpired ? { color: '#ff4d4f', fontWeight: 600 } : { color: '#fa8c16' }}>
            {formatDate(d)}
          </span>
        );
      },
    },
  ];

  const expiredCount = alerts.filter((a) => a.alertType === 'Expired').length;
  const expiringSoonCount = alerts.filter((a) => a.alertType === 'ExpiringSoon').length;
  const lowStockCount = alerts.filter((a) => a.alertType === 'LowStock').length;

  return (
    <Card
      title={translate('medicineAlerts')}
      extra={
        <div style={{ display: 'flex', gap: 16 }}>
          {expiredCount > 0 && <Tag color="red">{translate('expired2')} {expiredCount}</Tag>}
          {expiringSoonCount > 0 && <Tag color="orange">{translate('expiringSoon2')} {expiringSoonCount}</Tag>}
          {lowStockCount > 0 && <Tag color="gold">{translate('lowStock2')} {lowStockCount}</Tag>}
        </div>
      }
    >
      <Table
        rowKey={(r) => `${r.medicineId}-${r.stockId}-${r.alertType}`}
        columns={columns}
        dataSource={alerts}
        loading={loading}
        pagination={{ pageSize: 20, showSizeChanger: true }}
        rowClassName={(r) =>
          r.alertType === 'Expired' ? 'row-expired' :
          r.alertType === 'ExpiringSoon' ? 'row-expiring' :
          'row-low-stock'
        }
      />
    </Card>
  );
};

export default MedicineAlertsPage;
