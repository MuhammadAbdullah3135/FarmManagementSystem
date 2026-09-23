import React, { useState } from 'react';
import { Alert, Button, Card, Form, Input, InputNumber, Select, Space, Typography, message } from 'antd';
import { useSearchParams } from 'react-router-dom';
import { lookupsApi, type AnimalLookupRow } from '../../api/attendance';
import { useCachedQuery } from '../../offline/cachedQuery';
import SyncAgeLabel from '../../components/SyncAgeLabel';
import OutboxTable from '../../components/OutboxTable';
import { WEIGHT_RECORD, type WeightRecordPayload } from '../../offline/mutationKinds';
import { enqueueMutation, type OutboxItem } from '../../offline/outbox';
import { requestFlush } from '../../offline/syncEngine';
import { useSyncStore } from '../../offline/syncStatus';
import { useOfflineStore } from '../../offline/connectivity';
import { useOutboxItems } from '../../offline/useOutbox';
import { useAuthStore } from '../../stores/authStore';
import { useFarmStore } from '../../stores/farmStore';

const { Text, Title } = Typography;

/**
 * Recording a weight, with or without a connection.
 *
 * One write path on purpose: a recorded weight is always queued, and a flush is requested
 * immediately. Online that arrives within a second and the server's answer (or its refusal,
 * verbatim) replaces the optimistic row; offline it waits on the device. A separate
 * direct-to-API path for the online case would be two behaviours to keep identical, and the
 * sync endpoint already applies the item by calling the same service method the live endpoint
 * does — the 4.5.3 message-parity tests are what make that safe.
 *
 * The animal can only be one the device holds: the picker reads the cached farm lookup, so
 * there is no way to type a tag number that might resolve to a different animal at sync time.
 */
