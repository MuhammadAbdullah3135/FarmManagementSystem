import React, { useCallback, useEffect, useRef, useState } from 'react';
import {
  Alert, Button, Card, Descriptions, Space, Table, Tag, Typography, message,
} from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { CloudDownloadOutlined, DownloadOutlined, FileZipOutlined, SyncOutlined } from '@ant-design/icons';
import type { TFunction } from 'i18next';
import { useNavigate } from 'react-router-dom';
import { farmExportApi, fileNameFrom } from '../../api/farmExport';
import type { FarmExport, FarmExportFile } from '../../api/farmExport';
import { getApiError } from '../../api/farmApi';
import { formatDateTime, formatInteger } from '../../i18n/format';
import { downloadBlob } from '../../utils/export';
import { useTranslation } from 'react-i18next';

const { Title, Paragraph, Text } = Typography;

/** How often to look for a finished build while one is queued or running. */
const PollIntervalMs = 2000;

/**
 * The build state as a tag. `status` stays the server's value for the comparison; the word
 * on the tag is translated. Takes `t` rather than reading it from a closure so the tag
 * follows a language change like everything else on the page.
 */
const statusTag = (status: string, t: TFunction) => {
  switch (status) {
    case 'Queued':
      return <Tag icon={<SyncOutlined spin />} color="blue">{t('statusQueued')}</Tag>;
    case 'Running':
      return <Tag icon={<SyncOutlined spin />} color="processing">{t('statusBuilding')}</Tag>;
    case 'Completed':
      return <Tag color="success">{t('statusBuilt')}</Tag>;
    case 'Failed':
      return <Tag color="error">{t('statusFailed')}</Tag>;
    default:
      return <Tag>{t('statusNotStarted')}</Tag>;
  }
};

