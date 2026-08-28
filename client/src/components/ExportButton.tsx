import React from 'react';
import { Dropdown, Button } from 'antd';
import { DownloadOutlined, FilePdfOutlined, FileExcelOutlined, FileTextOutlined } from '@ant-design/icons';
import { exportCsv, exportExcel, exportPdf } from '../utils/export';

interface ExportButtonProps {
  filename: string;
  title: string;
  headers: string[];
  rows: (string | number)[][];
  disabled?: boolean;
}

const ExportButton: React.FC<ExportButtonProps> = ({ filename, title: _title, headers, rows, disabled }) => {
  const items = [
    {
      key: 'pdf',
      icon: <FilePdfOutlined />,
      label: 'Export PDF',
      onClick: () => exportPdf(filename, _title, headers, rows),
    },
    {
      key: 'excel',
      icon: <FileExcelOutlined />,
      label: 'Export Excel',
      onClick: () => exportExcel(filename, headers, rows),
    },
    {
      key: 'csv',
      icon: <FileTextOutlined />,
      label: 'Export CSV',
      onClick: () => exportCsv(filename, headers, rows),
    },
  ];

  return (
    <Dropdown menu={{ items }} trigger={['click']} disabled={disabled}>
      <Button icon={<DownloadOutlined />}>
        Export
      </Button>
    </Dropdown>
  );
};

export default ExportButton;
