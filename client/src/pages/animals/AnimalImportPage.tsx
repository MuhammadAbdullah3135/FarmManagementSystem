import { useMemo, useState } from 'react';
import { Alert, Button, Card, Input, Select, Space, Steps, Table, Tag, Typography, Upload, message } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { DownloadOutlined, InboxOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { animalImportApi, TEMPLATE_EXAMPLE_ROW, TEMPLATE_HEADERS } from '../../api/animalImport';
import type {
  AnimalImportCommit,
  AnimalImportFieldMap,
  AnimalImportMapping,
  AnimalImportPreview,
  AnimalImportRow,
} from '../../api/animalImport';
import { getApiError } from '../../api/farmApi';
import { exportCsv } from '../../utils/export';

/** Where a field's value comes from, as the mapping table models it. */
type Source = 'ignore' | 'constant' | `col:${number}`;

interface DraftField {
  source: Source;
  constant: string;
}

type Draft = Record<string, DraftField>;

/** The server's mapping into the wizard's draft shape. */
const toDraft = (mapping: AnimalImportMapping | undefined): Draft => {
  const draft: Draft = {};
  Object.entries(mapping?.fields ?? {}).forEach(([key, map]) => {
    if (map?.column !== undefined && map?.column !== null) {
      draft[key] = { source: `col:${map.column}`, constant: '' };
    } else if (map?.constant) {
      draft[key] = { source: 'constant', constant: map.constant };
    } else {
      draft[key] = { source: 'ignore', constant: '' };
    }
  });
  return draft;
};

/**
 * The draft back into a wire mapping.
 *
 * An ignored field is sent as an empty object rather than omitted: the server treats a
 * field that is *present but unmapped* as explicitly ignored, and one that is absent as
 * "fall back to auto-detection". Without that, clearing a wrong guess would be
 * impossible from the UI.
 */
const toMapping = (draft: Draft, dateFormat: string): AnimalImportMapping => {
  const fields: Record<string, AnimalImportFieldMap> = {};
  Object.entries(draft).forEach(([key, value]) => {
    if (value.source === 'ignore') {
      fields[key] = {};
    } else if (value.source === 'constant') {
      fields[key] = { constant: value.constant };
    } else {
      fields[key] = { column: Number(value.source.slice(4)) };
    }
  });
  return { fields, dateFormat: dateFormat.trim() || null };
};

const AnimalImportPage: React.FC = () => {
  const navigate = useNavigate();
  const [step, setStep] = useState(0);
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<AnimalImportPreview | null>(null);
  const [draft, setDraft] = useState<Draft>({});
  const [dateFormat, setDateFormat] = useState('');
  const [checking, setChecking] = useState(false);
  const [importing, setImporting] = useState(false);
  const [result, setResult] = useState<AnimalImportCommit | null>(null);
  const [error, setError] = useState<string | null>(null);

  const fieldLabels = useMemo(() => {
    const labels: Record<string, string> = {};
    preview?.fields.forEach((field) => {
      labels[field.key] = field.label;
    });
    return labels;
  }, [preview]);

  const labelFor = (key: string) => fieldLabels[key] ?? key;

  /** Validates a file. Nothing is written by this call, whichever step it is made from. */
  const checkFile = async (selected: File, mapping?: AnimalImportMapping): Promise<boolean> => {
    setChecking(true);
    setError(null);
    try {
      const response = await animalImportApi.preview(selected, mapping);
      setPreview(response.data);
      if (!mapping) {
        setDraft(toDraft(response.data.mapping));
        setDateFormat(response.data.mapping.dateFormat ?? '');
      }
      return true;
    } catch (err) {
      setError(getApiError(err));
      return false;
    } finally {
      setChecking(false);
    }
  };

  const handleFile = async (selected: File) => {
    const name = selected.name.toLowerCase();
    if (!name.endsWith('.csv') && !name.endsWith('.xlsx')) {
      setError('Choose a .csv or .xlsx file. If your file is a legacy .xls workbook, save it as .xlsx or CSV first.');
      return;
    }

    setFile(selected);
    setResult(null);
    if (await checkFile(selected)) setStep(1);
  };

  const handleCheck = async () => {
    if (!file) return;
    if (await checkFile(file, toMapping(draft, dateFormat))) setStep(2);
  };

  const handleCommit = async () => {
    if (!file) return;

    setImporting(true);
    setError(null);
    try {
      const response = await animalImportApi.commit(file, toMapping(draft, dateFormat));
      setResult(response.data);
      setStep(3);

      if (response.data.importedCount > 0) {
        message.success(`Imported ${response.data.importedCount} animals`);
      } else {
        message.warning('Nothing was imported — see the rows below');
      }
    } catch (err) {
      setError(getApiError(err));
    } finally {
      setImporting(false);
    }
  };

  const restart = () => {
    setStep(0);
    setFile(null);
    setPreview(null);
    setDraft({});
    setDateFormat('');
    setResult(null);
    setError(null);
  };

  const invalidColumns: ColumnsType<AnimalImportRow> = [
    { title: 'Row', dataIndex: 'rowNumber', key: 'rowNumber', width: 80 },
    {
      title: 'Field',
      key: 'field',
      width: 180,
      render: (_, row) => (
        <Space size={4} wrap>
          {[...new Set(row.errors.map((rowError) => labelFor(rowError.field)))].map((label) => (
            <Tag key={label}>{label}</Tag>
          ))}
        </Space>
      ),
    },
    {
      title: 'Problem',
      key: 'problem',
      render: (_, row) => (
        <ul style={{ margin: 0, paddingLeft: 18 }}>
          {row.errors.map((rowError, index) => (
            <li key={index}>{rowError.message}</li>
          ))}
        </ul>
      ),
    },
  ];

  const sampleColumns: ColumnsType<AnimalImportRow> = [
    { title: 'Row', dataIndex: 'rowNumber', key: 'rowNumber', width: 80 },
    ...(preview?.fields ?? [])
      .filter((field) => field.required)
      .map((field) => ({
        title: field.label,
        key: field.key,
        render: (_: unknown, row: AnimalImportRow) => row.values[field.key] || '—',
      })),
  ];

  return (
    <Card
      title="Import animals"
      extra={<Button icon={<DownloadOutlined />} onClick={() => exportCsv('animal-import-template', [...TEMPLATE_HEADERS], [TEMPLATE_EXAMPLE_ROW])}>Download CSV template</Button>}
    >
      <Steps
        current={step}
        items={[
          { title: 'Upload' },
          { title: 'Map columns' },
          { title: 'Review' },
          { title: 'Done' },
        ]}
        style={{ marginBottom: 24 }}
      />

      {error && (
        <Alert type="error" showIcon message={error} closable onClose={() => setError(null)} style={{ marginBottom: 16 }} />
      )}

      {step === 0 && (
        <Space direction="vertical" style={{ width: '100%' }} size="large">
          <Upload.Dragger
            accept=".csv,.xlsx"
            showUploadList={false}
            beforeUpload={(selected) => {
              void handleFile(selected as File);
              return false;
            }}
          >
            <p className="ant-upload-drag-icon"><InboxOutlined /></p>
            <p className="ant-upload-text">Click or drag a CSV or Excel file here</p>
            <p className="ant-upload-hint">
              Nothing is imported until you review the file. Your spreadsheet does not need
              {' '}
              the same column names as this app — the next step maps them.
            </p>
          </Upload.Dragger>

          <Typography.Text type="secondary">
            Not sure what a file should look like? Download the template above: it opens in
            Excel and can be uploaded again unchanged.
          </Typography.Text>
        </Space>
      )}

      {step === 1 && preview && (
        <Space direction="vertical" style={{ width: '100%' }} size="large">
          <Alert
            type="info"
            showIcon
            message={`${file?.name} · ${preview.headers.length} column(s)`}
            description="Check each field is reading the right column. Anything you leave as “Ignore” is left empty on the new animals."
          />

          <Space direction="vertical" style={{ width: '100%' }} size="small">
            {preview.fields.map((field) => {
              const value = draft[field.key] ?? { source: 'ignore' as Source, constant: '' };
              const suggestions = preview.lookups[field.key];

              return (
                <div key={field.key} style={{ display: 'flex', gap: 12, alignItems: 'flex-start' }}>
                  <div style={{ width: 230 }}>
                    <Space size={4}>
                      <Typography.Text strong>{field.label}</Typography.Text>
                      {field.required && <Tag color="red">required</Tag>}
                    </Space>
                    <div style={{ color: '#8c8c8c', fontSize: 12 }}>{field.hint}</div>
                  </div>

                  <Select<Source>
                    aria-label={`source-${field.key}`}
                    style={{ width: 240 }}
                    value={value.source}
                    onChange={(source) =>
                      setDraft((previous) => ({
                        ...previous,
                        [field.key]: { source, constant: previous[field.key]?.constant ?? '' },
                      }))
                    }
                    options={[
                      { value: 'ignore', label: 'Ignore' },
                      ...preview.headers.map((header, index) => ({
                        value: `col:${index}` as Source,
                        label: `Column ${index + 1}: ${header}`,
                      })),
                      { value: 'constant', label: 'Same value for every row' },
                    ]}
                  />

                  {value.source === 'constant' && (
                    <Input
                      aria-label={`constant-${field.key}`}
                      style={{ width: 240 }}
                      placeholder={suggestions?.length ? `e.g. ${suggestions.slice(0, 3).join(', ')}` : 'Value for every row'}
                      value={value.constant}
                      onChange={(event) =>
                        setDraft((previous) => ({
                          ...previous,
                          [field.key]: { source: 'constant', constant: event.target.value },
                        }))
                      }
                    />
                  )}
                </div>
              );
            })}
          </Space>

          <div style={{ display: 'flex', gap: 12, alignItems: 'center' }}>
            <Typography.Text strong>Date format</Typography.Text>
            <Input
              aria-label="date-format"
              style={{ width: 200 }}
              placeholder="yyyy-MM-dd"
              value={dateFormat}
              onChange={(event) => setDateFormat(event.target.value)}
            />
            <Typography.Text type="secondary">
              Only needed if your dates are ambiguous, such as 01/02/2023.
            </Typography.Text>
          </div>

          <Space>
            <Button type="primary" loading={checking} onClick={() => void handleCheck()}>
              Check file
            </Button>
            <Button onClick={() => setStep(0)}>Choose another file</Button>
          </Space>
        </Space>
      )}

      {step === 2 && preview && (
        <Space direction="vertical" style={{ width: '100%' }} size="large">
          <Alert
            type={preview.invalidRowCount > 0 ? 'warning' : 'success'}
            showIcon
            message={`${preview.totalRows} row(s): ${preview.validRowCount} valid, ${preview.invalidRowCount} with problems`}
            description={
              preview.invalidRowCount > 0
                ? 'The import is all-or-nothing, so nothing will be written until every row is valid. Correct these rows in your file and upload it again.'
                : 'Everything checks out. Importing writes all of these animals together.'
            }
          />

          {preview.invalidRows.length > 0 && (
            <Table
              rowKey="rowNumber"
              size="small"
              columns={invalidColumns}
              dataSource={preview.invalidRows}
              pagination={false}
            />
          )}

          {preview.truncated && (
            <Typography.Text type="secondary">
              Only the first {preview.invalidRows.length} problem rows are listed — fix these and re-check.
            </Typography.Text>
          )}

          {preview.sampleValidRows.length > 0 && (
            <>
              <Typography.Text strong>Ready to import (first {preview.sampleValidRows.length})</Typography.Text>
              <Table rowKey="rowNumber" size="small" columns={sampleColumns} dataSource={preview.sampleValidRows} pagination={false} />
            </>
          )}

          <Space>
            <Button
              type="primary"
              loading={importing}
              disabled={preview.invalidRowCount > 0 || preview.validRowCount === 0}
              onClick={() => void handleCommit()}
            >
              Import {preview.validRowCount} animal(s)
            </Button>
            <Button onClick={() => setStep(1)}>Adjust mapping</Button>
          </Space>
        </Space>
      )}

      {step === 3 && result && (
        <Space direction="vertical" style={{ width: '100%' }} size="large">
          <Alert
            type={result.importedCount > 0 ? 'success' : 'error'}
            showIcon
            message={
              result.importedCount > 0
                ? `Imported ${result.importedCount} animal(s)`
                : 'Nothing was imported'
            }
            description={
              result.importedCount > 0
                ? 'Every animal in the file was created with the usual registered history.'
                : 'The file no longer passes validation. The rows below say why — nothing was written.'
            }
          />

          {result.invalidRows.length > 0 && (
            <Table rowKey="rowNumber" size="small" columns={invalidColumns} dataSource={result.invalidRows} pagination={false} />
          )}

          <Space>
            <Button type="primary" onClick={() => navigate('/dashboard/animals')}>View animals</Button>
            <Button onClick={restart}>Import another file</Button>
          </Space>
        </Space>
      )}
    </Card>
  );
};

export default AnimalImportPage;