const formatSize = (bytes?: number | null): string => {
  if (!bytes) return '—';
  if (bytes >= 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  if (bytes >= 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${bytes} bytes`;
};

/**
 * The whole farm's data, in one request.
 *
 * The archive is assembled by a background job — deliberately: a farm with years of
 * history would otherwise hold a request open until the deployment's own timeout cut
 * it off mid-download. So this page asks, then watches, then offers the file: the
 * build's state is the server's record, and the manifest it renders comes from the
 * archive itself, so the page cannot describe the export differently from how the
 * export describes itself.
 */
const DataExportPage: React.FC = () => {const { t } = useTranslation('configuration'); 
  const navigate = useNavigate();

  const [exportState, setExportState] = useState<FarmExport | null>(null);
  const [loading, setLoading] = useState(false);
  const [requesting, setRequesting] = useState(false);
  const [downloading, setDownloading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const pollRef = useRef<number | null>(null);

  const stopPolling = useCallback(() => {
    if (pollRef.current !== null) {
      window.clearInterval(pollRef.current);
      pollRef.current = null;
    }
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await farmExportApi.status();
      // The server answers null for a farm that has never exported, which is an
      // ordinary state rather than an error.
      setExportState(response.data ?? null);
    } catch (err) {
      setError(getApiError(err));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const inFlight = exportState?.status === 'Queued' || exportState?.status === 'Running';

  // Watch a build in progress. Attached only while one is in flight, and always torn
  // down on unmount, so a page left open never polls a finished job forever.
  useEffect(() => {
    if (!inFlight) {
      stopPolling();
      return undefined;
    }

    pollRef.current = window.setInterval(() => {
      void load();
    }, PollIntervalMs);

    return stopPolling;
  }, [inFlight, load, stopPolling]);

  const handleRequest = async () => {
    setRequesting(true);
    setError(null);
    try {
      const response = await farmExportApi.request();
      setExportState(response.data);
      message.success(t('exportQueuedYouWillBeNotifiedWhenIt'));
    } catch (err) {
      setError(getApiError(err));
    } finally {
      setRequesting(false);
    }
  };

  const handleDownload = async () => {
    setDownloading(true);
    setError(null);
    try {
      const response = await farmExportApi.download();
      const name = fileNameFrom(response.headers as Record<string, unknown>);
      downloadBlob(response.data, name);
    } catch (err) {
      setError(getApiError(err, 'The archive could not be downloaded'));
    } finally {
      setDownloading(false);
    }
  };

  const manifest = exportState?.manifest;
  const fileColumns: ColumnsType<FarmExportFile> = [
    {
      title: t('file'),
      dataIndex: 'fileName',
      key: 'fileName',
      render: (name: string, file) => (
        <Space size={4}>
          <Text code>{name}</Text>
          {file.reimportable && <Tag color="blue">{t('reImportable')}</Tag>}
        </Space>
      ),
    },
    { title: t('contains'), dataIndex: 'entity', key: 'entity' },
    {
      title: t('rows'),
      dataIndex: 'rowCount',
      key: 'rowCount',
      align: 'right',
      width: 120,
    },
    {
      title: t('columns'),
      key: 'columns',
      width: 100,
      render: (_, file) => file.columns.length,
    },
  ];

  return (
    <div>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }} align="start">
        <div>
          <Title level={3} style={{ marginBottom: 0 }}>{t('dataExport')}</Title>
          <Paragraph type="secondary" style={{ marginBottom: 0 }}>
            {t('aSingleArchiveOfEveryRecordThisFarm')}
          </Paragraph>
        </div>
        <Space>
          <Button icon={<DownloadOutlined />} onClick={() => navigate('/dashboard/configuration')}>
            {t('back')}
          </Button>
          <Button
            type="primary"
            icon={<FileZipOutlined />}
            loading={requesting || inFlight}
            onClick={() => void handleRequest()}
          >
            {exportState ? t('exportAgain') : t('createExport')}
          </Button>
        </Space>
      </Space>

      {error && (
        <Alert
          type="error"
          showIcon
          closable
          message={error}
          style={{ marginBottom: 16 }}
          onClose={() => setError(null)}
        />
      )}

      <Card
        title={t('latestExport')}
        extra={
          <Space>
            {exportState && statusTag(exportState.status, t)}
            <Button
              type="primary"
              icon={<CloudDownloadOutlined />}
              disabled={!exportState?.isReady}
              loading={downloading}
              onClick={() => void handleDownload()}
            >
              {t('download')}
            </Button>
          </Space>
        }
        loading={loading && !exportState}
        style={{ marginBottom: 16 }}
      >
        {!exportState && (
          <Alert
            type="info"
            showIcon
            message={t('noExportHasBeenCreatedYet')}
            description={t('createOneAndThisPageWillOfferThe')}
          />
        )}

        {exportState && (
          <Descriptions size="small" column={{ xs: 1, sm: 2, lg: 4 }}>
            <Descriptions.Item label={t('state')}>
              {statusTag(exportState.status, t)}
            </Descriptions.Item>
            <Descriptions.Item label={t('archiveGenerated')}>
              {manifest?.generatedAtUtc
                ? formatDateTime(manifest.generatedAtUtc)
                : '—'}
            </Descriptions.Item>
            <Descriptions.Item label={t('size')}>
              {formatSize(exportState.sizeBytes)}
            </Descriptions.Item>
            <Descriptions.Item label={t('rows')}>
              {manifest ? formatInteger(manifest.totalRowCount) : '—'}
            </Descriptions.Item>
          </Descriptions>
        )}

        {exportState?.status === 'Failed' && (
          <Alert
            type="warning"
            showIcon
            style={{ marginTop: 12 }}
            message={t('thisBuildFailed')}
            description={
              <>
                {exportState.error}
                {exportState.isReady && (
                  <>
                    {' '}
                    {t('thePreviouslyBuiltArchiveIsStillAvailableTo')}
                  </>
                )}
              </>
            }
          />
        )}
      </Card>

      {manifest && (
        <>
          <Card
            title={t('whatIsInside')}
            extra={<Text type="secondary">{manifest.files.length} {t('files')}</Text>}
            style={{ marginBottom: 16 }}
          >
            <Table<FarmExportFile>
              rowKey="fileName"
              size="small"
              columns={fileColumns}
              dataSource={manifest.files}
              pagination={manifest.files.length > 25 ? { pageSize: 25 } : false}
            />
          </Card>

          <Card title={t('notIncluded')} style={{ marginBottom: 16 }}>
            <ul style={{ margin: 0, paddingLeft: 18 }}>
              {manifest.excludedEntities.map((exclusion) => (
                <li key={exclusion.entity}>
                  <Text strong>{exclusion.entity}</Text> — {exclusion.reason}
                </li>
              ))}
            </ul>
          </Card>

          <Card title={t('pleaseNote')}>
            <ul style={{ margin: 0, paddingLeft: 18 }}>
              {manifest.notes.map((note) => (
                <li key={note}>{note}</li>
              ))}
            </ul>
          </Card>
        </>
      )}
    </div>
  );
};

export default DataExportPage;
