import React from 'react';
import { Dropdown, Button } from 'antd';
import { DownloadOutlined, FilePdfOutlined, FileExcelOutlined, FileTextOutlined } from '@ant-design/icons';
import { exportCsv, exportExcel, exportPdf } from '../utils/export';
import { useTranslation } from 'react-i18next';

interface ExportButtonProps {
  filename: string;
  title: string;
  headers: string[];
  rows: (string | number)[][];
  disabled?: boolean;
}

const ExportButton: React.FC<ExportButtonProps> = ({ filename, title: _title, headers, rows, disabled }) => {const { t } = useTranslation('common'); 
  const items = [
    {
      key: 'pdf',
      icon: <FilePdfOutlined />,
      label: t('exportPdf'),
      onClick: () => exportPdf(filename, _title, headers, rows),
    },
    {
      key: 'excel',
      icon: <FileExcelOutlined />,
      label: t('exportExcel'),
      onClick: () => exportExcel(filename, headers, rows),
    },
    {
      key: 'csv',
      icon: <FileTextOutlined />,
      label: t('exportCsv'),
      onClick: () => exportCsv(filename, headers, rows),
    },
  ];

  return (
    <Dropdown menu={{ items }} trigger={['click']} disabled={disabled}>
      <Button icon={<DownloadOutlined />}>
        {t('export')}
      </Button>
    </Dropdown>
  );
};

export default ExportButton;
