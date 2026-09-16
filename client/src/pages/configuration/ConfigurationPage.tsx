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

const ConfigurationPage: React.FC = () => {
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
      const [at, br, so, ac, st, lt, loc] = await Promise.all([
        configurationApi.animalTypes(),
        configurationApi.breeds(),
        configurationApi.sexOptions(),
        configurationApi.ageCategories(),
        configurationApi.statuses(),
        configurationApi.locationTypes(),
        configurationApi.locations(),
      ]);
      setAnimalTypes(at.data);
      setBreeds(br.data);
      setSexOptions(so.data);
      setAgeCategories(ac.data);
      setStatuses(st.data);
      setLocationTypes(lt.data);
      setLocations(loc.data);
    } catch (err) {
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
          message.success('Animal type created');
          break;
        case 'breed':
          await configurationApi.createBreed({
            name: values.name,
            animalTypeId: values.animalTypeId,
            averageGestationDays: values.averageGestationDays ?? 283,
          });
          message.success('Breed created');
          break;
        case 'sexOption':
          await configurationApi.createSexOption({ value: values.value });
          message.success('Sex option created');
          break;
        case 'ageCategory':
          await configurationApi.createAgeCategory({
            name: values.name,
            minDays: values.minDays,
            maxDays: values.maxDays,
          });
          message.success('Age category created');
          break;
        case 'status':
          await configurationApi.createStatus({
            name: values.name,
            isActive: values.isActive,
            category: values.category,
          });
          message.success('Status created');
          break;
        case 'locationType':
          await configurationApi.createLocationType({ name: values.name });
          message.success('Location type created');
          break;
        case 'location':
          await configurationApi.createLocation({
            name: values.name,
            locationTypeId: values.locationTypeId,
            parentLocationId: values.parentLocationId || undefined,
          });
          message.success('Location created');
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
        case 'animalType': await configurationApi.deleteAnimalType(id); message.success('Animal type deleted'); break;
        case 'breed': await configurationApi.deleteBreed(id); message.success('Breed deleted'); break;
        case 'sexOption': await configurationApi.deleteSexOption(id); message.success('Sex option deleted'); break;
        case 'ageCategory': await configurationApi.deleteAgeCategory(id); message.success('Age category deleted'); break;
        case 'status': await configurationApi.deleteStatus(id); message.success('Status deleted'); break;
        case 'locationType': await configurationApi.deleteLocationType(id); message.success('Location type deleted'); break;
        case 'location': await configurationApi.deleteLocation(id); message.success('Location deleted'); break;
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
        Add {label}
      </Button>
    </div>
  );

  const typeColumns: ColumnsType<AnimalType> = [
    { title: 'Name', dataIndex: 'name' },
    { title: 'Breeds', dataIndex: 'breeds', width: 100, render: (b: Breed[]) => <Tag>{b.length}</Tag> },
    {
      title: '', width: 90,
      render: (_, r) => (
        <Popconfirm
          title="Delete this animal type?"
          description="Its breeds will also be removed."
          onConfirm={() => handleDelete('animalType', r.id)}
        >
          <Button size="small" danger>Delete</Button>
        </Popconfirm>
      ),
    },
  ];

  const breedColumns: ColumnsType<Breed> = [
    { title: 'Name', dataIndex: 'name' },
    { title: 'Animal Type', dataIndex: 'animalTypeId', render: (id: string) => breedTypeMap.get(id) ?? '-' },
    { title: 'Avg Gestation (days)', dataIndex: 'averageGestationDays', width: 170 },
    {
      title: '', width: 90,
      render: (_, r) => (
        <Popconfirm title="Delete this breed?" onConfirm={() => handleDelete('breed', r.id)}>
          <Button size="small" danger>Delete</Button>
        </Popconfirm>
      ),
    },
  ];

  const sexColumns: ColumnsType<SexOption> = [
    { title: 'Value', dataIndex: 'value' },
    {
      title: '', width: 90,
      render: (_, r) => (
        <Popconfirm title="Delete this sex option?" onConfirm={() => handleDelete('sexOption', r.id)}>
          <Button size="small" danger>Delete</Button>
        </Popconfirm>
      ),
    },
  ];

  const ageColumns: ColumnsType<AgeCategory> = [
    { title: 'Name', dataIndex: 'name' },
    {
      title: 'Age Range (days)', dataIndex: 'minDays', width: 160,
      render: (_, r) => `${r.minDays}–${r.maxDays >= MAX_DAYS_SENTINEL ? '∞' : r.maxDays}`,
    },
    {
      title: '', width: 90,
      render: (_, r) => (
        <Popconfirm title="Delete this age category?" onConfirm={() => handleDelete('ageCategory', r.id)}>
          <Button size="small" danger>Delete</Button>
        </Popconfirm>
      ),
    },
  ];

  const statusColumns: ColumnsType<AnimalStatus> = [
    { title: 'Name', dataIndex: 'name' },
    {
      title: 'Category', dataIndex: 'category', width: 120,
      render: (c: number) => <Tag color={STATUS_CATEGORY_COLORS[c]}>{STATUS_CATEGORY_LABELS[c] ?? c}</Tag>,
    },
    {
      title: 'Active', dataIndex: 'isActive', width: 90,
      render: (a: boolean) => (a ? <Tag color="green">Yes</Tag> : <Tag>No</Tag>),
    },
    {
      title: '', width: 90,
      render: (_, r) =>
        r.isSystemDefined ? (
          <Tag>System</Tag>
        ) : (
          <Popconfirm title="Delete this status?" onConfirm={() => handleDelete('status', r.id)}>
            <Button size="small" danger>Delete</Button>
          </Popconfirm>
        ),
    },
  ];

  const locationTypeColumns: ColumnsType<LocationType> = [
    { title: 'Name', dataIndex: 'name' },
    {
      title: '', width: 90,
      render: (_, r) => (
        <Popconfirm title="Delete this location type?" onConfirm={() => handleDelete('locationType', r.id)}>
          <Button size="small" danger>Delete</Button>
        </Popconfirm>
      ),
    },
  ];

  const locationColumns: ColumnsType<FlatLocation> = [
    {
      title: 'Name', dataIndex: 'name',
      render: (name: string, r) => (
        <span style={{ paddingLeft: r.depth * 20 }}>
          {r.depth > 0 && <span style={{ color: '#999' }}>↳ </span>}
          {name}
        </span>
      ),
    },
    {
      title: 'Type', dataIndex: 'locationTypeId', width: 140,
      render: (id: string) => locationTypeMap.get(id) ?? '-',
    },
    {
      title: '', width: 90,
      render: (_, r) => (
        <Popconfirm title="Delete this location?" onConfirm={() => handleDelete('location', r.id)}>
          <Button size="small" danger>Delete</Button>
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
      label: 'Animal Types',
      children: tabTable('animalType', 'Animal Type', typeColumns as ColumnsType<never>, animalTypes),
    },
    {
      key: 'breeds',
      label: 'Breeds',
      children: tabTable('breed', 'Breed', breedColumns as ColumnsType<never>, breeds),
    },
    {
      key: 'sexOptions',
      label: 'Sex Options',
      children: tabTable('sexOption', 'Sex Option', sexColumns as ColumnsType<never>, sexOptions),
    },
    {
      key: 'ageCategories',
      label: 'Age Categories',
      children: tabTable('ageCategory', 'Age Category', ageColumns as ColumnsType<never>, ageCategories),
    },
    {
      key: 'statuses',
      label: 'Animal Statuses',
      children: tabTable('status', 'Status', statusColumns as ColumnsType<never>, statuses),
    },
    {
      key: 'locationTypes',
      label: 'Location Types',
      children: tabTable('locationType', 'Location Type', locationTypeColumns as ColumnsType<never>, locationTypes),
    },
    {
      key: 'locations',
      label: 'Locations',
      children: tabTable('location', 'Location', locationColumns as ColumnsType<never>, flatLocations),
    },
  ];

  return (
    <Card title="Configuration">
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
            <Form.Item name="name" label="Name" rules={[{ required: true, message: 'Name is required' }]}>
              <Input maxLength={100} placeholder="e.g. Cattle" />
            </Form.Item>
          )}

          {modalType === 'breed' && (
            <>
              <Form.Item name="name" label="Name" rules={[{ required: true, message: 'Name is required' }]}>
                <Input maxLength={100} placeholder="e.g. Sahiwal" />
              </Form.Item>
              <Form.Item name="animalTypeId" label="Animal Type" rules={[{ required: true, message: 'Animal type is required' }]}>
                <Select
                  options={animalTypes.map((t) => ({ value: t.id, label: t.name }))}
                  placeholder="Select animal type"
                  style={{ width: '100%' }}
                />
              </Form.Item>
              <Form.Item name="averageGestationDays" label="Average Gestation (days)">
                <InputNumber min={1} max={999} style={{ width: '100%' }} />
              </Form.Item>
            </>
          )}

          {modalType === 'sexOption' && (
            <Form.Item name="value" label="Value" rules={[{ required: true, message: 'Value is required' }]}>
              <Input maxLength={50} placeholder="e.g. Male" />
            </Form.Item>
          )}

          {modalType === 'ageCategory' && (
            <>
              <Form.Item name="name" label="Name" rules={[{ required: true, message: 'Name is required' }]}>
                <Input maxLength={100} placeholder="e.g. Calf" />
              </Form.Item>
              <Row gutter={[16, 16]}>
                <Col xs={24} sm={12}>
                  <Form.Item name="minDays" label="Min Age (days)" rules={[{ required: true }]}>
                    <InputNumber min={0} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
                <Col xs={24} sm={12}>
                  <Form.Item name="maxDays" label="Max Age (days)" rules={[{ required: true }]}>
                    <InputNumber min={0} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
              </Row>
            </>
          )}

          {modalType === 'status' && (
            <>
              <Form.Item name="name" label="Name" rules={[{ required: true, message: 'Name is required' }]}>
                <Input maxLength={100} placeholder="e.g. Quarantine" />
              </Form.Item>
              <Form.Item name="category" label="Category" rules={[{ required: true }]}>
                <Select
                  options={(Object.keys(STATUS_CATEGORY_LABELS) as unknown as number[]).map((k) => ({
                    value: Number(k),
                    label: STATUS_CATEGORY_LABELS[Number(k)],
                  }))}
                  style={{ width: '100%' }}
                />
              </Form.Item>
              <Form.Item name="isActive" label="Active" valuePropName="checked">
                <Switch />
              </Form.Item>
            </>
          )}

          {modalType === 'locationType' && (
            <Form.Item name="name" label="Name" rules={[{ required: true, message: 'Name is required' }]}>
              <Input maxLength={100} placeholder="e.g. Shed" />
            </Form.Item>
          )}

          {modalType === 'location' && (
            <>
              <Form.Item name="name" label="Name" rules={[{ required: true, message: 'Name is required' }]}>
                <Input maxLength={200} placeholder="e.g. Shed A" />
              </Form.Item>
              <Form.Item name="locationTypeId" label="Location Type" rules={[{ required: true, message: 'Location type is required' }]}>
                <Select
                  options={locationTypes.map((lt) => ({ value: lt.id, label: lt.name }))}
                  placeholder="Select location type"
                  style={{ width: '100%' }}
                />
              </Form.Item>
              <Form.Item name="parentLocationId" label="Parent Location (optional)">
                <Select
                  allowClear
                  options={flatLocations.map((l) => ({
                    value: l.id,
                    label: `${'\u00A0\u00A0'.repeat(l.depth)}${l.name}`,
                  }))}
                  placeholder="None (top level)"
                  style={{ width: '100%' }}
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
