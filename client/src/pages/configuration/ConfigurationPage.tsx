import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, Col, Form, Input, InputNumber, Modal, Popconfirm, Row, Select, Switch, Table, Tabs, Tag, message,
} from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { PlusOutlined } from '@ant-design/icons';
import { configurationApi } from '../../api/configuration';
import type {
  AnimalType, Breed, SexOption, AgeCategory, AnimalStatus, LocationType, Location,
} from '../../api/configuration';
import { getApiError } from '../../api/farmApi';
import LookupQuickAddSelect from '../../components/LookupQuickAddSelect';
import { DirectionalGlyph } from '../../i18n/DirectionalIcon';
import { useTranslation } from 'react-i18next';

type ModalType =
  | 'animalType'
  | 'breed'
  | 'sexOption'
  | 'ageCategory'
  | 'status'
  | 'locationType'
  | 'location';

interface FlatLocation {
  id: string;
  name: string;
  depth: number;
  locationTypeId: string;
}

const STATUS_CATEGORY_LABELS: Record<number, string> = { 0: 'Active', 1: 'Inactive', 2: 'Terminal' };
const STATUS_CATEGORY_COLORS: Record<number, string> = { 0: 'green', 1: 'default', 2: 'red' };
const MAX_DAYS_SENTINEL = 99999;

const flattenLocationTree = (nodes: Location[], depth = 0): FlatLocation[] =>
  nodes.flatMap((n) => [
    { id: n.id, name: n.name, depth, locationTypeId: n.locationTypeId },
    ...flattenLocationTree(n.childLocations ?? [], depth + 1),
  ]);

