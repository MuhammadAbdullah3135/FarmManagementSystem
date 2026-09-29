import { useEffect, useMemo, useRef, useState } from 'react';
import { Button, Card, Empty, InputNumber, Select, Space, Spin, Tag, Typography, message } from 'antd';
import { SearchOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { lookupsApi } from '../../api/attendance';
import { lineageApi } from '../../api/breeding';
import { getApiError } from '../../api/farmApi';
import { formatDate } from '../../i18n/format';
import type { LineageNode, LineageResponse } from '../../types';
import {
  DEFAULT_LINEAGE_METRICS,
  buildLineageLayout,
  metricsOf,
  sameMetrics,
  type LineageSide,
} from './lineageLayout';

const { Text } = Typography;

const SEX_TAG_COLORS: Record<string, string> = { Male: 'blue', Female: 'pink' };

/**
 * One animal in the chart.
 *
 * Its box is fixed by the stylesheet (`--fms-lineage-card-w/h`), because the connectors are
 * positioned against that box: a card that grew with its content would leave the stem above it
 * hanging over its own text. Everything that varies — a missing name, a missing date of birth,
 * a breed that wraps — is therefore absorbed inside the fixed box.
 *
 * The record's `sex` is printed as it was stored. The farm's data contains one impossible
 * parent (a female recorded as a sire) and this view shows it as recorded; refusing to draw it
 * would hide the data error rather than surface it.
 */
function LineageCard({ node, side }: { node: LineageNode; side: LineageSide }) {
  const { t } = useTranslation('breeding');

  return (
    <Card
      size="small"
      className="fms-lineage-card"
      data-side={side}
      data-root={node.isRoot ? 'true' : undefined}
      styles={{
        body: {
          padding: 8,
          height: '100%',
          display: 'flex',
          flexDirection: 'column',
          justifyContent: 'center',
          gap: 4,
        },
      }}
    >
      <div className="fms-lineage-card-title">
        <strong>{node.tagNumber}</strong>
        {node.isRoot && <Tag color="gold">{t('root')}</Tag>}
      </div>
      {node.name && <span className="fms-lineage-card-name">{node.name}</span>}
      <div className="fms-lineage-card-meta">
        <Tag color={SEX_TAG_COLORS[node.sex] ?? 'default'}>{node.sex}</Tag>
        {node.breed && <Tag>{node.breed}</Tag>}
        <Tag>{node.status}</Tag>
      </div>
      {node.dateOfBirth && (
        <span className="fms-lineage-card-dob">
          {t('dob')} {formatDate(node.dateOfBirth)}
        </span>
      )}
    </Card>
  );
}

export default function LineagePage() {
  const { t } = useTranslation('breeding');
  const [animals, setAnimals] = useState<{ id: string; tagNumber: string; name?: string }[]>([]);
  const [selectedAnimalId, setSelectedAnimalId] = useState<string | null>(null);
  const [lineage, setLineage] = useState<LineageResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [ancestorDepth, setAncestorDepth] = useState(5);
  const [descendantDepth, setDescendantDepth] = useState(3);
  const [metrics, setMetrics] = useState(DEFAULT_LINEAGE_METRICS);
  const canvasRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    lookupsApi
      .animals()
      .then((res) => setAnimals(res.data.items))
      .catch(() => {});
  }, []);

  /*
   * The card and gap sizes live in the stylesheet, where the phone breakpoint and the rest of
   * the responsive rules already are; a media query is not visible to JavaScript, so the chart
   * asks the canvas what it resolved to and re-lays out when the answer changes. Without this
   * the phone would draw desktop-sized cards, which is most of why the old chart was unusable
   * there.
   */
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const measure = () => {
      const next = metricsOf(getComputedStyle(canvas));
      setMetrics((current) => (sameMetrics(current, next) ? current : next));
    };

    measure();
    window.addEventListener('resize', measure);
    return () => window.removeEventListener('resize', measure);
  }, [lineage]);

  const layout = useMemo(
    () =>
      lineage
        ? buildLineageLayout(lineage.root, lineage.ancestorDepth, lineage.descendantDepth, metrics)
        : null,
    [lineage, metrics],
  );

  /*
   * A pedigree is wider than it is tall, so the box opens somewhere in the middle of it. Open
   * it on the animal that was asked about instead of on whichever card happens to sit at the
   * inline-start edge — which, for a five-generation chart, is an animal several generations
   * away from the one the reader selected.
   */
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas || !layout) return;

    const overflow = canvas.scrollWidth - canvas.clientWidth;
    if (overflow <= 0) return;

    const offset = Math.min(Math.max(layout.rootX - canvas.clientWidth / 2, 0), overflow);
    // RTL counts `scrollLeft` backwards from the inline-start edge, the same edge the offset
    // is measured from.
    canvas.scrollLeft = getComputedStyle(canvas).direction === 'rtl' ? -offset : offset;
  }, [layout]);

  const handleSearch = async () => {
    if (!selectedAnimalId) {
      message.warning(t('selectAnAnimalFirst'));
      return;
    }
    setLoading(true);
    try {
      const res = await lineageApi.get(selectedAnimalId, ancestorDepth, descendantDepth);
      setLineage(res.data);
    } catch (err) {
      message.error(getApiError(err));
      setLineage(null);
    } finally {
      setLoading(false);
    }
  };

  return (
    <Card title={t('parentageLineageView')}>
      <Space className="fms-lineage-controls" style={{ marginBottom: 16 }} wrap>
        <Select
          showSearch
          placeholder={t('selectAnimalToViewLineage')}
          optionFilterProp="label"
          style={{ width: 350 }}
          value={selectedAnimalId}
          onChange={setSelectedAnimalId}
          options={animals.map((animal) => ({
            value: animal.id,
            // Same label shape as the birth-recording form, including its untranslated
            // fallback name: that string is shared, so changing it here alone would make the
            // two pickers disagree.
            label: `${animal.tagNumber} - ${animal.name || 'Unnamed'}`,
          }))}
        />
        <Space>
          <span>{t('ancestors')}</span>
          <InputNumber
            min={1}
            max={10}
            value={ancestorDepth}
            onChange={(value) => setAncestorDepth(value || 5)}
          />
        </Space>
        <Space>
          <span>{t('descendants')}</span>
          <InputNumber
            min={1}
            max={10}
            value={descendantDepth}
            onChange={(value) => setDescendantDepth(value || 3)}
          />
        </Space>
        <Button type="primary" icon={<SearchOutlined />} onClick={handleSearch} loading={loading}>
          {t('viewLineage')}
        </Button>
      </Space>

      {/* A bare spinner, like every other page in the app: the `tip` that used to sit here
          was a hardcoded English string on a prop antd v6 deprecates (`description` replaced
          it), and nothing else on the screen labels its own loading state. */}
      {loading && (
        <div className="fms-lineage-status">
          <Spin size="large" />
        </div>
      )}

      {!loading && !lineage && <Empty description={t('selectAnAnimalAndClickViewLineageTo')} />}

      {!loading && lineage && layout && (
        <>
          {layout.ancestorRows === 0 && (
            <Text type="secondary" className="fms-lineage-note">
              {t('noAncestorsRecorded')}
            </Text>
          )}

          {/* The tree is the only part that scrolls sideways; the notes above and below it stay
              legible without panning, because a reader who has to scroll to find out why the
              chart is short has been told nothing. */}
          <div className="fms-lineage-canvas" ref={canvasRef}>
            <div className="fms-lineage-chart" style={{ width: layout.width, height: layout.height }}>
              {layout.links.map((link) => (
                <div
                  key={link.key}
                  className="fms-lineage-link"
                  data-side={link.side}
                  style={{
                    insetInlineStart: link.x,
                    top: link.y,
                    inlineSize: link.width,
                    blockSize: link.height,
                  }}
                />
              ))}
              {layout.cards.map((card) => (
                <div
                  key={card.key}
                  className="fms-lineage-card-slot"
                  style={{ insetInlineStart: card.x, top: card.y }}
                >
                  <LineageCard node={card.node} side={card.side} />
                </div>
              ))}
            </div>
          </div>

          {layout.descendantRows === 0 && (
            <Text type="secondary" className="fms-lineage-note">
              {t('noOffspringRecorded')}
            </Text>
          )}
        </>
      )}
    </Card>
  );
}
