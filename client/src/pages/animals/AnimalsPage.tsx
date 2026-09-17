import { useState, useEffect, useCallback } from 'react';
import { Card, Table, Button, Modal, Form, Input, Select, DatePicker, Space, Tag, message, Popconfirm, Row, Col } from 'antd';
import { configurationApi, flattenLocations, type AnimalType, type Breed } from '../../api/configuration';
import type { ColumnsType } from 'antd/es/table';
import { PlusOutlined, EditOutlined, DeleteOutlined, EyeOutlined } from '@ant-design/icons';
import { animalsApi, type AnimalListFilter, type CreateAnimalPayload } from '../../api/animals';
import { lookupsApi } from '../../api/attendance';
import { getApiError } from '../../api/farmApi';
import LookupQuickAddSelect, { type QuickAddFieldSpec } from '../../components/LookupQuickAddSelect';
import dayjs from 'dayjs';
import { useNavigate } from 'react-router-dom';
import type { AnimalListItem } from '../../types';

interface LookupOption {
  id: string;
  name: string;
  value?: string;
}

const STATUS_COLORS: Record<number, string> = { 0: 'green', 1: 'orange', 2: 'red' };

const STATUS_CATEGORY_LABELS: Record<number, string> = { 0: 'Active', 1: 'Inactive', 2: 'Terminal' };

/** Indents nested location labels so the tree structure is visible in the select. */
const indentLocationLabel = (name: string, depth: number) => `${'\u00A0\u00A0'.repeat(depth)}${name}`;

const ANIMAL_TYPE_QUICK_ADD_FIELDS: QuickAddFieldSpec[] = [
  { name: 'name', label: 'Name', widget: 'input', required: true, maxLength: 100, placeholder: 'e.g. Cattle' },
];

const BREED_QUICK_ADD_FIELDS: QuickAddFieldSpec[] = [
  { name: 'name', label: 'Name', widget: 'input', required: true, maxLength: 100, placeholder: 'e.g. Sahiwal' },
  { name: 'averageGestationDays', label: 'Average Gestation (days)', widget: 'number', min: 1, max: 999, initialValue: 283 },
];

const SEX_QUICK_ADD_FIELDS: QuickAddFieldSpec[] = [
  { name: 'value', label: 'Value', widget: 'input', required: true, maxLength: 50, placeholder: 'e.g. Female' },
];

const STATUS_QUICK_ADD_FIELDS: QuickAddFieldSpec[] = [
  { name: 'name', label: 'Name', widget: 'input', required: true, maxLength: 100, placeholder: 'e.g. Quarantined' },
  {
    name: 'category',
    label: 'Category',
    widget: 'select',
    initialValue: 0,
    options: Object.keys(STATUS_CATEGORY_LABELS).map((k) => ({ value: Number(k), label: STATUS_CATEGORY_LABELS[Number(k)] })),
  },
  { name: 'isActive', label: 'Active', widget: 'switch', initialValue: true },
];

const AGE_QUICK_ADD_FIELDS: QuickAddFieldSpec[] = [
  { name: 'name', label: 'Name', widget: 'input', required: true, maxLength: 100, placeholder: 'e.g. Calf' },
  { name: 'minDays', label: 'Min Age (days)', widget: 'number', min: 0, initialValue: 0 },
  { name: 'maxDays', label: 'Max Age (days)', widget: 'number', min: 0, initialValue: 99999 },
];

const LOCATION_TYPE_QUICK_ADD_FIELDS: QuickAddFieldSpec[] = [
  { name: 'name', label: 'Name', widget: 'input', required: true, maxLength: 100, placeholder: 'e.g. Shed' },
];