const ConfigurationPage: React.FC = () => {const { t: translate } = useTranslation('configuration'); 
  const [animalTypes, setAnimalTypes] = useState<AnimalType[]>([]);
  const [breeds, setBreeds] = useState<Breed[]>([]);
  const [sexOptions, setSexOptions] = useState<SexOption[]>([]);
  const [ageCategories, setAgeCategories] = useState<AgeCategory[]>([]);
  const [statuses, setStatuses] = useState<AnimalStatus[]>([]);
  const [locationTypes, setLocationTypes] = useState<LocationType[]>([]);
  const [locations, setLocations] = useState<Location[]>([]);
  const [loading, setLoading] = useState(false);

  const [modalOpen, setModalOpen] = useState(false);
  const [modalType, setModalType] = useState<ModalType>('animalType');
  const [form] = Form.useForm();

  const load = useCallback(async () => {
    setLoading(true);
    try {
      // Each lookup settles independently: one failing list must not discard the six that loaded,
      // otherwise a failed refresh leaves the page stale and looks like a failed delete.
      const [at, br, so, ac, st, lt, loc] = await Promise.allSettled([
        configurationApi.animalTypes(),
        configurationApi.breeds(),
        configurationApi.sexOptions(),
        configurationApi.ageCategories(),
        configurationApi.statuses(),
        configurationApi.locationTypes(),
        configurationApi.locations(),
      ]);

      if (at.status === 'fulfilled') setAnimalTypes(at.value.data);
      if (br.status === 'fulfilled') setBreeds(br.value.data);
      if (so.status === 'fulfilled') setSexOptions(so.value.data);
      if (ac.status === 'fulfilled') setAgeCategories(ac.value.data);
      if (st.status === 'fulfilled') setStatuses(st.value.data);
      if (lt.status === 'fulfilled') setLocationTypes(lt.value.data);
      if (loc.status === 'fulfilled') setLocations(loc.value.data);

      const failures = [at, br, so, ac, st, lt, loc].filter(r => r.status === 'rejected');
      if (failures.length > 0) {
        const reason = getApiError((failures[0] as PromiseRejectedResult).reason);
        message.error(
          failures.length === 1 ? reason : `${reason} (${failures.length} lists failed to load)`,
        );
      }
    } catch (err) {
      // Building the request list throws when no farm is selected yet.
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(() => { void load(); }, 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  const openAdd = (type: ModalType) => {
    setModalType(type);
    form.resetFields();
    if (type === 'breed') form.setFieldsValue({ averageGestationDays: 283 });
    if (type === 'status') form.setFieldsValue({ isActive: true, category: 0 });
    if (type === 'ageCategory') form.setFieldsValue({ minDays: 0, maxDays: MAX_DAYS_SENTINEL });
    setModalOpen(true);
  };

  const handleSave = async () => {
    try {
      const values = await form.validateFields();
      switch (modalType) {
        case 'animalType':
          await configurationApi.createAnimalType({ name: values.name });
          message.success(translate('animalTypeCreated'));
          break;
        case 'breed':
          await configurationApi.createBreed({
            name: values.name,
            animalTypeId: values.animalTypeId,
            averageGestationDays: values.averageGestationDays ?? 283,
          });
          message.success(translate('breedCreated'));
          break;
        case 'sexOption':
          await configurationApi.createSexOption({ value: values.value });
          message.success(translate('sexOptionCreated'));
          break;
        case 'ageCategory':
          await configurationApi.createAgeCategory({
            name: values.name,
            minDays: values.minDays,
            maxDays: values.maxDays,
          });
          message.success(translate('ageCategoryCreated'));
          break;
        case 'status':
          await configurationApi.createStatus({
            name: values.name,
            isActive: values.isActive,
            category: values.category,
          });
          message.success(translate('statusCreated'));
          break;
        case 'locationType':
          await configurationApi.createLocationType({ name: values.name });
          message.success(translate('locationTypeCreated'));
          break;
        case 'location':
          await configurationApi.createLocation({
            name: values.name,
            locationTypeId: values.locationTypeId,
            parentLocationId: values.parentLocationId || undefined,
          });
          message.success(translate('locationCreated'));
          break;
      }
      setModalOpen(false);
      void load();
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const handleDelete = async (type: ModalType, id: string) => {
    try {
      switch (type) {
        case 'animalType': await configurationApi.deleteAnimalType(id); message.success(translate('animalTypeDeleted')); break;
        case 'breed': await configurationApi.deleteBreed(id); message.success(translate('breedDeleted')); break;
        case 'sexOption': await configurationApi.deleteSexOption(id); message.success(translate('sexOptionDeleted')); break;
        case 'ageCategory': await configurationApi.deleteAgeCategory(id); message.success(translate('ageCategoryDeleted')); break;
        case 'status': await configurationApi.deleteStatus(id); message.success(translate('statusDeleted')); break;
        case 'locationType': await configurationApi.deleteLocationType(id); message.success(translate('locationTypeDeleted')); break;
        case 'location': await configurationApi.deleteLocation(id); message.success(translate('locationDeleted')); break;
      }
      void load();
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const typeName: Record<ModalType, string> = {
    animalType: 'Animal Type',
    breed: 'Breed',
    sexOption: 'Sex Option',
    ageCategory: 'Age Category',
    status: 'Animal Status',
    locationType: 'Location Type',
    location: 'Location',
  };

  const breedTypeMap = new Map(animalTypes.map((t) => [t.id, t.name]));
  const locationTypeMap = new Map(locationTypes.map((lt) => [lt.id, lt.name]));
  const flatLocations = flattenLocationTree(locations);

  const addToolbar = (type: ModalType, label: string) => (
    <div style={{ marginBottom: 12 }}>
      <Button type="primary" size="small" icon={<PlusOutlined />} onClick={() => openAdd(type)}>
        {translate('add')} {label}
      </Button>
    </div>
  );

  const typeColumns: ColumnsType<AnimalType> = [
    { title: translate('name'), dataIndex: 'name' },
    { title: translate('breeds'), dataIndex: 'breeds', width: 100, render: (b: Breed[]) => <Tag>{b.length}</Tag> },
    {
      title: '', width: 90,
      render: (_, r) => (
        <Popconfirm
          title={translate('deleteThisAnimalType')}
          description={translate('itsBreedsWillAlsoBeRemoved')}
          onConfirm={() => handleDelete('animalType', r.id)}
        >
          <Button size="small" danger>{translate('delete')}</Button>
        </Popconfirm>
      ),
    },
  ];

  const breedColumns: ColumnsType<Breed> = [
    { title: translate('name'), dataIndex: 'name' },
    { title: translate('animalType'), dataIndex: 'animalTypeId', render: (id: string) => breedTypeMap.get(id) ?? '-' },
    { title: translate('avgGestationDays'), dataIndex: 'averageGestationDays', width: 170 },
    {
      title: '', width: 90,
      render: (_, r) => (
        <Popconfirm title={translate('deleteThisBreed')} onConfirm={() => handleDelete('breed', r.id)}>
          <Button size="small" danger>{translate('delete')}</Button>
        </Popconfirm>
      ),
    },
  ];

  const sexColumns: ColumnsType<SexOption> = [
    { title: translate('value'), dataIndex: 'value' },
    {
      title: '', width: 90,
      render: (_, r) => (
        <Popconfirm title={translate('deleteThisSexOption')} onConfirm={() => handleDelete('sexOption', r.id)}>
          <Button size="small" danger>{translate('delete')}</Button>
        </Popconfirm>
      ),
    },
  ];

  const ageColumns: ColumnsType<AgeCategory> = [
    { title: translate('name'), dataIndex: 'name' },
    {
      title: translate('ageRangeDays'), dataIndex: 'minDays', width: 160,
      render: (_, r) => `${r.minDays}–${r.maxDays >= MAX_DAYS_SENTINEL ? '∞' : r.maxDays}`,
    },
    {
      title: '', width: 90,
      render: (_, r) => (
        <Popconfirm title={translate('deleteThisAgeCategory')} onConfirm={() => handleDelete('ageCategory', r.id)}>
          <Button size="small" danger>{translate('delete')}</Button>
        </Popconfirm>
      ),
    },
  ];

  const statusColumns: ColumnsType<AnimalStatus> = [
    { title: translate('name'), dataIndex: 'name' },
    {
      title: translate('category'), dataIndex: 'category', width: 120,
      render: (c: number) => <Tag color={STATUS_CATEGORY_COLORS[c]}>{STATUS_CATEGORY_LABELS[c] ?? c}</Tag>,
    },
    {
      title: translate('active'), dataIndex: 'isActive', width: 90,
      render: (a: boolean) => (a ? <Tag color="green">{translate('yes')}</Tag> : <Tag>{translate('no')}</Tag>),
    },
    {
      title: '', width: 90,
      render: (_, r) =>
        r.isSystemDefined ? (
          <Tag>{translate('system')}</Tag>
        ) : (
          <Popconfirm title={translate('deleteThisStatus')} onConfirm={() => handleDelete('status', r.id)}>
            <Button size="small" danger>{translate('delete')}</Button>
          </Popconfirm>
        ),
    },
  ];

  const locationTypeColumns: ColumnsType<LocationType> = [
    { title: translate('name'), dataIndex: 'name' },
    {
      title: '', width: 90,
      render: (_, r) => (
        <Popconfirm title={translate('deleteThisLocationType')} onConfirm={() => handleDelete('locationType', r.id)}>
          <Button size="small" danger>{translate('delete')}</Button>
        </Popconfirm>
      ),
    },
  ];

  const locationColumns: ColumnsType<FlatLocation> = [
    {
      title: translate('name'), dataIndex: 'name',
      render: (name: string, r) => (
        <span style={{ paddingInlineStart: r.depth * 20 }}>
          {r.depth > 0 && (
            <span style={{ color: '#999' }}>
              <DirectionalGlyph mark="tree-branch" />{' '}
            </span>
          )}
          {name}
        </span>
      ),
    },
    {
      title: translate('type'), dataIndex: 'locationTypeId', width: 140,
      render: (id: string) => locationTypeMap.get(id) ?? '-',
    },
    {
      title: '', width: 90,
      render: (_, r) => (
        <Popconfirm title={translate('deleteThisLocation')} onConfirm={() => handleDelete('location', r.id)}>
          <Button size="small" danger>{translate('delete')}</Button>
        </Popconfirm>
      ),
    },
  ];

  const tabTable = (type: ModalType, label: string, columns: ColumnsType<never>, dataSource: unknown, rowKey = 'id') => (
    <>
      {addToolbar(type, label)}
      <Table
        rowKey={rowKey}
        size="small"
        columns={columns}
        dataSource={dataSource as never}
        loading={loading}
        pagination={false}
      />
    </>
  );

  const items = [
    {
      key: 'animalTypes',
      label: translate('animalTypes'),
      children: tabTable('animalType', 'Animal Type', typeColumns as ColumnsType<never>, animalTypes),
    },
    {
      key: 'breeds',
      label: translate('breeds'),
      children: tabTable('breed', 'Breed', breedColumns as ColumnsType<never>, breeds),
    },
    {
      key: 'sexOptions',
      label: translate('sexOptions'),
      children: tabTable('sexOption', 'Sex Option', sexColumns as ColumnsType<never>, sexOptions),
    },
    {
      key: 'ageCategories',
      label: translate('ageCategories'),
      children: tabTable('ageCategory', 'Age Category', ageColumns as ColumnsType<never>, ageCategories),
    },
    {
      key: 'statuses',
      label: translate('animalStatuses'),
      children: tabTable('status', 'Status', statusColumns as ColumnsType<never>, statuses),
    },
    {
      key: 'locationTypes',
      label: translate('locationTypes'),
      children: tabTable('locationType', 'Location Type', locationTypeColumns as ColumnsType<never>, locationTypes),
    },
    {
      key: 'locations',
      label: translate('locations'),
      children: tabTable('location', 'Location', locationColumns as ColumnsType<never>, flatLocations),
    },
  ];

  return (
    <Card title={translate('configuration')}>
      <Tabs items={items} />

      <Modal
        title={`New ${typeName[modalType]}`}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => setModalOpen(false)}
        destroyOnClose
        width={480}
      >
        <Form form={form} layout="vertical">
          {modalType === 'animalType' && (
            <Form.Item name="name" label={translate('name')} rules={[{ required: true, message: 'Name is required' }]}>
              <Input maxLength={100} placeholder={translate('eGCattle')} />
            </Form.Item>
          )}

          {modalType === 'breed' && (
            <>
              <Form.Item name="name" label={translate('name')} rules={[{ required: true, message: 'Name is required' }]}>
                <Input maxLength={100} placeholder={translate('eGSahiwal')} />
              </Form.Item>
              <Form.Item name="animalTypeId" label={translate('animalType')} rules={[{ required: true, message: 'Animal type is required' }]}>
                <LookupQuickAddSelect
                  kind="animalType"
                  options={animalTypes.map((t) => ({ value: t.id, label: t.name }))}
                  placeholder={translate('selectAnimalType')}
                  onCreated={() => load()}
                />
              </Form.Item>
              <Form.Item name="averageGestationDays" label={translate('averageGestationDays')}>
                <InputNumber min={1} max={999} style={{ width: '100%' }} />
              </Form.Item>
            </>
          )}

          {modalType === 'sexOption' && (
            <Form.Item name="value" label={translate('value')} rules={[{ required: true, message: 'Value is required' }]}>
              <Input maxLength={50} placeholder={translate('eGMale')} />
            </Form.Item>
          )}

          {modalType === 'ageCategory' && (
            <>
              <Form.Item name="name" label={translate('name')} rules={[{ required: true, message: 'Name is required' }]}>
                <Input maxLength={100} placeholder={translate('eGCalf')} />
              </Form.Item>
              <Row gutter={[16, 16]}>
                <Col xs={24} sm={12}>
                  <Form.Item name="minDays" label={translate('minAgeDays')} rules={[{ required: true }]}>
                    <InputNumber min={0} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
                <Col xs={24} sm={12}>
                  <Form.Item name="maxDays" label={translate('maxAgeDays')} rules={[{ required: true }]}>
                    <InputNumber min={0} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
              </Row>
            </>
          )}

          {modalType === 'status' && (
            <>
              <Form.Item name="name" label={translate('name')} rules={[{ required: true, message: 'Name is required' }]}>
                <Input maxLength={100} placeholder={translate('eGQuarantine')} />
              </Form.Item>
              <Form.Item name="category" label={translate('category')} rules={[{ required: true }]}>
                <Select
                  options={(Object.keys(STATUS_CATEGORY_LABELS) as unknown as number[]).map((k) => ({
                    value: Number(k),
                    label: STATUS_CATEGORY_LABELS[Number(k)],
                  }))}
                  style={{ width: '100%' }}
                />
              </Form.Item>
              <Form.Item name="isActive" label={translate('active')} valuePropName="checked">
                <Switch />
              </Form.Item>
            </>
          )}

          {modalType === 'locationType' && (
            <Form.Item name="name" label={translate('name')} rules={[{ required: true, message: 'Name is required' }]}>
              <Input maxLength={100} placeholder={translate('eGShed')} />
            </Form.Item>
          )}

          {modalType === 'location' && (
            <>
              <Form.Item name="name" label={translate('name')} rules={[{ required: true, message: 'Name is required' }]}>
                <Input maxLength={200} placeholder={translate('eGShedA')} />
              </Form.Item>
              <Form.Item name="locationTypeId" label={translate('locationType')} rules={[{ required: true, message: 'Location type is required' }]}>
                <LookupQuickAddSelect
                  kind="locationType"
                  options={locationTypes.map((lt) => ({ value: lt.id, label: lt.name }))}
                  placeholder={translate('selectLocationType')}
                  onCreated={() => load()}
                />
              </Form.Item>
              <Form.Item name="parentLocationId" label={translate('parentLocationOptional')}>
                <LookupQuickAddSelect
                  kind="location"
                  ctx={{
                    locationTypes: locationTypes.map((lt) => ({ value: lt.id, label: lt.name })),
                    locations: flatLocations.map((l) => ({
                      value: l.id,
                      label: `${'\u00A0\u00A0'.repeat(l.depth)}${l.name}`,
                    })),
                  }}
                  allowClear
                  options={flatLocations.map((l) => ({
                    value: l.id,
                    label: `${'\u00A0\u00A0'.repeat(l.depth)}${l.name}`,
                  }))}
                  placeholder={translate('noneTopLevel')}
                  onCreated={() => load()}
                />
              </Form.Item>
            </>
          )}
        </Form>
      </Modal>
    </Card>
  );
};

export default ConfigurationPage;
