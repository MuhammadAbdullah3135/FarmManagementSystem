import { useState, useEffect, useCallback, useMemo } from 'react';
import { Card, Table, Button, Modal, Form, Input, Select, DatePicker, Space, Tag, Typography, message, Popconfirm, Row, Col, Tooltip } from 'antd';
import { flattenLocations, type AnimalType } from '../../api/configuration';
import type { ColumnsType } from 'antd/es/table';
import {
  PlusOutlined, EditOutlined, DeleteOutlined, EyeOutlined, UploadOutlined, PrinterOutlined,
  CheckCircleOutlined, SwapOutlined,
} from '@ant-design/icons';
import { AnimalScanButton } from '../../components/AnimalScanModal';
import { animalsApi, type AnimalListFilter, type BulkOperationResult, type CreateAnimalPayload } from '../../api/animals';
import { lookupsApi } from '../../api/attendance';
import { getApiError } from '../../api/farmApi';
import LookupQuickAddSelect, { type CreatedLookup } from '../../components/LookupQuickAddSelect';
import type { LookupKind } from '../../components/lookupQuickAdd';
import { formatDate } from '../../i18n/format';
import { parentCandidates } from '../../utils/animalSex';
import { resolvePage, useListParams } from '../../utils/listParams';
import dayjs from 'dayjs';
import { useNavigate } from 'react-router-dom';
import { useAuthStore } from '../../stores/authStore';
import type { AnimalListItem } from '../../types';
import { useTranslation } from 'react-i18next';

interface LookupOption {
  id: string;
  name: string;
  value?: string;
}

const STATUS_COLORS: Record<number, string> = { 0: 'green', 1: 'orange', 2: 'red' };

const { Text } = Typography;

/** The filters this page owns; each one maps 1:1 onto an `AnimalListFilter` field. */
interface ListFilters {
  search?: string;
  animalTypeId?: string;
  statusId?: string;
  locationId?: string;
  sexOptionId?: string;
}

type BulkAction = 'status' | 'transfer';

const PAGE_SIZE_OPTIONS = ['10', '20', '50', '100'];
const DEFAULT_PAGE_SIZE = 10;

/** The page size the URL asks for; only a size the control offers is honoured. */
const resolvePageSize = (raw: string | null) =>
  raw !== null && PAGE_SIZE_OPTIONS.includes(raw) ? Number(raw) : DEFAULT_PAGE_SIZE;

/** Indents nested location labels so the tree structure is visible in the select. */
const indentLocationLabel = (name: string, depth: number) => `${'\u00A0\u00A0'.repeat(depth)}${name}`;

