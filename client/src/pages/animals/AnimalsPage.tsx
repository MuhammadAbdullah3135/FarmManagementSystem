import { useState, useEffect, useCallback } from 'react';
import { Card, Table, Button, Modal, Form, Input, Select, DatePicker, Space, Tag, message, Popconfirm, Row, Col } from 'antd';
import { configurationApi, type AnimalType } from '../../api/configuration';
import type { ColumnsType } from 'antd/es/table';
import { PlusOutlined, EditOutlined, DeleteOutlined, EyeOutlined } from '@ant-design/icons';
import { animalsApi, type AnimalListFilter, type CreateAnimalPayload } from '../../api/animals';
import { lookupsApi } from '../../api/attendance';
import { getApiError } from '../../api/farmApi';
import dayjs from 'dayjs';
import { useNavigate } from 'react-router-dom';
import type { AnimalListItem } from '../../types';

interface LookupOption {
  id: string;
  name: string;
  value?: string;
}

const STATUS_COLORS: Record<number, string> = { 0: 'green', 1: 'orange', 2: 'red' };

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
  const [allAnimals, setAllAnimals] = useState<{ id: string; tagNumber: string; name?: string }[]>([]);
  const [form] = Form.useForm();
  const [quickTypeName, setQuickTypeName] = useState<string | null>(null);

  const loadLookups = useCallback(async () => {
    try {
      const [atRes, soRes, stRes, locRes, acRes, anRes] = await Promise.all([
        lookupsApi.animalTypes(),
        lookupsApi.sexOptions(),
        lookupsApi.statuses(),
        lookupsApi.locations(),
        lookupsApi.ageCategories(),
        lookupsApi.animals(),
      ]);
      setAnimalTypes(atRes.data as unknown as AnimalType[]);
      setSexOptions(soRes.data.map((s: { id: string; value: string }) => ({ id: s.id, name: s.value })));
      setStatuses(stRes.data.map((s: LookupOption) => ({ id: s.id, name: s.name })));
      setLocations(locRes.data.map((l: LookupOption) => ({ id: l.id, name: l.name })));
      setAgeCategories(acRes.data.map((c: LookupOption) => ({ id: c.id, name: c.name })));
      setAllAnimals(anRes.data.items.map((a: { id: string; tagNumber: string; name?: string }) => ({ id: a.id, tagNumber: a.tagNumber, name: a.name })));
    } catch {}
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

  const handleQuickAddType = async () => {
    const name = quickTypeName?.trim();
    if (!name) {
      message.info('Type a name in the search box first, then click Add type');
      return;
    }
    try {
      const res = await configurationApi.createAnimalType({ name });
      const created = res.data as AnimalType;
      setAnimalTypes((prev) => [...prev, created]);
      message.success(`Animal type "${name}" created`);
      setQuickTypeName(null);
      form.setFieldValue('animalTypeId', created.id);
      void handleBreedsForType(created.id);
    } catch (err) {
      message.error(getApiError(err));
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
                <Select
                  options={animalTypes.map(t => ({ value: t.id, label: t.name }))}
                  onChange={handleBreedsForType}
                  onSearch={(q: string) => setQuickTypeName(q)}
                  showSearch
                  optionFilterProp="label"
                  style={{ width: '100%' }}
                  popupRender={(menu: React.ReactElement) => (
                    <>
                      {menu}
                      <div style={{ padding: '4px 8px', borderTop: '1px solid #f0f0f0' }}>
                        <Button
                          type="link"
                          size="small"
                          icon={<PlusOutlined />}
                          onClick={handleQuickAddType}
                        >
                          Add type
                        </Button>
                      </div>
                    </>
                  )}
                />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="breedId" label="Breed">
                <Select
                  options={breeds.map(b => ({ value: b.id, label: b.name }))}
                  showSearch
                  optionFilterProp="label"
                  allowClear
                  style={{ width: '100%' }}
                />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="sexOptionId" label="Sex" rules={[{ required: true }]}>
                <Select
                  options={sexOptions.map(s => ({ value: s.id, label: s.name }))}
                  showSearch
                  optionFilterProp="label"
                  style={{ width: '100%' }}
                />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="animalStatusId" label="Status" rules={[{ required: true }]}>
                <Select
                  options={statuses.map(s => ({ value: s.id, label: s.name }))}
                  showSearch
                  optionFilterProp="label"
                  style={{ width: '100%' }}
                />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="locationId" label="Location">
                <Select
                  options={locations.map(l => ({ value: l.id, label: l.name }))}
                  showSearch
                  optionFilterProp="label"
                  allowClear
                  style={{ width: '100%' }}
                />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="ageCategoryId" label="Age Category">
                <Select
                  options={ageCategories.map(c => ({ value: c.id, label: c.name }))}
                  showSearch
                  optionFilterProp="label"
                  allowClear
                  style={{ width: '100%' }}
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
