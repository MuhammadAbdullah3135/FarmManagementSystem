import React, { useCallback, useEffect, useState } from 'react';
import {
  Button, Card, DatePicker, Form, Input, InputNumber, Modal, Popconfirm, Row, Col, Select, Space, Statistic, Table, message,
} from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs, { Dayjs } from 'dayjs';
import { salaryPaymentsApi, payrollApi, employeesApi } from '../../api/hr';
import { getApiError } from '../../api/farmApi';
import type { Employee, PayrollReport, SalaryPayment } from '../../types';

const SalaryPaymentsPage: React.FC = () => {
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [selectedEmp, setSelectedEmp] = useState<string>('');
  const [payments, setPayments] = useState<SalaryPayment[]>([]);
  const [paymentTotal, setPaymentTotal] = useState(0);
  const [paymentPage, setPaymentPage] = useState(1);
  const [report, setReport] = useState<PayrollReport | null>(null);
  const [loading, setLoading] = useState(false);
  const [modalOpen, setModalOpen] = useState(false);
  const [payRange, setPayRange] = useState<[Dayjs | null, Dayjs | null]>([
    dayjs().subtract(30, 'day'), dayjs(),
  ]);
  const [form] = Form.useForm();

  const loadEmployees = useCallback(async () => {
    try {
      const res = await employeesApi.list({ page: 1, pageSize: 100 });
      setEmployees(res.data.items);
    } catch (err) {
      message.error(getApiError(err));
    }
  }, []);

  const loadPayments = useCallback(async (empId: string, p: number) => {
    if (!empId) return;
    setLoading(true);
    try {
      const res = await salaryPaymentsApi.list(empId, p, 10);
      setPayments(res.data.items);
      setPaymentTotal(res.data.totalCount);
      setPaymentPage(p);
    } catch (err) {
      message.error(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, []);

  const loadReport = useCallback(async () => {
    try {
      const r = await payrollApi.report(payRange[0]?.toISOString(), payRange[1]?.toISOString());
      setReport(r.data);
    } catch (err) {
      message.error(getApiError(err));
    }
  }, [payRange]);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      loadEmployees();
      loadReport();
    }, 0);
    return () => window.clearTimeout(timer);
  }, [loadEmployees, loadReport]);

  useEffect(() => {
    const timer = window.setTimeout(() => { if (selectedEmp) loadPayments(selectedEmp, 1); }, 0);
    return () => window.clearTimeout(timer);
  }, [selectedEmp, loadPayments]);

  const handleRecord = async () => {
    try {
      const values = await form.validateFields();
      await salaryPaymentsApi.record(selectedEmp, {
        amount: values.amount,
        paymentDate: values.paymentDate?.toISOString(),
        notes: values.notes,
      });
      message.success('Payment recorded');
      setModalOpen(false);
      loadPayments(selectedEmp, paymentPage);
    } catch (err) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      message.error(getApiError(err));
    }
  };

  const handleDeletePayment = async (paymentId: string) => {
    try {
      await salaryPaymentsApi.remove(selectedEmp, paymentId);
      message.success('Payment deleted');
      loadPayments(selectedEmp, paymentPage);
    } catch (err) {
      message.error(getApiError(err));
    }
  };

  const payCols: ColumnsType<SalaryPayment> = [
    { title: 'Date', dataIndex: 'paymentDate', render: (d: string) => dayjs(d).format('YYYY-MM-DD') },
    { title: 'Amount', dataIndex: 'amount', align: 'right', render: (v: number) => `$${v.toLocaleString()}` },
    { title: 'Type', dataIndex: 'salaryTypeName' },
    { title: 'Notes', dataIndex: 'notes', render: (n?: string) => n ?? '-' },
    {
      title: '',
      render: (_, r) => (
        <Popconfirm title="Delete payment?" onConfirm={() => handleDeletePayment(r.id)}>
          <Button size="small" danger>Delete</Button>
        </Popconfirm>
      ),
    },
  ];

  return (
    <div>
      <Row gutter={[16, 16]} style={{ marginBottom: 16 }}>
        <Col xs={24} lg={16}>
          <Card title="Salary Payments" extra={
            <Space>
              <Select
                showSearch
                optionFilterProp="label"
                placeholder="Select employee"
                style={{ width: 220 }}
                value={selectedEmp || undefined}
                onChange={setSelectedEmp}
                options={employees.map((e) => ({
                  value: e.id,
                  label: `${e.firstName} ${e.lastName}`,
                }))}
              />
              {selectedEmp && (
                <Button type="primary" icon={<PlusOutlined />} onClick={() => { form.resetFields(); form.setFieldsValue({ paymentDate: dayjs() }); setModalOpen(true); }}>
                  Record Payment
                </Button>
              )}
            </Space>
          }>
            {selectedEmp ? (
              <Table
                rowKey="id"
                columns={payCols}
                dataSource={payments}
                loading={loading}
                pagination={{ current: paymentPage, total: paymentTotal, pageSize: 10, onChange: (p) => loadPayments(selectedEmp, p) }}
              />
            ) : (
              <p style={{ color: '#999' }}>Select an employee to view payment history.</p>
            )}
          </Card>
        </Col>
        <Col xs={24} lg={8}>
          <Card
            title="Payroll Report"
            extra={
              <Space>
                <DatePicker.RangePicker value={payRange} onChange={(v) => v && setPayRange(v)} />
                <Button size="small" onClick={loadReport}>Refresh</Button>
              </Space>
            }
          >
            {report ? (
              <>
                <Row gutter={16}>
                  <Col span={12}><Statistic title="Total Paid" value={report.totalPaid} prefix="$" precision={2} /></Col>
                  <Col span={12}><Statistic title="Payments" value={report.paymentCount} /></Col>
                </Row>
                <Row gutter={16} style={{ marginTop: 16 }}>
                  <Col span={24}><Statistic title="Expected Monthly Payroll" value={report.expectedMonthlyPayroll} prefix="$" precision={2} /></Col>
                </Row>
                <Table
                  rowKey="employeeId"
                  size="small"
                  style={{ marginTop: 16 }}
                  columns={[
                    { title: 'Employee', dataIndex: 'employeeName' },
                    { title: 'Paid', dataIndex: 'totalPaid', align: 'right', render: (v: number) => `$${v.toLocaleString()}` },
                  ]}
                  dataSource={report.byEmployee}
                  pagination={false}
                />
              </>
            ) : (
              <p style={{ color: '#999' }}>Loading...</p>
            )}
          </Card>
        </Col>
      </Row>

      <Modal title="Record Payment" open={modalOpen} onOk={handleRecord} onCancel={() => setModalOpen(false)} destroyOnClose>
        <Form form={form} layout="vertical">
          <Form.Item name="amount" label="Amount" rules={[{ required: true }]}>
            <InputNumber min={0.01} style={{ width: '100%' }} prefix="$" />
          </Form.Item>
          <Form.Item name="paymentDate" label="Payment Date">
            <DatePicker style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="notes" label="Notes">
            <Input.TextArea rows={2} maxLength={1000} />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
};

export default SalaryPaymentsPage;
