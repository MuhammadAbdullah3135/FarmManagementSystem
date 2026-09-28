import { useCallback, useEffect, useState } from 'react';
import { Alert, Button, Card, Empty, Input, Pagination, QRCode, Select, Space, Spin, Typography } from 'antd';
import { PrinterOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { animalsApi, type AnimalQrLabel } from '../../api/animals';
import './AnimalQrLabelsPage.css';

const { Text, Title } = Typography;

/** The page sizes the sheet offers. 200 is the server's own ceiling on a page of animals. */
const PAGE_SIZES = [20, 50, 100, 200];

/**
 * A sheet of animal QR labels, ready to print and stick on the animals.
 *
 * <para>
 * The codes come from the server (`GET …/animals/qr-labels`) rather than being generated here
 * from the rows on screen. That is not ceremony: the payload is built from the deployment's own
 * configured frontend origin, so a label printed from this build points at this build. A code
 * assembled in the browser from `window.location` would be right until the app moved to a
 * different path, and the mistake would only surface when somebody scanned a tag months later.
 * </para>
 *
 * <para>
 * It is therefore an online-only screen, and says so when it cannot reach the API instead of
 * drawing a sheet of codes it cannot vouch for. Everything on a label is human-readable without
 * a scanner as well — the tag number is printed beside the code.
 * </para>
 */
export default function AnimalQrLabelsPage() {
  const { t } = useTranslation('animals');

  const [labels, setLabels] = useState<AnimalQrLabel[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const [search, setSearch] = useState('');
  const [loading, setLoading] = useState(false);
  const [failed, setFailed] = useState(false);

  const load = useCallback(async (nextPage: number, nextPageSize: number, nextSearch: string) => {
    setLoading(true);
    try {
      const response = await animalsApi.qrLabels({
        page: nextPage,
        pageSize: nextPageSize,
        search: nextSearch.trim() ? nextSearch.trim() : undefined,
      });
      setLabels(response.data.items ?? []);
      setTotal(response.data.totalCount);
      setFailed(false);
    } catch {
      // No error text from the request: the only thing to say here is that a sheet has to be
      // built by the server, and the queue/offline banner already covers the connectivity story.
      setLabels([]);
      setTotal(0);
      setFailed(true);
    } finally {
      setLoading(false);
    }
  }, []);

  // Deferred like the other pages in this app, so the effect body does not kick off a render
  // of its own while React is committing this one.
  useEffect(() => {
    const timer = window.setTimeout(() => {
      void load(page, pageSize, search);
    }, 0);
    return () => window.clearTimeout(timer);
  }, [load, page, pageSize, search]);

  return (
    <Space direction="vertical" size={16} style={{ width: '100%' }}>
      <div>
        <Title level={4} style={{ marginBottom: 0 }}>{t('qrLabels')}</Title>
        <Text type="secondary">{t('labelSheetIntro')}</Text>
      </div>

      <Space wrap>
        <Input.Search
          allowClear
          placeholder={t('searchByTagOrName')}
          style={{ width: 260 }}
          onSearch={(value) => {
            setPage(1);
            setSearch(value);
          }}
        />
        <Select
          value={pageSize}
          style={{ width: 190 }}
          options={PAGE_SIZES.map((size) => ({ value: size, label: t('labelsPerPage', { size }) }))}
          onChange={(value) => {
            setPage(1);
            setPageSize(value);
          }}
        />
        <Button
          type="primary"
          icon={<PrinterOutlined />}
          disabled={labels.length === 0}
          // Printing the page, not a generated file: the browser's own dialog is where paper
          // size, margins and a physical printer are chosen, and it is the only one that knows
          // what this device can actually reach.
          onClick={() => window.print()}
        >
          {t('print')}
        </Button>
      </Space>

      {failed && (
        <Alert
          type="warning"
          showIcon
          message={t('labelsNeedAConnection')}
          description={t('theSheetIsBuiltByTheServerSoEvery')}
        />
      )}

      <Card>
        {loading ? (
          <Spin style={{ display: 'block', margin: '40px auto' }} />
        ) : labels.length === 0 && !failed ? (
          <Empty description={t('noAnimalsMatchThisFilter')} />
        ) : (
          <div className="fms-label-sheet">
            {labels.map((label) => (
              <div className="fms-label" key={label.animalId}>
                {/*
                  An SVG code rather than a canvas one: canvas is rasterised at one size and
                  prints blurry, and a code that a cheap scanner cannot read is a label that has
                  to be reprinted. SVG stays sharp at whatever the printer resolves.
                */}
                <QRCode value={label.url} type="svg" size={104} bordered={false} errorLevel="M" />
                <div className="fms-label-text">
                  <div className="fms-label-tag">{label.tagNumber}</div>
                  {label.name && <Text>{label.name}</Text>}
                  {label.animalTypeName && (
                    <div>
                      <Text type="secondary">{label.animalTypeName}</Text>
                    </div>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}
      </Card>

      {total > 0 && (
        <Pagination
          current={page}
          pageSize={pageSize}
          total={total}
          showSizeChanger={false}
          onChange={(nextPage) => setPage(nextPage)}
        />
      )}
    </Space>
  );
}
