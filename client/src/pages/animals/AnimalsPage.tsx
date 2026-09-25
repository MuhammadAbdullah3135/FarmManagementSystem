import { useState, useEffect, useCallback } from 'react';
import { Card, Table, Button, Modal, Form, Input, Select, DatePicker, Space, Tag, message, Popconfirm, Row, Col } from 'antd';
import { flattenLocations, type AnimalType } from '../../api/configuration';
import type { ColumnsType } from 'antd/es/table';
import { PlusOutlined, EditOutlined, DeleteOutlined, EyeOutlined, UploadOutlined } from '@ant-design/icons';
import { animalsApi, type AnimalListFilter, type CreateAnimalPayload } from '../../api/animals';
import { lookupsApi } from '../../api/attendance';
import { getApiError } from '../../api/farmApi';
import LookupQuickAddSelect, { type CreatedLookup } from '../../components/LookupQuickAddSelect';
import type { LookupKind } from '../../components/lookupQuickAdd';
import { formatDate } from '../../i18n/format';
import dayjs from 'dayjs';
import { useNavigate } from 'react-router-dom';
import type { AnimalListItem } from '../../types';
import { useTranslation } from 'react-i18next';

interface LookupOption {
  id: string;
  name: string;
  value?: string;
}

const STATUS_COLORS: Record<number, string> = { 0: 'green', 1: 'orange', 2: 'red' };

/** Indents nested location labels so the tree structure is visible in the select. */
const indentLocationLabel = (name: string, depth: number) => `${'\u00A0\u00A0'.repeat(depth)}${name}`;

export default function AnimalsPage() {const { t: translate } = useTranslation('animals'); 
  const navigate = useNavigate();
  const [data, setData] = useState<AnimalListItem[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<AnimalListItem | null>(null);
  const [animalTypes, setAnimalTypes] = useState<AnimalType[]>([]);
  const [breeds, setBreeds] = useState<LookupOption[]>([]);
  const [sexOptions, setSexOptions] = useState<LookupOption[]>([]);
  const [statuses, setStatuses] = useState<LookupOption[]>([]);
  const [locations, setLocations] = useState<LookupOption[]>([]);
  const [ageCategories, setAgeCategories] = useState<LookupOption[]>([]);
  const [locationTypes, setLocationTypes] = useState<LookupOption[]>([]);
  const [allAnimals, setAllAnimals] = useState<{ id: string; tagNumber: string; name?: string }[]>([]);
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
    if (anRes.status === 'fulfilled') setAllAnimals(anRes.value.data.items.map((a: { id: string; tagNumber: string; name?: string }) => ({ id: a.id, tagNumber: a.tagNumber, name: a.name })));
    else failed.push('animals');
    if (failed.length > 0) {
      console.error('Failed to load animal form lookups:', failed.join(', '));
      message.error(`Failed to load some form options: ${failed.join(', ')}`);
    }
  }, []);

  const load = useCallback(async (p = page, filters?: AnimalListFilter) => {
    setLoading(true);
    try {
      const params: AnimalListFilter = { page: p, pageSize: 10, ...filters };
      const res = await animalsApi.list(params);
      setData(res.data.items as unknown as AnimalListItem[]);
      setTotal(res.data.totalCount);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, [page]);

  useEffect(() => {
    const timer = window.setTimeout(() => { void loadLookups(); void load(1); }, 0);
    return () => window.clearTimeout(timer);
  }, [loadLookups, load]);

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
      load(1);
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await animalsApi.delete(id);
      message.success(translate('animalDeleted'));
      load(page);
    } catch (err) {
      message.error(getApiError(err));
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
            <Button icon={<UploadOutlined />} onClick={() => navigate('/dashboard/animals/import')}>
              {translate('import')}
            </Button>
            <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>{translate('addAnimal')}</Button>
          </Space>
        }
      >
        <Table
          rowKey="id"
          columns={columns}
          dataSource={data}
          loading={loading}
          pagination={{ current: page, total, pageSize: 10, onChange: (p) => { setPage(p); load(p); } }}
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
                  options={allAnimals.map(a => ({ value: a.id, label: a.name ? `${a.tagNumber} - ${a.name}` : a.tagNumber }))}
                  showSearch
                  optionFilterProp="label"
                  allowClear
                  placeholder={translate('selectSire')}
                  style={{ width: '100%' }}
                  popupMatchSelectWidth={false}
                />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="damId" label={translate('damMother')}>
                <Select
                  options={allAnimals.map(a => ({ value: a.id, label: a.name ? `${a.tagNumber} - ${a.name}` : a.tagNumber }))}
                  showSearch
                  optionFilterProp="label"
                  allowClear
                  placeholder={translate('selectDam')}
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
    </>
  );
}
