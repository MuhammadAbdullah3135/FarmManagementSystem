import React, { useState } from 'react';
import { Button, Input, Modal, Space, Table, Tag, Tooltip, Typography, message } from 'antd';
import {
  ReloadOutlined,
  StopOutlined,
} from '@ant-design/icons';
import dayjs from 'dayjs';
import { markDismissed, requeueMutation } from '../offline/outbox';
import { OUTBOX_STATUS_COLORS, describeItem, kindLabel } from '../offline/mutationKinds';
import { outboxStatusLabel } from '../i18n/vocabulary';
import { requestFlush } from '../offline/syncEngine';
import type { OutboxItem } from '../offline/db';
import { useTranslation } from 'react-i18next';

const { Text } = Typography;

export interface OutboxTableProps {
  items: OutboxItem[];
  /** The animal tag (or other human name) for an item, when the device knows it. */
  targetLabel?: (item: OutboxItem) => string | null;
  /** Hidden where the table is read-only (e.g. the "recently synced" list). */
  showActions?: boolean;
  loading?: boolean;
  emptyText?: string;
}

/**
 * The queue, rendered the same way wherever it appears.
 *
 * A quarantine row shows the server's own message — the exact wording the single-record
 * endpoint would have returned — because that text is the only thing that tells a user what to
 * change. Two ways out of quarantine, both explicit: **Retry** puts the item back in the queue
 * for another attempt, and **Dismiss** requires a reason and keeps the record (with its
 * payload) on the device rather than deleting it. Nothing here can silently discard a
 * measurement.
 */
const OutboxTable: React.FC<OutboxTableProps> = ({
  items,
  targetLabel,
  showActions = true,
  loading = false,
  emptyText = 'Nothing queued on this device.',
}) => {const { t } = useTranslation('common'); 
  const [dismissing, setDismissing] = useState<OutboxItem | null>(null);
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState<string | null>(null);

  const scopeOf = (item: OutboxItem) => ({ accountId: item.accountId, farmId: item.farmId });

  const handleRetry = async (item: OutboxItem) => {
    setBusy(item.mutationId);
    try {
      await requeueMutation(scopeOf(item), item.mutationId);
      message.success(t('queuedToSendAgain'));
      void requestFlush();
    } finally {
      setBusy(null);
    }
  };

  const closeDismiss = () => {
    setDismissing(null);
    setReason('');
  };

  const handleDismiss = async () => {
    if (!dismissing || reason.trim() === '') return;
    setBusy(dismissing.mutationId);
    try {
      await markDismissed(scopeOf(dismissing), dismissing.mutationId, reason.trim());
      message.info(t('recordDismissedItsDetailsStayOnThisDevice'));
      closeDismiss();
    } finally {
      setBusy(null);
    }
  };

  const columns = [
    {
      title: t('record2'),
      key: 'record',
      render: (_: unknown, item: OutboxItem) => (
        <Space direction="vertical" size={0}>
          <Text>{describeItem(item, targetLabel?.(item) ?? null)}</Text>
          <Text type="secondary" style={{ fontSize: 12 }}>{kindLabel(item.kind)}</Text>
        </Space>
      ),
    },
    {
      title: t('taken'),
      key: 'occurredAt',
      render: (_: unknown, item: OutboxItem) => dayjs(item.occurredAt).format('YYYY-MM-DD HH:mm'),
    },
    {
      title: t('status'),
      key: 'status',
      render: (_: unknown, item: OutboxItem) => (
        <Tag color={OUTBOX_STATUS_COLORS[item.status]}>{outboxStatusLabel(t, item.status)}</Tag>
      ),
    },
    {
      title: t('message'),
      key: 'message',
      render: (_: unknown, item: OutboxItem) => {
        // A dismissed record leads with the reason the user gave: that is the decision that
        // now stands, and the server's original refusal is what they were deciding about.
        if (item.status === 'dismissed') {
          return (
            <Text type="secondary">
              {item.dismissedReason ? `Dismissed: ${item.dismissedReason}` : t('dismissed')}
              {item.serverMessage ? ` (was: ${item.serverMessage})` : ''}
            </Text>
          );
        }

        const text = item.serverMessage ?? item.lastError;
        if (!text) return <Text type="secondary">—</Text>;
        return <Text type={item.status === 'quarantined' ? 'danger' : 'secondary'}>{text}</Text>;
      },
    },
  ];

  if (showActions) {
    columns.push({
      title: t('actions'),
      key: 'actions',
      render: (_: unknown, item: OutboxItem) => {
        if (item.status === 'pending' || item.status === 'applied') {
          return <Text type="secondary">—</Text>;
        }
        return (
          <Space size={4}>
            <Tooltip title={t('sendThisRecordAgain')}>
              <Button
                size="small"
                icon={<ReloadOutlined />}
                loading={busy === item.mutationId}
                onClick={() => void handleRetry(item)}
              >
                {t('retry')}
              </Button>
            </Tooltip>
            <Tooltip title={t('keepTheRecordOnThisDeviceButStop')}>
              <Button
                size="small"
                danger
                icon={<StopOutlined />}
                disabled={busy === item.mutationId}
                onClick={() => {
                  setDismissing(item);
                  setReason('');
                }}
              >
                {t('dismiss')}
              </Button>
            </Tooltip>
          </Space>
        );
      },
    });
  }

  return (
    <>
      <Table
        rowKey="mutationId"
        size="small"
        columns={columns}
        dataSource={items}
        loading={loading}
        pagination={false}
        locale={{ emptyText }}
      />

      <Modal
        open={dismissing !== null}
        title={t('dismissThisRecord')}
        okText={t('dismiss')}
        okButtonProps={{ danger: true, disabled: reason.trim() === '' }}
        onOk={() => void handleDismiss()}
        onCancel={closeDismiss}
      >
        <Text>
          {t('itWillNotBeSentToTheServer')}
        </Text>
        <Input.TextArea
          style={{ marginTop: 12 }}
          rows={3}
          value={reason}
          maxLength={200}
          placeholder={t('eGWeighedTheWrongAnimal')}
          onChange={(event) => setReason(event.target.value)}
        />
      </Modal>
    </>
  );
};

export default OutboxTable;
