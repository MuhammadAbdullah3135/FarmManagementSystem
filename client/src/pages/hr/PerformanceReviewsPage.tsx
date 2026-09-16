import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, Col, DatePicker, Form, Input, InputNumber, Modal, Popconfirm, Row, Select, Space, Table, message,
} from 'antd';
import { PlusOutlined, StarFilled } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';
import { performanceReviewsApi } from '../../api/attendance';
import { employeesApi } from '../../api/hr';
import { getApiError } from '../../api/farmApi';
import type { Employee, PerformanceReview } from '../../types';

const PerformanceReviewsPage: React.FC = () => {
  const [reviews, setReviews] = useState<PerformanceReview[]>([]);
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<PerformanceReview | null>(null);
  const [empFilter, setEmpFilter] = useState<string | undefined>();
  const [form] = Form.useForm();

  const load = useCallback(async (p: number) => {
    setLoading(true);
    try {
      const [revRes, empRes] = await Promise.all([
        performanceReviewsApi.list({ employeeId: empFilter, page: p, pageSize: 10 }),
        employeesApi.list({ page: 1, pageSize: 100 }),
      ]);
      setReviews(revRes.data.items);
      setTotal(revRes.data.totalCount);
      setPage(p);
      setEmployees(empRes.data.items);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, [empFilter]);

  useEffect(() => {
    const timer = window.setTimeout(() => { load(1); }, 0);
    return () => window.clearTimeout(timer);
  }, [load]);

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    form.setFieldsValue({ reviewDate: dayjs() });
    setModalOpen(true);
  };

  const openEdit = (r: PerformanceReview) => {
    setEditing(r);
    form.setFieldsValue({
      employeeId: r.employeeId,
      rating: r.rating,
      reviewDate: dayjs(r.reviewDate),
      periodStart: r.periodStart ? dayjs(r.periodStart) : undefined,
      periodEnd: r.periodEnd ? dayjs(r.periodEnd) : undefined,
      strengths: r.strengths,
      areasForImprovement: r.areasForImprovement,
      comments: r.comments,
    });
    setModalOpen(true);
  };

  const handleSave = async () => {
    try {
      const values = await form.validateFields();
      const data = {
        employeeId: values.employeeId,
        rating: values.rating,
        reviewDate: values.reviewDate.format('YYYY-MM-DD'),
        periodStart: values.periodStart?.format('YYYY-MM-DD'),
        periodEnd: values.periodEnd?.format('YYYY-MM-DD'),
        strengths: values.strengths,
        areasForImprovement: values.areasForImprovement,
        comments: values.comments,
      };
      if (editing) {
        await performanceReviewsApi.update(editing.id, data);
        message.success('Review updated');
      } else {
        await performanceReviewsApi.create(data);
        message.success('Review created');
      }
      setModalOpen(false);
      load(page);
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await performanceReviewsApi.remove(id);
      message.success('Review deleted');
      load(page);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const columns: ColumnsType<PerformanceReview> = [
    { title: 'Employee', dataIndex: 'employeeName' },
    {
      title: 'Rating',
      dataIndex: 'rating',
      render: (r: number) => (
        <Space>
          {Array.from({ length: 5 }, (_, i) => (
            <StarFilled key={i} style={{ color: i < r ? '#faad14' : '#d9d9d9' }} />
          ))}
        </Space>
      ),
    },
    { title: 'Date', dataIndex: 'reviewDate', render: (d: string) => dayjs(d).format('YYYY-MM-DD') },
    { title: 'Period', render: (_, r) => r.periodStart && r.periodEnd ? `${dayjs(r.periodStart).format('MMM D')} – ${dayjs(r.periodEnd).format('MMM D, YYYY')}` : '-' },
    { title: 'Strengths', dataIndex: 'strengths', ellipsis: true },
    { title: 'Comments', dataIndex: 'comments', ellipsis: true },
    {
      title: 'Actions',
      render: (_, r) => (
        <Space>
          <Button size="small" onClick={() => openEdit(r)}>Edit</Button>
          <Popconfirm title="Delete this review?" onConfirm={() => handleDelete(r.id)}>
            <Button size="small" danger>Delete</Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <Card
      title="Performance Reviews"
      extra={
        <Space>
          <Select
            allowClear
            placeholder="Filter by employee"
            style={{ width: 200 }}
            value={empFilter}
            onChange={setEmpFilter}
            options={employees.map((e) => ({ value: e.id, label: `${e.firstName} ${e.lastName}` }))}
          />
          <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
            New Review
          </Button>
        </Space>
      }
    >
      <Table
        rowKey="id"
        columns={columns}
        dataSource={reviews}
        loading={loading}
        pagination={{ current: page, total, pageSize: 10, onChange: load }}
      />

      <Modal
        title={editing ? 'Edit Review' : 'New Review'}
        open={modalOpen}
        onOk={handleSave}
        onCancel={() => setModalOpen(false)}
        destroyOnClose
        width={640}
      >
        <Form form={form} layout="vertical">
          <Form.Item name="employeeId" label="Employee" rules={[{ required: true }]}>
            <Select
              showSearch
              optionFilterProp="label"
              options={employees.map((e) => ({ value: e.id, label: `${e.firstName} ${e.lastName}` }))}
              style={{ width: '100%' }}
              popupMatchSelectWidth={false}
            />
          </Form.Item>
          <Form.Item name="rating" label="Rating (1–5)" rules={[{ required: true }]}>
            <InputNumber min={1} max={5} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="reviewDate" label="Review Date" rules={[{ required: true }]}>
            <DatePicker style={{ width: '100%' }} />
          </Form.Item>
          <Row gutter={[16, 16]}>
            <Col xs={24} sm={12}>
              <Form.Item name="periodStart" label="Period Start">
                <DatePicker style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="periodEnd" label="Period End">
                <DatePicker style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="strengths" label="Strengths">
            <Input.TextArea rows={2} maxLength={2000} />
          </Form.Item>
          <Form.Item name="areasForImprovement" label="Areas for Improvement">
            <Input.TextArea rows={2} maxLength={2000} />
          </Form.Item>
          <Form.Item name="comments" label="Comments">
            <Input.TextArea rows={2} maxLength={2000} />
          </Form.Item>
        </Form>
      </Modal>
    </Card>
  );
};

export default PerformanceReviewsPage;