const RecordWeightPage: React.FC = () => {
  const [searchParams] = useSearchParams();
  const [form] = Form.useForm();
  const [submitting, setSubmitting] = useState(false);

  const accountId = useAuthStore((state) => state.user?.accountId ?? null);
  const farmId = useFarmStore((state) => state.activeFarm?.id ?? null);
  const scope = accountId && farmId ? { accountId, farmId } : null;

  const isOnline = useOfflineStore((store) => store.isOnline);
  const isFlushing = useSyncStore((store) => store.isFlushing);

  const animalsQuery = useCachedQuery<AnimalLookupRow>({
    collection: 'animals',
    variant: 'lookup',
    fetcher: async () => {
      const response = await lookupsApi.animals();
      return { rows: response.data.items, total: response.data.totalCount };
    },
    // A failed lookup must not interrupt the page: the cached list is what the picker needs,
    // and an empty one is reported below in words the user can act on.
    onError: () => undefined,
  });

  // Weights only: the queue also carries attendance and task workflows now, and this card is
  // about what was recorded on this screen.
  const items = useOutboxItems(scope, { kinds: [WEIGHT_RECORD] });
  const quarantined = items.filter((item) => item.status === 'quarantined');

  const labelFor = (item: OutboxItem) => {
    const row = animalsQuery.rows.find((animal) => animal.id === item.targetId);
    if (!row) return null;
    return row.name ? `${row.tagNumber} — ${row.name}` : row.tagNumber;
  };

  const preselected = searchParams.get('animalId') ?? undefined;
  const hasCachedAnimals = animalsQuery.rows.length > 0;

  const handleSubmit = async (values: { animalId: string; weightKg: number; notes?: string }) => {
    if (!scope) {
      message.error('Select a farm first.');
      return;
    }

    // The device's clock, captured now: this is the measurement's real time, not the time it
    // eventually reaches the server. The 5.3 endpoint validates it against the same
    // "not too far in the future" rule the live weight endpoint applies.
    const recordedAt = new Date().toISOString();
    const payload: WeightRecordPayload = {
      animalId: values.animalId,
      weightKg: values.weightKg,
      recordedAt,
      notes: values.notes?.trim() ? values.notes.trim() : null,
    };

    setSubmitting(true);
    try {
      const queued = await enqueueMutation<WeightRecordPayload>({
        scope,
        kind: WEIGHT_RECORD,
        targetId: payload.animalId,
        occurredAt: payload.recordedAt,
        payload,
      });

      if (!queued.item) {
        // A weight that is not queued is a weight the server will never see, so this must fail
        // loudly rather than look like a save — and with the *reason*, which since 5.6 may be
        // the device being full or the session being too old to deliver it, not just storage.
        message.error(queued.message ?? 'The weight was not saved.');
        return;
      }

      if (queued.warning) message.warning(queued.warning);

      message.success(
        isOnline
          ? `Saved ${payload.weightKg} kg. Sending now.`
          : `Saved ${payload.weightKg} kg on this device. It will sync when you are online.`,
      );

      // Keep the animal selected for the next animal in the same pen; clear the measurement.
      form.resetFields(['weightKg', 'notes']);
      void requestFlush();
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Space direction="vertical" size={16} style={{ width: '100%' }}>
      <div>
        <Title level={4} style={{ marginBottom: 0 }}>Record weight</Title>
        <Text type="secondary">
          Saved on this device first, then sent to the server. The time recorded is this device&apos;s
          clock at the moment you save.
        </Text>
      </div>

      {!isOnline && (
        <Alert
          type="warning"
          showIcon
          message="You are offline"
          description="Recording still works: the weights are stored on this device and sent when the connection is back."
        />
      )}

      {quarantined.length > 0 && (
        <Alert
          type="error"
          showIcon
          message={`${quarantined.length} record${quarantined.length === 1 ? '' : 's'} could not be saved`}
          description="The server refused them. Each message below says why — fix and retry, or dismiss."
        />
      )}

      <Card>
        <Form
          form={form}
          layout="vertical"
          initialValues={{ animalId: preselected }}
          onFinish={(values) => void handleSubmit(values)}
        >
          <Form.Item
            name="animalId"
            label="Animal"
            rules={[{ required: true, message: 'Choose an animal' }]}
            extra={
              !isOnline && !hasCachedAnimals
                ? 'No animals are stored on this device yet. Open the animal list once while online.'
                : undefined
            }
          >
            <Select
              showSearch
              placeholder={hasCachedAnimals ? 'Search by tag number or name' : 'No animals available'}
              optionFilterProp="label"
              disabled={!hasCachedAnimals && !isOnline}
              options={animalsQuery.rows.map((animal) => ({
                value: animal.id,
                label: animal.name ? `${animal.tagNumber} — ${animal.name}` : animal.tagNumber,
              }))}
            />
          </Form.Item>

          {animalsQuery.lastSyncedAt && (
            <div style={{ marginTop: -12, marginBottom: 12 }}>
              <SyncAgeLabel lastSyncedAt={animalsQuery.lastSyncedAt} />
            </div>
          )}

          <Form.Item
            name="weightKg"
            label="Weight"
            rules={[
              { required: true, message: 'Enter a weight' },
              {
                type: 'number',
                min: 0.01,
                message: 'Weight must be greater than zero',
              },
            ]}
          >
            <InputNumber style={{ width: 200 }} min={0} step={0.1} addonAfter="kg" />
          </Form.Item>

          <Form.Item name="notes" label="Notes">
            <Input.TextArea rows={2} maxLength={500} placeholder="Optional" />
          </Form.Item>

          <Space>
            <Button type="primary" htmlType="submit" loading={submitting}>
              Save weight
            </Button>
            <Button
              disabled={items.length === 0}
              loading={isFlushing}
              onClick={() => void requestFlush({ force: true })}
            >
              Sync now
            </Button>
          </Space>
        </Form>
      </Card>

      <Card
        title="Recorded on this device"
        extra={<Text type="secondary">Kept for a day after they sync.</Text>}
      >
        <OutboxTable
          items={items}
          targetLabel={labelFor}
          emptyText="Weights you record show up here until the server has them."
        />
      </Card>

      <Text type="secondary">
        Queued records are sent oldest first, and a record that reaches the server keeps the
        time it was taken on this device — not the time it was uploaded.
      </Text>
    </Space>
  );
};

export default RecordWeightPage;