export default function AnimalsPage() {
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

  // ─── Inline quick-add handlers (each returns the created option id) ────

  const handleQuickAddAnimalType = async (values: Record<string, unknown>) => {
    const res = await configurationApi.createAnimalType({ name: String(values.name) });
    const created = res.data as AnimalType;
    setAnimalTypes((prev) => [...prev, created]);
    return created.id;
  };

  const handleQuickAddBreed = async (values: Record<string, unknown>) => {
    const animalTypeId = form.getFieldValue('animalTypeId') as string | undefined;
    if (!animalTypeId) throw new Error('Select an animal type first');
    const res = await configurationApi.createBreed({
      name: String(values.name),
      animalTypeId,
      averageGestationDays: Number(values.averageGestationDays ?? 283),
    });
    const created = res.data as Breed;
    setBreeds((prev) => [...prev, { id: created.id, name: created.name }]);
    return created.id;
  };

  const handleQuickAddSexOption = async (values: Record<string, unknown>) => {
    const res = await configurationApi.createSexOption({ value: String(values.value) });
    const created = res.data as { id: string; value: string };
    setSexOptions((prev) => [...prev, { id: created.id, name: created.value }]);
    return created.id;
  };

  const handleQuickAddStatus = async (values: Record<string, unknown>) => {
    const res = await configurationApi.createStatus({
      name: String(values.name),
      isActive: Boolean(values.isActive ?? true),
      category: Number(values.category ?? 0),
    });
    const created = res.data as { id: string; name: string };
    setStatuses((prev) => [...prev, { id: created.id, name: created.name }]);
    return created.id;
  };

  const handleQuickAddAgeCategory = async (values: Record<string, unknown>) => {
    const res = await configurationApi.createAgeCategory({
      name: String(values.name),
      minDays: Number(values.minDays ?? 0),
      maxDays: Number(values.maxDays ?? 99999),
    });
    const created = res.data as { id: string; name: string };
    setAgeCategories((prev) => [...prev, { id: created.id, name: created.name }]);
    return created.id;
  };

  const handleQuickAddLocation = async (values: Record<string, unknown>) => {
    const res = await configurationApi.createLocation({
      name: String(values.name),
      locationTypeId: String(values.locationTypeId),
      parentLocationId: values.parentLocationId ? String(values.parentLocationId) : undefined,
    });
    // Re-fetch so nested locations (including the new one) appear correctly.
    const refreshed = await lookupsApi.locations();
    setLocations(flattenLocations(refreshed.data).map((l) => ({ id: l.id, name: indentLocationLabel(l.name, l.depth) })));
    return res.data.id as string;
  };

  const handleQuickAddLocationType = async (values: Record<string, unknown>) => {
    const res = await configurationApi.createLocationType({ name: String(values.name) });
    const created = res.data as { id: string; name: string };
    setLocationTypes((prev) => [...prev, { id: created.id, name: created.name }]);
    return created.id;
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
        message.success('Animal updated');
      } else {
        await animalsApi.create(payload);
        message.success('Animal created');
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
      message.success('Animal deleted');
      load(page);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ColumnsType<AnimalListItem> = [
    {
      title: 'Tag',
      dataIndex: 'tagNumber',
      key: 'tagNumber',
      render: (text: string, record) => (
        <a onClick={() => navigate(`/dashboard/animals/${record.id}`)}>{text}</a>
      ),
    },
    {
      title: 'Name',
      dataIndex: 'name',
      key: 'name',
      render: (text: string) => text || '-',
    },
    {
      title: 'Type',
      dataIndex: 'animalTypeName',
      key: 'animalTypeName',
    },
    {
      title: 'Breed',
      dataIndex: 'breedName',
      key: 'breedName',
      render: (text: string) => text || '-',
    },
    {
      title: 'Sex',
      dataIndex: 'sexValue',
      key: 'sexValue',
    },
    {
      title: 'Status',
      dataIndex: 'statusName',
      key: 'statusName',
      render: (text: string, record) => (
        <Tag color={STATUS_COLORS[record.statusCategory] || 'default'}>{text}</Tag>
      ),
    },
    {
      title: 'Sire',
      dataIndex: 'sireTagNumber',
      key: 'sireTagNumber',
      render: (text: string) => text || '-',
    },
    {
      title: 'Dam',
      dataIndex: 'damTagNumber',
      key: 'damTagNumber',
      render: (text: string) => text || '-',
    },
    {
      title: 'DOB',
      dataIndex: 'dateOfBirth',
      key: 'dateOfBirth',
      render: (text: string) => text ? dayjs(text).format('YYYY-MM-DD') : '-',
    },
    {
      title: 'Actions',
      key: 'actions',
      width: 120,
      render: (_, record) => (
        <Space>
          <Button size="small" icon={<EyeOutlined />} onClick={() => navigate(`/dashboard/animals/${record.id}`)} />
          <Button size="small" icon={<EditOutlined />} onClick={() => openEdit(record)} />
          <Popconfirm title="Delete this animal?" onConfirm={() => handleDelete(record.id)}>
            <Button size="small" danger icon={<DeleteOutlined />} />
          </Popconfirm>
        </Space>
      ),
    },
  ];

  const locationTypeField = (nesting: 'top' | 'nested'): QuickAddFieldSpec => ({
    name: 'locationTypeId',
    label: 'Location Type',
    widget: 'select',
    required: true,
    placeholder: 'Select location type',
    initialValue: locationTypes[0]?.id,
    options: locationTypes.map((lt) => ({ value: lt.id, label: lt.name })),
    // Location types are a prerequisite for creating a location — allow adding
    // one without leaving the Add Location modal.
    ...(nesting === 'top' ? { nestedQuickAdd: { label: 'Location Type', fields: LOCATION_TYPE_QUICK_ADD_FIELDS, onQuickAdd: handleQuickAddLocationType } } : {}),
  });

  const locationQuickAddFields: QuickAddFieldSpec[] = [
    { name: 'name', label: 'Name', widget: 'input', required: true, maxLength: 200, placeholder: 'e.g. Shed A' },
    locationTypeField('top'),
    {
      name: 'parentLocationId',
      label: 'Parent Location (optional)',
      widget: 'select',
      placeholder: 'None (top level)',
      options: locations.map((l) => ({ value: l.id, label: l.name })),
      // A missing parent location can be created inline as well; it only needs
      // a name and a location type (which itself can be added inline).
      nestedQuickAdd: {
        label: 'Location',
        fields: [
          { name: 'name', label: 'Name', widget: 'input', required: true, maxLength: 200, placeholder: 'e.g. Shed B' },
          locationTypeField('nested'),
        ],
        onQuickAdd: handleQuickAddLocation,
      },
    },
  ];

  return (
    <>
      <Card
        title="Animals"
        extra={<Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>Add Animal</Button>}
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
        title={editing ? 'Edit Animal' : 'Add Animal'}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => setModalOpen(false)}
        width={720}
        destroyOnClose
      >
        <Form form={form} layout="vertical">
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="tagNumber" label="Tag Number" rules={[{ required: true }]}>
                <Input maxLength={50} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="name" label="Name">
                <Input maxLength={200} />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="animalTypeId" label="Animal Type" rules={[{ required: true }]}>
                <LookupQuickAddSelect
                  label="Animal Type"
                  options={animalTypes.map((t) => ({ value: t.id, label: t.name }))}
                  fields={ANIMAL_TYPE_QUICK_ADD_FIELDS}
                  onQuickAdd={handleQuickAddAnimalType}
                  onValueSelected={(v) => void handleAnimalTypeChange(v)}
                  placeholder="Select animal type"
                />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="breedId" label="Breed">
                <LookupQuickAddSelect
                  label="Breed"
                  options={breeds.map((b) => ({ value: b.id, label: b.name }))}
                  fields={BREED_QUICK_ADD_FIELDS}
                  onQuickAdd={handleQuickAddBreed}
                  allowClear
                  addDisabled={!selectedAnimalTypeId}
                  disabledHint="Select an Animal Type first, then add breeds for it"
                  placeholder={selectedAnimalTypeId ? 'Select breed' : 'Select an Animal Type first'}
                />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="sexOptionId" label="Sex" rules={[{ required: true }]}>
                <LookupQuickAddSelect
                  label="Sex"
                  options={sexOptions.map((s) => ({ value: s.id, label: s.name }))}
                  fields={SEX_QUICK_ADD_FIELDS}
                  onQuickAdd={handleQuickAddSexOption}
                  placeholder="Select sex"
                />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="animalStatusId" label="Status" rules={[{ required: true }]}>
                <LookupQuickAddSelect
                  label="Status"
                  options={statuses.map((s) => ({ value: s.id, label: s.name }))}
                  fields={STATUS_QUICK_ADD_FIELDS}
                  onQuickAdd={handleQuickAddStatus}
                  placeholder="Select status"
                />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="locationId" label="Location">
                <LookupQuickAddSelect
                  label="Location"
                  options={locations.map((l) => ({ value: l.id, label: l.name }))}
                  fields={locationQuickAddFields}
                  onQuickAdd={handleQuickAddLocation}
                  allowClear
                  placeholder="Select location"
                />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="ageCategoryId" label="Age Category">
                <LookupQuickAddSelect
                  label="Age Category"
                  options={ageCategories.map((c) => ({ value: c.id, label: c.name }))}
                  fields={AGE_QUICK_ADD_FIELDS}
                  onQuickAdd={handleQuickAddAgeCategory}
                  allowClear
                  placeholder="Select age category"
                />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="sireId" label="Sire (Father)">
                <Select
                  options={allAnimals.map(a => ({ value: a.id, label: a.name ? `${a.tagNumber} - ${a.name}` : a.tagNumber }))}
                  showSearch
                  optionFilterProp="label"
                  allowClear
                  placeholder="Select sire"
                  style={{ width: '100%' }}
                  popupMatchSelectWidth={false}
                />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="damId" label="Dam (Mother)">
                <Select
                  options={allAnimals.map(a => ({ value: a.id, label: a.name ? `${a.tagNumber} - ${a.name}` : a.tagNumber }))}
                  showSearch
                  optionFilterProp="label"
                  allowClear
                  placeholder="Select dam"
                  style={{ width: '100%' }}
                  popupMatchSelectWidth={false}
                />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="dateOfBirth" label="Date of Birth">
                <DatePicker style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="acquisitionDate" label="Acquisition Date">
                <DatePicker style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="notes" label="Notes">
            <Input.TextArea rows={2} maxLength={2000} />
          </Form.Item>
        </Form>
      </Modal>
    </>
  );
}
