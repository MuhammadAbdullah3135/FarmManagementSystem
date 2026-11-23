import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, DatePicker, Form, Input, InputNumber, Modal, Popconfirm, Radio, Space, Table, Tag, message,
} from 'antd';
import Select from '../../components/Select';
import { PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs, { Dayjs } from 'dayjs';
import { feedRecordsApi, feedTypesApi } from '../../api/feed';
import { lookupsApi } from '../../api/attendance';
import { flattenLocations } from '../../api/configuration';
import { getApiError } from '../../api/farmApi';
import LookupQuickAddSelect from '../../components/LookupQuickAddSelect';
import type { FeedRecord, FeedType, MealRole, MealSlot } from '../../types';
import { useTranslation } from 'react-i18next';
import { useEnumOptions } from '../../i18n/enumOptions';
import { feedUnitLabel } from '../../i18n/vocabulary';

interface AnimalOption {
  id: string;
  tagNumber: string;
  name?: string;
}

interface LocationOption {
  id: string;
  name: string;
}

/**
 * Which feeding a clock time belongs to. Mirrors `FeedService.DefaultMealSlot`, so a record
 * created from this form and one created by an API caller without a slot agree.
 */
const slotForHour = (hour: number): MealSlot =>
  hour < 11 ? 'Morning' : hour < 15 ? 'Midday' : hour < 20 ? 'Evening' : 'Night';

/**
 * The ration's main course vs an accompaniment, from the feed type's own category.
 * Mirrors `FeedService.DefaultMealRole`.
 */
const roleForCategory = (category?: string): MealRole =>
  category === 'Forage' || category === 'Concentrate'
    ? 'Main'
    : category === 'Mineral' || category === 'Supplement' || category === 'Additive'
      ? 'Supplement'
      : 'Other';

const FeedRecordsPage: React.FC = () => {const { t: translate } = useTranslation('feed'); 
  const targetModeOptions = useEnumOptions('feedTargetMode');
  // One label map per chip row, so the chips and the table column can never disagree.
  const mealSlotLabels: Record<MealSlot, string> = {
    Morning: translate('morning'),
    Midday: translate('midday'),
    Evening: translate('evening'),
    Night: translate('night'),
  };
  const mealRoleLabels: Record<MealRole, string> = {
    Main: translate('roleMain'),
    Supplement: translate('roleSupplement'),
    Other: translate('roleOther'),
  };
  const mealSlotOptions = (Object.keys(mealSlotLabels) as MealSlot[]).map((value) => ({
    value,
    label: mealSlotLabels[value],
  }));
  const mealRoleOptions = (Object.keys(mealRoleLabels) as MealRole[]).map((value) => ({
    value,
    label: mealRoleLabels[value],
  }));
  const [records, setRecords] = useState<FeedRecord[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [feedTypes, setFeedTypes] = useState<FeedType[]>([]);
  const [animals, setAnimals] = useState<AnimalOption[]>([]);
  const [locations, setLocations] = useState<LocationOption[]>([]);
  const [locationTypes, setLocationTypes] = useState<LocationOption[]>([]);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<FeedRecord | null>(null);
  const [targetMode, setTargetMode] = useState<'animal' | 'location'>('animal');
  const [filters, setFilters] = useState<{
    feedTypeId?: string;
    mealSlot?: MealSlot;
    range?: [Dayjs | null, Dayjs | null];
  }>({});
  const [form] = Form.useForm();
  // Derived, not stored: the addon follows whatever feed type is selected.
  const watchedFeedTypeId = Form.useWatch('feedTypeId', form);
  // In edit mode there is no feed-type field; the record already carries its unit.
  const quantityUnit = editing?.unitName ?? feedTypes.find((ft) => ft.id === watchedFeedTypeId)?.unitName;
  // A chip the user picked outranks the derivation from the clock or the category.
  const [slotTouched, setSlotTouched] = useState(false);
  const [roleTouched, setRoleTouched] = useState(false);

  const loadLookups = useCallback(async () => {
    try {
      const [typesRes, animalsRes, locationsRes, locationTypesRes] = await Promise.all([
        feedTypesApi.list(),
        lookupsApi.animals(),
        lookupsApi.locations(),
        // Needed by the inline Add Location quick-add.
        lookupsApi.locationTypes(),
      ]);
      setFeedTypes(typesRes.data);
      setAnimals(animalsRes.data.items);
      setLocationTypes(locationTypesRes.data);
      // Flatten the location tree so nested (child) locations appear too.
      setLocations(
        flattenLocations(locationsRes.data).map((l) => ({
          id: l.id,
          name: `${'\u00A0\u00A0'.repeat(l.depth)}${l.name}`,
        })),
      );
    } catch (err) {
      message.error(getApiError(err));
    }
  }, []);

  const loadRecords = useCallback(async (p: number) => {
    setLoading(true);
    try {
      const res = await feedRecordsApi.list({
        feedTypeId: filters.feedTypeId,
        mealSlot: filters.mealSlot,
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
    const timer = window.setTimeout(() => { loadLookups(); }, 0);
    return () => window.clearTimeout(timer);
  }, [loadLookups]);

  useEffect(() => {
    const timer = window.setTimeout(() => { loadRecords(1); }, 0);
    return () => window.clearTimeout(timer);
  }, [loadRecords]);

  const openCreate = () => {
    setEditing(null);
    setTargetMode('animal');
    form.resetFields();
    setSlotTouched(false);
    setRoleTouched(false);
    const now = dayjs();
    // Pre-filled from the clock so recording the feeding taking place now is two clicks.
    form.setFieldsValue({ fedAt: now, mealSlot: slotForHour(now.hour()) });
    setModalOpen(true);
  };

  const openEdit = (record: FeedRecord) => {
    setEditing(record);
    // An existing record's own choices are deliberate: changing the time must not overwrite them.
    setSlotTouched(Boolean(record.mealSlot));
    setRoleTouched(Boolean(record.mealRole));
    setTargetMode(record.animalId ? 'animal' : 'location');
    form.resetFields();
    form.setFieldsValue({
      quantity: record.quantity,
      fedAt: dayjs(record.fedAt),
      notes: record.notes,
      mealSlot: record.mealSlot,
      mealRole: record.mealRole,
    });
    setModalOpen(true);
  };

  const handleSave = async () => {
    try {
      const values = await form.validateFields();
      if (editing) {
        await feedRecordsApi.update(editing.id, {
          mealSlot: values.mealSlot,
          mealRole: values.mealRole,
          quantity: values.quantity,
          fedAt: values.fedAt?.toISOString(),
          notes: values.notes,
        });
        message.success(translate('feedRecordUpdated'));
      } else {
        await feedRecordsApi.create({
          feedTypeId: values.feedTypeId,
          mealSlot: values.mealSlot,
          mealRole: values.mealRole,
          animalId: targetMode === 'animal' ? values.animalId : undefined,
          locationId: targetMode === 'location' ? values.locationId : undefined,
          quantity: values.quantity,
          fedAt: values.fedAt?.toISOString(),
          notes: values.notes,
        });
        message.success(translate('feedingRecorded'));
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
      message.success(translate('feedRecordDeleted'));
      loadRecords(page);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ColumnsType<FeedRecord> = [
    { title: translate('fedAt'), dataIndex: 'fedAt', render: (d: string) => dayjs(d).format('YYYY-MM-DD HH:mm') },
    { title: translate('feedType'), dataIndex: 'feedTypeName' },
    {
      title: translate('meal'),
      render: (_, record) =>
        record.mealSlot || record.mealRole ? (
          <Space size={4} wrap>
            {record.mealSlot && <Tag>{mealSlotLabels[record.mealSlot]}</Tag>}
            {record.mealRole && <Tag color="default">{mealRoleLabels[record.mealRole]}</Tag>}
          </Space>
        ) : (
          '-'
        ),
    },
    {
      title: translate('target'),
      render: (_, record) =>
        record.animalId
          ? `${record.animalTagNumber}${record.animalName ? ` (${record.animalName})` : ''}`
          : record.locationName ?? '-',
    },
    { title: translate('quantity'), dataIndex: 'quantity', align: 'right', render: (v: number, r) => `${v} ${r.unitName}` },
    { title: translate('cost'), dataIndex: 'totalCost', align: 'right' },
    { title: translate('notes'), dataIndex: 'notes', ellipsis: true },
    {
      title: translate('actions'),
      render: (_, record) => (
        <Space>
          <Button size="small" onClick={() => openEdit(record)}>{translate('edit')}</Button>
          <Popconfirm title={translate('restoreStockAndDeleteThisRecord')} onConfirm={() => handleDelete(record.id)}>
            <Button size="small" danger>{translate('delete')}</Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <Card
      title={translate('dailyFeedRecords')}
      extra={
        <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
          {translate('recordFeeding')}
        </Button>
      }
    >
      <Space wrap style={{ marginBottom: 16 }}>
        <Select
          allowClear
          placeholder={translate('filterByFeedType')}
          style={{ width: 200 }}
          value={filters.feedTypeId}
          onChange={(v) => setFilters((f) => ({ ...f, feedTypeId: v }))}
          options={feedTypes.map((t) => ({ value: t.id, label: t.name }))}
        />
        <Select
          allowClear
          placeholder={translate('filterByMeal')}
          style={{ width: 160 }}
          value={filters.mealSlot}
          onChange={(v) => setFilters((f) => ({ ...f, mealSlot: v }))}
          options={mealSlotOptions}
        />
        <DatePicker.RangePicker
          value={filters.range}
          onChange={(range) => setFilters((f) => ({ ...f, range: range ?? undefined }))}
        />
      </Space>

      <Table
        rowKey="id"
        columns={columns}
        dataSource={records}
        loading={loading}
        pagination={{ current: page, total, pageSize: 10, onChange: loadRecords }}
      />

      {/* The form is taller than a laptop viewport once the chips are in, so the body
          scrolls on its own and the footer's Save button stays on screen. */}
      <Modal
        title={editing ? translate('editFeedRecord') : translate('recordFeeding')}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => setModalOpen(false)}
        destroyOnClose
        styles={{ body: { maxHeight: 'calc(100vh - 220px)', overflowY: 'auto' } }}
      >
        <Form form={form} layout="vertical">
          {!editing && (
            <>
              <Form.Item label={translate('feedTarget')}>
                <Radio.Group
                  value={targetMode}
                  onChange={(e) => setTargetMode(e.target.value)}
options={targetModeOptions}
                  optionType="button"
                  buttonStyle="solid"
                />
              </Form.Item>
              <Form.Item name="feedTypeId" label={translate('feedType')} rules={[{ required: true }]}>
                <LookupQuickAddSelect
                  kind="feedType"
                  options={feedTypes.map((t) => ({ value: t.id, label: `${t.name} (${t.unitName})` }))}
                  onValueSelected={(id) => {
                    // The category decides what kind of course this is; a chip the user
                    // already picked is never overwritten.
                    if (roleTouched) return;
                    const feed = feedTypes.find((t) => t.id === id);
                    form.setFieldsValue({ mealRole: roleForCategory(feed?.category) });
                  }}
                  onCreated={() => loadLookups()}
                />
              </Form.Item>
              <Form.Item name="mealSlot" label={translate('meal')}>
                <Radio.Group
                  options={mealSlotOptions}
                  optionType="button"
                  buttonStyle="solid"
                  onChange={() => setSlotTouched(true)}
                />
              </Form.Item>
              <Form.Item name="mealRole" label={translate('feedRole')}>
                <Radio.Group
                  options={mealRoleOptions}
                  optionType="button"
                  buttonStyle="solid"
                  onChange={() => setRoleTouched(true)}
                />
              </Form.Item>
              {targetMode === 'animal' ? (
                <Form.Item name="animalId" label={translate('animal')} rules={[{ required: true }]}>
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
                <Form.Item name="locationId" label={translate('location')} rules={[{ required: true }]}>
                  <LookupQuickAddSelect
                    kind="location"
                    ctx={{
                      locationTypes: locationTypes.map((lt) => ({ value: lt.id, label: lt.name })),
                      locations: locations.map((l) => ({ value: l.id, label: l.name })),
                    }}
                    options={locations.map((l) => ({ value: l.id, label: l.name }))}
                    onCreated={() => loadLookups()}
                  />
                </Form.Item>
              )}
            </>
          )}
          <Form.Item name="quantity" label={translate('quantity')} rules={[{ required: true }]}>
            <InputNumber
              min={0.01}
              style={{ width: '100%' }}
              // The feed type owns the unit, so the box says what the number is in.
              // `suffix`, not `addonAfter`: the addon wraps the control in a Space.Compact
              // that does not take the field's width, leaving the quantity box at a
              // third of the width of every other field.
              suffix={quantityUnit ? feedUnitLabel(translate, quantityUnit) : undefined}
            />
          </Form.Item>
          <Form.Item name="fedAt" label={translate('fedAt')} rules={[{ required: true }]}>
            <DatePicker
              showTime
              style={{ width: '100%' }}
              onChange={(value) => {
                // The slot follows the clock until the user picks a chip themselves.
                if (slotTouched || !value) return;
                form.setFieldsValue({ mealSlot: slotForHour(value.hour()) });
              }}
            />
          </Form.Item>
          <Form.Item name="notes" label={translate('notes')}>
            <Input.TextArea rows={2} maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>
    </Card>
  );
};

export default FeedRecordsPage;
