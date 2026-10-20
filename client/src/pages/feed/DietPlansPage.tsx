import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Alert, Button, Card, Col, Form, Input, InputNumber, Modal, Popconfirm, Row, Space, Statistic, Switch, Table, Tag,
  message,
} from 'antd';
import { DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import { dietPlansApi, feedTypesApi } from '../../api/feed';
import { lookupsApi } from '../../api/attendance';
import { getApiError } from '../../api/farmApi';
import LookupQuickAddSelect from '../../components/LookupQuickAddSelect';
import { formatMoney, formatNumber } from '../../i18n/format';
import type { DietPlan, DietPlanItem, FeedType } from '../../types';
import { useTranslation } from 'react-i18next';

interface LookupOption {
  id: string;
  name: string;
}

/** A row being edited in the ration builder. `key` is React-only. */
interface RationRow {
  key: string;
  feedTypeId?: string;
  quantity?: number;
}

/**
 * How many kilograms one of a feed unit is worth. Bale, bag, litre and "other" are not
 * convertible without a per-feed weight, so they are deliberately absent instead of
 * being counted as if they were kilograms.
 */
const KG_PER_UNIT: Record<string, number> = {
  Kilogram: 1,
  Gram: 0.001,
  Ton: 1000,
};

const DietPlansPage: React.FC = () => {const { t: translate } = useTranslation(['feed', 'validation']); 
  const [plans, setPlans] = useState<DietPlan[]>([]);
  const [loading, setLoading] = useState(false);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<DietPlan | null>(null);
  const [feedTypes, setFeedTypeOptions] = useState<FeedType[]>([]);
  const [animalTypes, setAnimalTypes] = useState<LookupOption[]>([]);
  const [ageCategories, setAgeCategories] = useState<LookupOption[]>([]);
  const [rationRows, setRationRows] = useState<RationRow[]>([]);
  const rowSequence = useRef(0);
  const [form] = Form.useForm();

  const loadPlans = useCallback(async () => {
    setLoading(true);
    try {
      const res = await dietPlansApi.list();
      setPlans(res.data);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, []);

  /** Each lookup settles independently: one failing list must not blank the others. */
  const loadOptions = useCallback(async () => {
    const [ft, at, ac] = await Promise.all([
      feedTypesApi.list().catch(() => undefined),
      lookupsApi.animalTypes().catch(() => undefined),
      lookupsApi.ageCategories().catch(() => undefined),
    ]);
    if (ft) setFeedTypeOptions(ft.data);
    if (at) setAnimalTypes(at.data);
    if (ac) setAgeCategories(ac.data);
  }, []);

  useEffect(() => {
    window.setTimeout(() => { loadPlans(); void loadOptions(); }, 0);
  }, [loadPlans, loadOptions]);

  const feedById = useMemo(
    () => new Map(feedTypes.map((feed) => [feed.id, feed])),
    [feedTypes],
  );

  const openCreate = () => {
    setEditing(null);
    setRationRows([]);
    form.resetFields();
    form.setFieldsValue({ isActive: true });
    setModalOpen(true);
  };

  const openEdit = (plan: DietPlan) => {
    setEditing(plan);
    // Existing items become rows in the builder, so editing no longer needs a drawer.
    setRationRows(
      plan.items.map((item) => ({
        key: item.id,
        feedTypeId: item.feedTypeId,
        quantity: item.quantityPerFeeding,
      })),
    );
    form.setFieldsValue({
      name: plan.name,
      animalTypeId: plan.animalTypeId,
      breedId: plan.breedId,
      ageCategoryId: plan.ageCategoryId,
      minWeightKg: plan.minWeightKg,
      maxWeightKg: plan.maxWeightKg,
      isActive: plan.isActive,
      notes: plan.notes,
    });
    setModalOpen(true);
  };

  const updateRow = (key: string, patch: Partial<RationRow>) => {
    setRationRows((rows) => rows.map((row) => (row.key === key ? { ...row, ...patch } : row)));
  };

  const addRow = () => {
    rowSequence.current += 1;
    setRationRows((rows) => [...rows, { key: `new-${rowSequence.current}` }]);
  };

  const removeRow = (key: string) => {
    setRationRows((rows) => rows.filter((row) => row.key !== key));
  };

  const completeRows = rationRows.filter((row) => row.feedTypeId && row.quantity);
  const totalQuantity = completeRows.reduce((sum, row) => sum + (row.quantity ?? 0), 0);
  const totalCost = completeRows.reduce((sum, row) => {
    const feed = row.feedTypeId ? feedById.get(row.feedTypeId) : undefined;
    return sum + (row.quantity ?? 0) * (feed?.costPerUnit ?? 0);
  }, 0);

  /**
   * Shares are derived from the quantities, never stored: a percentage that can drift
   * from the quantity it describes is worse than no percentage at all. With mixed feed
   * units the share is of the total row count, which the note under the table says.
   */
  const shareOf = (row: RationRow): number =>
    totalQuantity > 0 && row.quantity ? (row.quantity / totalQuantity) * 100 : 0;

  // Rounded to two decimals so binary floating point cannot display a 99.9% total
  // for rows that are mathematically 100%.
  const totalShare = Math.round(completeRows.reduce((sum, row) => sum + shareOf(row), 0) * 100) / 100;

  const unitTotals = (() => {
    const totals = new Map<string, number>();
    for (const row of completeRows) {
      const feed = row.feedTypeId ? feedById.get(row.feedTypeId) : undefined;
      const unit = feed?.unitName ?? '';
      totals.set(unit, (totals.get(unit) ?? 0) + (row.quantity ?? 0));
    }
    return totals;
  })();

  const kgTotal = completeRows.reduce((sum, row) => {
    const feed = row.feedTypeId ? feedById.get(row.feedTypeId) : undefined;
    const factor = feed ? KG_PER_UNIT[feed.unit] : undefined;
    return factor === undefined ? sum : sum + (row.quantity ?? 0) * factor;
  }, 0);

  const unitsUsed = new Set(
    completeRows
      .map((row) => (row.feedTypeId ? feedById.get(row.feedTypeId)?.unitName : undefined))
      .filter((unit): unit is string => Boolean(unit)),
  );
  const hasNonMassUnit = completeRows.some((row) => {
    const feed = row.feedTypeId ? feedById.get(row.feedTypeId) : undefined;
    return feed ? KG_PER_UNIT[feed.unit] === undefined : false;
  });
  const showUnitNote = unitsUsed.size > 1 || hasNonMassUnit;

  /** Blocks saving an incomplete or ambiguous ration before it reaches the server. */
  const rationProblem = (() => {
    if (rationRows.length === 0) return undefined;
    if (rationRows.some((row) => !row.feedTypeId || !row.quantity || row.quantity <= 0)) {
      return translate('incompleteRation');
    }
    const ids = rationRows.map((row) => row.feedTypeId);
    if (new Set(ids).size !== ids.length) return translate('duplicateFeed');
    if (totalQuantity > 0 && Math.abs(totalShare - 100) > 0.01) {
      return translate('percentagesMustSumTo100');
    }
    return undefined;
  })();

  /**
   * Updates have no item-level replace, so the builder reconciles the rows with the
   * stored items: a removed or changed row is deleted and re-added. The payload is the
   * same `{ feedTypeId, quantityPerFeeding }` the drawer used, so storage is unchanged.
   */
  const syncItems = async (
    plan: DietPlan,
    items: { feedTypeId: string; quantityPerFeeding: number }[],
  ) => {
    const desiredByFeed = new Map(items.map((item) => [item.feedTypeId, item]));
    for (const existing of plan.items) {
      const desired = desiredByFeed.get(existing.feedTypeId);
      if (!desired || desired.quantityPerFeeding !== existing.quantityPerFeeding) {
        await dietPlansApi.removeItem(plan.id, existing.id);
      }
    }
    const existingByFeed = new Map(plan.items.map((item) => [item.feedTypeId, item]));
    for (const item of items) {
      const existing = existingByFeed.get(item.feedTypeId);
      if (!existing || existing.quantityPerFeeding !== item.quantityPerFeeding) {
        await dietPlansApi.addItem(plan.id, item);
      }
    }
  };

  const handleSave = async () => {
    try {
      const values = await form.validateFields();
      if (rationProblem) return;
      const items = rationRows.map((row) => ({
        feedTypeId: row.feedTypeId as string,
        quantityPerFeeding: row.quantity as number,
      }));
      if (editing) {
        await dietPlansApi.update(editing.id, values);
        await syncItems(editing, items);
        message.success(translate('dietPlanUpdated'));
      } else {
        await dietPlansApi.create({ ...values, items });
        message.success(translate('dietPlanCreated'));
      }
      setModalOpen(false);
      loadPlans();
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await dietPlansApi.remove(id);
      message.success(translate('dietPlanDeleted'));
      loadPlans();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ColumnsType<DietPlan> = [
    { title: translate('name'), dataIndex: 'name' },
    {
      title: translate('targets'),
      render: (_, record) => (
        <Space size={4} wrap>
          {record.animalTypeName && <Tag>{record.animalTypeName}</Tag>}
          {record.breedName && <Tag>{record.breedName}</Tag>}
          {record.ageCategoryName && <Tag>{record.ageCategoryName}</Tag>}
          {(record.minWeightKg || record.maxWeightKg) && (
            <Tag>
              {record.minWeightKg ?? 0}–{record.maxWeightKg ?? '∞'} {translate('kg')}
            </Tag>
          )}
          {!record.animalTypeName && !record.breedName && !record.ageCategoryName &&
            !record.minWeightKg && !record.maxWeightKg && <span>{translate('allAnimals')}</span>}
        </Space>
      ),
    },
    { title: translate('items'), dataIndex: 'items', render: (items: DietPlanItem[]) => items.length },
    { title: translate('schedules'), dataIndex: 'scheduleCount', align: 'center' },
    {
      title: translate('active'),
      dataIndex: 'isActive',
      render: (active: boolean) =>
        active ? <Tag color="green">{translate('active')}</Tag> : <Tag color="default">{translate('inactive')}</Tag>,
    },
    {
      title: translate('actions'),
      render: (_, record) => (
        <Space>
          <Button size="small" onClick={() => openEdit(record)}>{translate('edit')}</Button>
          <Popconfirm title={translate('deleteThisDietPlan')} onConfirm={() => handleDelete(record.id)}>
            <Button size="small" danger>{translate('delete')}</Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  const rationColumns: ColumnsType<RationRow> = [
    {
      title: translate('feedType'),
      width: 280,
      render: (_, row) => (
        <LookupQuickAddSelect
          kind="feedType"
          value={row.feedTypeId}
          onChange={(feedTypeId) => updateRow(row.key, { feedTypeId })}
          placeholder={translate('feedType2')}
          options={feedTypes.map((feed) => ({ value: feed.id, label: `${feed.name} (${feed.unitName})` }))}
          onCreated={() => loadOptions()}
        />
      ),
    },
    {
      title: translate('quantityPerFeeding'),
      width: 170,
      render: (_, row) => {
        const feed = row.feedTypeId ? feedById.get(row.feedTypeId) : undefined;
        return (
          <InputNumber
            min={0.01}
            precision={3}
            value={row.quantity}
            onChange={(quantity) => updateRow(row.key, { quantity: quantity ?? undefined })}
            addonAfter={feed?.unitName}
            style={{ width: '100%' }}
          />
        );
      },
    },
    {
      title: translate('shareOfRation'),
      width: 120,
      align: 'right',
      render: (_, row) =>
        `${formatNumber(shareOf(row), undefined, { maximumFractionDigits: 1 })}%`,
    },
    {
      title: translate('cost'),
      width: 110,
      align: 'right',
      render: (_, row) => {
        const feed = row.feedTypeId ? feedById.get(row.feedTypeId) : undefined;
        return formatMoney((row.quantity ?? 0) * (feed?.costPerUnit ?? 0));
      },
    },
    {
      title: '',
      width: 48,
      render: (_, row) => (
        <Button
          size="small"
          danger
          type="text"
          icon={<DeleteOutlined />}
          aria-label={translate('removeFeed')}
          onClick={() => removeRow(row.key)}
        />
      ),
    },
  ];

  return (
    <Card
      title={translate('dietPlans')}
      extra={
        <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
          {translate('newDietPlan')}
        </Button>
      }
    >
      <Table rowKey="id" columns={columns} dataSource={plans} loading={loading} pagination={false} />

      <Modal
        title={editing ? translate('editDietPlan') : translate('newDietPlan')}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => setModalOpen(false)}
        okButtonProps={{ disabled: Boolean(rationProblem) }}
        destroyOnClose
        width={780}
      >
        <Form form={form} layout="vertical">
          <Form.Item name="name" label={translate('name')} rules={[{ required: true, message: translate('validation:common.nameRequired') }]}>
            <Input maxLength={100} />
          </Form.Item>
          <Form.Item name="animalTypeId" label={translate('animalType')}>
            <LookupQuickAddSelect
              kind="animalType"
              allowClear
              placeholder={translate('any')}
              options={animalTypes.map((t) => ({ value: t.id, label: t.name }))}
              onCreated={() => loadOptions()}
            />
          </Form.Item>
          <Form.Item name="ageCategoryId" label={translate('ageCategory')}>
            <LookupQuickAddSelect
              kind="ageCategory"
              allowClear
              placeholder={translate('any')}
              options={ageCategories.map((c) => ({ value: c.id, label: c.name }))}
              onCreated={() => loadOptions()}
            />
          </Form.Item>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="minWeightKg" label={translate('minWeightKg')}>
                <InputNumber min={0} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="maxWeightKg" label={translate('maxWeightKg')}>
                <InputNumber min={0} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          {editing && (
            <Form.Item name="isActive" label={translate('active')} valuePropName="checked">
              <Switch />
            </Form.Item>
          )}
          <Form.Item name="notes" label={translate('notes')}>
            <Input.TextArea rows={2} maxLength={500} />
          </Form.Item>

          <Form.Item label={translate('ration')} style={{ marginBottom: 8 }}>
            <Table
              rowKey="key"
              columns={rationColumns}
              dataSource={rationRows}
              pagination={false}
              size="small"
              locale={{ emptyText: translate('noFeedsInRation') }}
            />
            <Button
              type="dashed"
              icon={<PlusOutlined />}
              onClick={addRow}
              block
              style={{ marginTop: 8 }}
            >
              {translate('addFeed')}
            </Button>
          </Form.Item>

          <Row gutter={16} style={{ marginBottom: 8 }}>
            <Col span={8}>
              <Statistic
                title={translate('totalPercent')}
                value={totalShare}
                precision={1}
                suffix="%"
              />
            </Col>
            <Col span={8}>
              <Statistic
                title={translate('totalCost')}
                value={totalCost}
                precision={2}
                prefix="$"
              />
            </Col>
            <Col span={8}>
              <Statistic
                title={translate('totalKg')}
                value={kgTotal}
                precision={2}
                suffix={translate('kg')}
              />
            </Col>
          </Row>

          {(unitTotals.size > 1 || hasNonMassUnit) && (
            <Space size={[4, 4]} wrap style={{ marginBottom: 8 }}>
              {[...unitTotals.entries()].map(([unit, quantity]) => (
                <Tag key={unit || 'unknown'}>
                  {formatNumber(quantity, undefined, { maximumFractionDigits: 3 })} {unit}
                </Tag>
              ))}
            </Space>
          )}

          {showUnitNote && (
            <Alert type="info" showIcon message={translate('mixedUnitsNote')} style={{ marginBottom: 8 }} />
          )}
          {rationProblem && (
            <Alert type="warning" showIcon message={rationProblem} />
          )}
        </Form>
      </Modal>
    </Card>
  );
};

export default DietPlansPage;