export default function AnimalsPage() {const { t: translate } = useTranslation('animals'); 
  const navigate = useNavigate();
  // Sign-out nulls the user, so the flag itself is the whole check.
  const isDemo = useAuthStore((state) => Boolean(state.user?.isDemo));
  const demoBulkHint = isDemo ? translate('bulkDisabledForDemo') : undefined;
  const { searchParams, updateParams } = useListParams();
  const [data, setData] = useState<AnimalListItem[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  // The URL is this list's view state, so a reload or a shared link shows the same rows.
  const filters = useMemo<ListFilters>(() => ({
    search: searchParams.get('search') || undefined,
    animalTypeId: searchParams.get('animalTypeId') || undefined,
    statusId: searchParams.get('statusId') || undefined,
    locationId: searchParams.get('locationId') || undefined,
    sexOptionId: searchParams.get('sexOptionId') || undefined,
  }), [searchParams]);
  const page = resolvePage(searchParams.get('page'));
  const pageSize = resolvePageSize(searchParams.get('pageSize'));
  // What has been typed but not yet applied; null means the box shows the applied term from the
  // URL, which is how a reload or a Back lands with the search it is actually filtering by.
  const [searchDraft, setSearchDraft] = useState<string | null>(null);
  const searchValue = searchDraft ?? filters.search ?? '';
  const [selectedRowKeys, setSelectedRowKeys] = useState<string[]>([]);
  const [bulkAction, setBulkAction] = useState<BulkAction | null>(null);
  const [bulkSaving, setBulkSaving] = useState(false);
  const [bulkStatusId, setBulkStatusId] = useState<string | undefined>();
  const [bulkLocationId, setBulkLocationId] = useState<string | undefined>();
  const [bulkReason, setBulkReason] = useState('');
  // Bumped after a write so the current page refetches without page or filter state having to change.
  const [refreshToken, setRefreshToken] = useState(0);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<AnimalListItem | null>(null);
  const [animalTypes, setAnimalTypes] = useState<AnimalType[]>([]);
  const [breeds, setBreeds] = useState<LookupOption[]>([]);
  const [sexOptions, setSexOptions] = useState<LookupOption[]>([]);
  const [statuses, setStatuses] = useState<LookupOption[]>([]);
  const [locations, setLocations] = useState<LookupOption[]>([]);
  const [ageCategories, setAgeCategories] = useState<LookupOption[]>([]);
  const [locationTypes, setLocationTypes] = useState<LookupOption[]>([]);
  const [allAnimals, setAllAnimals] = useState<
    { id: string; tagNumber: string; name?: string; sexValue?: string }[]
  >([]);
  const [form] = Form.useForm();

  const selectedAnimalTypeId = Form.useWatch('animalTypeId', form);

  const loadLookups = useCallback(async () => {
    // allSettled so one failing lookup cannot blank every dropdown silently.
    const [atRes, soRes, stRes, locRes, acRes, ltRes, anRes] = await Promise.allSettled([
      lookupsApi.animalTypes(),
      lookupsApi.sexOptions(),
      lookupsApi.statuses(),
      lookupsApi.locations(),
      lookupsApi.ageCategories(),
      lookupsApi.locationTypes(),
      lookupsApi.animals(),
    ]);
    const failed: string[] = [];
    if (atRes.status === 'fulfilled') setAnimalTypes(atRes.value.data as unknown as AnimalType[]);
    else failed.push('animal types');
    if (soRes.status === 'fulfilled') setSexOptions(soRes.value.data.map((s: { id: string; value: string }) => ({ id: s.id, name: s.value })));
    else failed.push('sex options');
    if (stRes.status === 'fulfilled') setStatuses(stRes.value.data.map((s: LookupOption) => ({ id: s.id, name: s.name })));
    else failed.push('statuses');
    if (locRes.status === 'fulfilled') setLocations(flattenLocations(locRes.value.data).map((l) => ({ id: l.id, name: indentLocationLabel(l.name, l.depth) })));
    else failed.push('locations');
    if (acRes.status === 'fulfilled') setAgeCategories(acRes.value.data.map((c: LookupOption) => ({ id: c.id, name: c.name })));
    else failed.push('age categories');
    if (ltRes.status === 'fulfilled') setLocationTypes(ltRes.value.data.map((lt: LookupOption) => ({ id: lt.id, name: lt.name })));
    else failed.push('location types');
    if (anRes.status === 'fulfilled') setAllAnimals(anRes.value.data.items.map((a: { id: string; tagNumber: string; name?: string; sexValue?: string }) => ({ id: a.id, tagNumber: a.tagNumber, name: a.name, sexValue: a.sexValue })));
    else failed.push('animals');
    if (failed.length > 0) {
      console.error('Failed to load animal form lookups:', failed.join(', '));
      message.error(`Failed to load some form options: ${failed.join(', ')}`);
    }
  }, []);

  /**
   * One read of the current page, size and filters.
   *
   * The URL is the single source of truth, so every control only writes to it and lets this run:
   * a filter cannot be applied while the table is still on page 3 of the old result, and a page
   * size cannot change without the page index being re-resolved against it.
   */
  const load = useCallback(async () => {
    setLoading(true);
    try {
      const params: AnimalListFilter = { page, pageSize, ...filters };
      const res = await animalsApi.list(params);
      setData(res.data.items as unknown as AnimalListItem[]);
      setTotal(res.data.totalCount);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, [page, pageSize, filters]);

  /** Re-reads the first page after a write, whatever page the user was on. */
  const refresh = () => {
    updateParams({ page: undefined });
    setRefreshToken((token) => token + 1);
  };

  useEffect(() => {
    const timer = window.setTimeout(() => { void loadLookups(); }, 0);
    return () => window.clearTimeout(timer);
  }, [loadLookups]);

  useEffect(() => {
    const timer = window.setTimeout(() => { void load(); }, 0);
    return () => window.clearTimeout(timer);
  }, [load, refreshToken]);

  const handleBreedsForType = async (typeId: string) => {
    if (!typeId) { setBreeds([]); return; }
    try {
      const res = await lookupsApi.breeds(typeId);
      setBreeds(res.data.map((b: LookupOption) => ({ id: b.id, name: b.name })));
    } catch {}
  };

  /** Runs whenever the Animal Type select changes (user pick or quick-add). */
  const handleAnimalTypeChange = async (typeId?: string) => {
    if (!typeId) { setBreeds([]); return; }
    try {
      const res = await lookupsApi.breeds(typeId);
      const list = res.data.map((b: LookupOption) => ({ id: b.id, name: b.name }));
      setBreeds(list);
      // A selected breed from the previous type is no longer valid.
      const currentBreedId = form.getFieldValue('breedId') as string | undefined;
      if (currentBreedId && !list.some((b) => b.id === currentBreedId)) {
        form.setFieldValue('breedId', undefined);
      }
    } catch {}
  };

  // ─── Inline quick-add ──────────────────────────────────────────────────
  // The modal fields and create payloads live in the shared registry
  // (components/lookupQuickAdd.ts); this only keeps the option lists in step.

  /**
   * Mirrors a lookup created from inside the form into its option list. A
   * location refetches its tree rather than appending, so nesting and depth
   * indentation stay correct — including inside the still-open Add Location
   * modal, whose parent and location-type selects read these lists.
   */
  const handleLookupCreated = async (created: CreatedLookup, kind?: LookupKind) => {
    switch (kind) {
      case 'animalType':
        setAnimalTypes((prev) => [...prev, { id: created.id, name: created.label, breeds: [] }]);
        break;
      case 'breed':
        setBreeds((prev) => [...prev, { id: created.id, name: created.label }]);
        break;
      case 'sexOption':
        setSexOptions((prev) => [...prev, { id: created.id, name: created.label }]);
        break;
      case 'animalStatus':
        setStatuses((prev) => [...prev, { id: created.id, name: created.label }]);
        break;
      case 'ageCategory':
        setAgeCategories((prev) => [...prev, { id: created.id, name: created.label }]);
        break;
      case 'locationType':
        setLocationTypes((prev) => [...prev, { id: created.id, name: created.label }]);
        break;
      case 'location': {
        // Re-fetch so nested locations (including the new one) appear correctly.
        const refreshed = await lookupsApi.locations();
        setLocations(
          flattenLocations(refreshed.data).map((l) => ({
            id: l.id,
            name: indentLocationLabel(l.name, l.depth),
          })),
        );
        break;
      }
      default:
        break;
    }
  };

  /**
   * The options a parent field may offer. A sire is male and a dam is female, neither may be
   * the animal being edited, and the parent already on the record is kept in the list even
   * when it fails that rule — the farm holds one such record (a female recorded as the sire of
   * TAG-0079 Boocho), and it has to keep showing its parent by tag so the mistake can be seen
   * and corrected instead of surfacing as a raw id. `utils/animalSex` holds the rule, and the
   * API applies the same one on save.
   */
  const parentOptions = (required: 'male' | 'female', selectedId?: string) =>
    parentCandidates(allAnimals, required, { editingId: editing?.id, selectedId }).map((animal) => ({
      value: animal.id,
      label: animal.name ? `${animal.tagNumber} - ${animal.name}` : animal.tagNumber,
    }));

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    setBreeds([]);
    setModalOpen(true);
  };

  const openEdit = async (record: AnimalListItem) => {
    setEditing(record);
    form.resetFields();
    if (record.breedId && record.animalTypeId) {
      await handleBreedsForType(record.animalTypeId);
    }
    form.setFieldsValue({
      ...record,
      dateOfBirth: record.dateOfBirth ? dayjs(record.dateOfBirth) : undefined,
    });
    setModalOpen(true);
  };

  const handleSave = async () => {
    try {
      const values = await form.validateFields();
      const payload: CreateAnimalPayload = {
        tagNumber: values.tagNumber,
        name: values.name,
        animalTypeId: values.animalTypeId,
        breedId: values.breedId,
        sexOptionId: values.sexOptionId,
        ageCategoryId: values.ageCategoryId,
        animalStatusId: values.animalStatusId,
        locationId: values.locationId,
        sireId: values.sireId,
        damId: values.damId,
        dateOfBirth: values.dateOfBirth?.toISOString(),
        acquisitionDate: values.acquisitionDate?.toISOString(),
        notes: values.notes,
      };
      if (editing) {
        await animalsApi.update(editing.id, payload);
        message.success(translate('animalUpdated'));
      } else {
        await animalsApi.create(payload);
        message.success(translate('animalCreated'));
      }
      setModalOpen(false);
      refresh();
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await animalsApi.delete(id);
      message.success(translate('animalDeleted'));
      refresh();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  /** Applies a filter change in the URL, returns to the first page and drops a selection made under the old filter. */
  const applyFilters = (patch: Partial<ListFilters>) => {
    updateParams({ ...patch, page: undefined });
    setSelectedRowKeys([]);
  };

  const activeFilterCount = Object.values(filters).filter(Boolean).length;

  const clearFilters = () =>
    applyFilters({
      search: undefined,
      animalTypeId: undefined,
      statusId: undefined,
      locationId: undefined,
      sexOptionId: undefined,
    });

  const openBulk = (action: BulkAction) => {
    setBulkAction(action);
    setBulkStatusId(undefined);
    setBulkLocationId(undefined);
    setBulkReason('');
  };

  /**
   * Both bulk writes take the same shape — the ticked ids, one change, an optional reason — and
   * both answer per animal rather than all-or-nothing, so a partial result is reported as
   * "x of y" and the selection is cleared only once the server has answered.
   */
  const handleBulkConfirm = async () => {
    if (!bulkAction || selectedRowKeys.length === 0) return;

    const ids = selectedRowKeys;
    const reason = bulkReason.trim() || undefined;
    // Resolved before the request so the modal's Apply stays inert until its one required field
    // is chosen, and the id is narrowed rather than asserted.
    const request = bulkAction === 'status'
      ? (bulkStatusId
        ? () => animalsApi.bulkChangeStatus({ animalIds: ids, newStatusId: bulkStatusId, reason })
        : null)
      : (bulkLocationId
        ? () => animalsApi.bulkTransfer({ animalIds: ids, toLocationId: bulkLocationId, reason })
        : null);
    if (!request) return;

    setBulkSaving(true);
    try {
      const result: BulkOperationResult = (await request()).data;

      message.success(
        bulkAction === 'status'
          ? translate('bulkStatusFinished', { count: result.successCount, total: result.requestedCount })
          : translate('bulkTransferFinished', { count: result.successCount, total: result.requestedCount }),
      );
      if (result.failures.length > 0) {
        message.warning(translate('bulkRefused', { count: result.failures.length }));
      }

      setBulkAction(null);
      setSelectedRowKeys([]);
      refresh();
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setBulkSaving(false);
    }
  };

  const columns: ColumnsType<AnimalListItem> = [
    {
      title: translate('tag'),
      dataIndex: 'tagNumber',
      key: 'tagNumber',
      render: (text: string, record) => (
        <a onClick={() => navigate(`/dashboard/animals/${record.id}`)}>{text}</a>
      ),
    },
    {
      title: translate('name'),
      dataIndex: 'name',
      key: 'name',
      render: (text: string) => text || '-',
    },
    {
      title: translate('type'),
      dataIndex: 'animalTypeName',
      key: 'animalTypeName',
    },
    {
      title: translate('breed'),
      dataIndex: 'breedName',
      key: 'breedName',
      render: (text: string) => text || '-',
    },
    {
      title: translate('sex'),
      dataIndex: 'sexValue',
      key: 'sexValue',
    },
    {
      title: translate('status'),
      dataIndex: 'statusName',
      key: 'statusName',
      render: (text: string, record) => (
        <Tag color={STATUS_COLORS[record.statusCategory] || 'default'}>{text}</Tag>
      ),
    },
    {
      title: translate('sire'),
      dataIndex: 'sireTagNumber',
      key: 'sireTagNumber',
      render: (text: string) => text || '-',
    },
    {
      title: translate('dam'),
      dataIndex: 'damTagNumber',
      key: 'damTagNumber',
      render: (text: string) => text || '-',
    },
    {
      title: translate('dob'),
      dataIndex: 'dateOfBirth',
      key: 'dateOfBirth',
      render: (text: string) => text ? formatDate(text) : '-',
    },
    {
      title: translate('actions'),
      key: 'actions',
      width: 120,
      render: (_, record) => (
        <Space>
          <Button size="small" icon={<EyeOutlined />} onClick={() => navigate(`/dashboard/animals/${record.id}`)} />
          <Button size="small" icon={<EditOutlined />} onClick={() => openEdit(record)} />
          <Popconfirm title={translate('deleteThisAnimal')} onConfirm={() => handleDelete(record.id)}>
            <Button size="small" danger icon={<DeleteOutlined />} />
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <>
      <Card
        title={translate('animals')}
        extra={
          <Space>
            {/*
              Three ways in that are not "type a tag": scan one, print a sheet of them, import a
              file. The scan button is the same component as the header's, so there is one scan
              flow rather than one per entry point.
            */}
            <AnimalScanButton />
            <Button icon={<PrinterOutlined />} onClick={() => navigate('/dashboard/animals/labels')}>
              {translate('printLabels')}
            </Button>
            <Button icon={<UploadOutlined />} onClick={() => navigate('/dashboard/animals/import')}>
              {translate('import')}
            </Button>
            <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>{translate('addAnimal')}</Button>
          </Space>
        }
      >
        {/*
          Filters live in a row of their own rather than in the card head: five controls plus the
          four entry points above cannot share one line at any width, and `fms-filter-row` is the
          app's existing rule for stacking them one per line on a phone.
        */}
        <Row className="fms-filter-row" gutter={[8, 8]} style={{ marginBottom: 12 }}>
          <Col xs={24} sm={12} md={6} lg={5}>
            <Input.Search
              aria-label={translate('searchByTagOrName')}
              placeholder={translate('searchByTagOrName')}
              value={searchValue}
              onChange={(event) => {
                const value = event.target.value;
                // Clearing the box applies at once, instead of leaving an invisible search in force.
                if (!value) {
                  setSearchDraft(null);
                  applyFilters({ search: undefined });
                  return;
                }
                setSearchDraft(value);
              }}
              allowClear
              onSearch={(value) => {
                setSearchDraft(null);
                applyFilters({ search: value.trim() || undefined });
              }}
            />
          </Col>
          <Col xs={12} sm={6} md={4} lg={3}>
            <Select
              aria-label={translate('filterByType')}
              allowClear
              placeholder={translate('allAnimalTypes')}
              style={{ width: '100%' }}
              value={filters.animalTypeId}
              onChange={(value) => applyFilters({ animalTypeId: value || undefined })}
              options={animalTypes.map((type) => ({ value: type.id, label: type.name }))}
            />
          </Col>
          <Col xs={12} sm={6} md={4} lg={3}>
            <Select
              aria-label={translate('filterBySex')}
              allowClear
              placeholder={translate('allSexes')}
              style={{ width: '100%' }}
              value={filters.sexOptionId}
              onChange={(value) => applyFilters({ sexOptionId: value || undefined })}
              options={sexOptions.map((option) => ({ value: option.id, label: option.name }))}
            />
          </Col>
          <Col xs={12} sm={6} md={4} lg={3}>
            <Select
              aria-label={translate('filterByStatus')}
              allowClear
              placeholder={translate('allStatuses')}
              style={{ width: '100%' }}
              value={filters.statusId}
              onChange={(value) => applyFilters({ statusId: value || undefined })}
              options={statuses.map((status) => ({ value: status.id, label: status.name }))}
            />
          </Col>
          <Col xs={12} sm={6} md={4} lg={4}>
            <Select
              aria-label={translate('filterByLocation')}
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder={translate('allLocations')}
              style={{ width: '100%' }}
              value={filters.locationId}
              onChange={(value) => applyFilters({ locationId: value || undefined })}
              options={locations.map((location) => ({ value: location.id, label: location.name }))}
            />
          </Col>
          <Col xs={24} sm={12} md={4} lg={3}>
            <Button onClick={clearFilters} disabled={activeFilterCount === 0} block>
              {translate('clearFilters')}
            </Button>
          </Col>
        </Row>

        {selectedRowKeys.length > 0 && (
          <Space wrap style={{ marginBottom: 12 }}>
            <Text strong>{translate('selectedCount', { count: selectedRowKeys.length })}</Text>
            {/* Stated at the button the API refuses, not after a status has been picked and Apply
                pressed; the buttons stay visible because the demo shows what the app can do. */}
            <Tooltip title={demoBulkHint}>
              <span>
                <Button icon={<CheckCircleOutlined />} disabled={isDemo} onClick={() => openBulk('status')}>
                  {translate('changeStatus')}
                </Button>
              </span>
            </Tooltip>
            <Tooltip title={demoBulkHint}>
              <span>
                <Button icon={<SwapOutlined />} disabled={isDemo} onClick={() => openBulk('transfer')}>
                  {translate('transferToLocation')}
                </Button>
              </span>
            </Tooltip>
            <Button type="link" onClick={() => setSelectedRowKeys([])}>
              {translate('clearSelection')}
            </Button>
          </Space>
        )}

        <Table
          rowKey="id"
          columns={columns}
          dataSource={data}
          loading={loading}
          rowSelection={{
            selectedRowKeys,
            // Kept across pages on purpose: a bulk change is usually several pages of animals,
            // and dropping the selection on page change would make that impossible.
            preserveSelectedRowKeys: true,
            onChange: (keys) => setSelectedRowKeys(keys.map(String)),
          }}
          pagination={{
            current: page,
            total,
            pageSize,
            showSizeChanger: true,
            pageSizeOptions: PAGE_SIZE_OPTIONS,
            onChange: (nextPage, nextPageSize) => {
              // A new page size re-resolves the page index, so both always move together.
              if (nextPageSize !== pageSize) {
                updateParams({
                  pageSize: nextPageSize === DEFAULT_PAGE_SIZE ? undefined : nextPageSize,
                  page: undefined,
                });
                return;
              }
              updateParams({ page: nextPage === 1 ? undefined : nextPage });
            },
          }}
        />
      </Card>

      <Modal
        title={editing ? translate('editAnimal') : translate('addAnimal')}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => setModalOpen(false)}
        width={720}
        destroyOnClose
      >
        <Form form={form} layout="vertical">
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="tagNumber" label={translate('tagNumber')} rules={[{ required: true }]}>
                <Input maxLength={50} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="name" label={translate('name')}>
                <Input maxLength={200} />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="animalTypeId" label={translate('animalType')} rules={[{ required: true }]}>
                <LookupQuickAddSelect
                  kind="animalType"
                  options={animalTypes.map((t) => ({ value: t.id, label: t.name }))}
                  onCreated={handleLookupCreated}
                  onValueSelected={(v) => void handleAnimalTypeChange(v)}
                  placeholder={translate('selectAnimalType')}
                />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="breedId" label={translate('breed')}>
                <LookupQuickAddSelect
                  kind="breed"
                  ctx={{ animalTypeId: selectedAnimalTypeId }}
                  options={breeds.map((b) => ({ value: b.id, label: b.name }))}
                  onCreated={handleLookupCreated}
                  allowClear
                  placeholder={selectedAnimalTypeId ? translate('selectBreed') : translate('selectAnAnimalTypeFirst')}
                />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="sexOptionId" label={translate('sex')} rules={[{ required: true }]}>
                <LookupQuickAddSelect
                  kind="sexOption"
                  options={sexOptions.map((s) => ({ value: s.id, label: s.name }))}
                  onCreated={handleLookupCreated}
                  placeholder={translate('selectSex')}
                />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="animalStatusId" label={translate('status')} rules={[{ required: true }]}>
                <LookupQuickAddSelect
                  kind="animalStatus"
                  options={statuses.map((s) => ({ value: s.id, label: s.name }))}
                  onCreated={handleLookupCreated}
                  placeholder={translate('selectStatus')}
                />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="locationId" label={translate('location')}>
                <LookupQuickAddSelect
                  kind="location"
                  ctx={{
                    locationTypes: locationTypes.map((lt) => ({ value: lt.id, label: lt.name })),
                    locations: locations.map((l) => ({ value: l.id, label: l.name })),
                  }}
                  options={locations.map((l) => ({ value: l.id, label: l.name }))}
                  onCreated={handleLookupCreated}
                  allowClear
                  placeholder={translate('selectLocation')}
                />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="ageCategoryId" label={translate('ageCategory')}>
                <LookupQuickAddSelect
                  kind="ageCategory"
                  options={ageCategories.map((c) => ({ value: c.id, label: c.name }))}
                  onCreated={handleLookupCreated}
                  allowClear
                  placeholder={translate('selectAgeCategory')}
                />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="sireId" label={translate('sireFather')}>
                <Select
                  options={parentOptions('male', editing?.sireId)}
                  showSearch
                  optionFilterProp="label"
                  allowClear
                  placeholder={translate('selectSire')}
                  notFoundContent={translate('noCandidateAnimalsForThisSex')}
                  style={{ width: '100%' }}
                  popupMatchSelectWidth={false}
                />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="damId" label={translate('damMother')}>
                <Select
                  options={parentOptions('female', editing?.damId)}
                  showSearch
                  optionFilterProp="label"
                  allowClear
                  placeholder={translate('selectDam')}
                  notFoundContent={translate('noCandidateAnimalsForThisSex')}
                  style={{ width: '100%' }}
                  popupMatchSelectWidth={false}
                />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="dateOfBirth" label={translate('dateOfBirth')}>
                <DatePicker style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="acquisitionDate" label={translate('acquisitionDate')}>
                <DatePicker style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="notes" label={translate('notes')}>
            <Input.TextArea rows={2} maxLength={2000} />
          </Form.Item>
        </Form>
      </Modal>

      {/* One modal per bulk action: the selection is the only thing the two share, and each
          action has exactly one required field plus the same optional reason. */}
      <Modal
        title={translate('changeStatusForSelected', { count: selectedRowKeys.length })}
        open={bulkAction === 'status'}
        onOk={handleBulkConfirm}
        onCancel={() => setBulkAction(null)}
        confirmLoading={bulkSaving}
        okText={translate('apply')}
        okButtonProps={{ disabled: !bulkStatusId }}
        destroyOnHidden
      >
        <Form layout="vertical">
          <Form.Item label={translate('newStatus')} required>
            <Select
              aria-label={translate('newStatus')}
              value={bulkStatusId}
              onChange={(value: string) => setBulkStatusId(value)}
              options={statuses.map((status) => ({ value: status.id, label: status.name }))}
              placeholder={translate('selectStatus')}
              style={{ width: '100%' }}
            />
          </Form.Item>
          <Form.Item label={translate('reasonOptional')}>
            <Input.TextArea
              rows={2}
              maxLength={500}
              value={bulkReason}
              onChange={(event) => setBulkReason(event.target.value)}
            />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title={translate('transferSelected', { count: selectedRowKeys.length })}
        open={bulkAction === 'transfer'}
        onOk={handleBulkConfirm}
        onCancel={() => setBulkAction(null)}
        confirmLoading={bulkSaving}
        okText={translate('apply')}
        okButtonProps={{ disabled: !bulkLocationId }}
        destroyOnHidden
      >
        <Form layout="vertical">
          <Form.Item label={translate('destinationLocation')} required>
            <Select
              aria-label={translate('destinationLocation')}
              value={bulkLocationId}
              onChange={(value: string) => setBulkLocationId(value)}
              options={locations.map((location) => ({ value: location.id, label: location.name }))}
              placeholder={translate('selectLocation')}
              showSearch
              optionFilterProp="label"
              style={{ width: '100%' }}
            />
          </Form.Item>
          <Form.Item label={translate('reasonOptional')}>
            <Input.TextArea
              rows={2}
              maxLength={500}
              value={bulkReason}
              onChange={(event) => setBulkReason(event.target.value)}
            />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
}
