import { Empty, Space, Typography } from 'antd';
import { Cell, Pie, PieChart, ResponsiveContainer, Tooltip } from 'recharts';
import type { PieLabelRenderProps } from 'recharts';
import { useTranslation } from 'react-i18next';
import { formatNumber } from '../i18n/format';

const { Text } = Typography;

/** Shared palette so a category keeps the same colour on every report page. */
const PIE_COLORS = [
  '#1677ff', '#52c41a', '#faad14', '#ff4d4f', '#722ed1', '#13c2c2', '#eb2f96', '#fa8c16',
];

// Below this share the percentage text is wider than the wedge it labels, so it would spill onto the
// neighbouring slice. The legend still lists the exact value for those categories.
const MIN_LABEL_PERCENT = 8;

export interface BreakdownDatum {
  name: string;
  value: number;
  /** Fixed colour (e.g. status colours). Falls back to the palette by position. */
  color?: string;
}

interface BreakdownPieChartProps {
  data: BreakdownDatum[];
  height?: number;
  innerRadius?: number;
  outerRadius?: number;
  colors?: string[];
  valueFormatter?: (value: number) => string;
  /** Hide when an adjacent table already lists every category with its share. */
  showLegend?: boolean;
  emptyText?: string;
}

/**
 * Pie/donut for a single category breakdown.
 *
 * Every label is drawn inside the ring and the categories are listed in HTML underneath, so a narrow
 * card can never clip a label and two slices can never print their text on the same spot.
 */
export default function BreakdownPieChart({
  data,
  height = 260,
  innerRadius = 0,
  outerRadius = 90,
  colors = PIE_COLORS,
  valueFormatter,
  showLegend = true,
  emptyText,
}: BreakdownPieChartProps) {
  // A chart's empty state is copy like any other: the default comes from the shared
  // `common` namespace so it is translated even when a caller passes no override.
  const { t } = useTranslation('common');
  const emptyLabel = emptyText ?? t('noData');
  // Colour is resolved against the original position, so dropping an empty slice cannot shift the palette.
  const slices = data.map((d, i) => ({ ...d, color: d.color ?? colors[i % colors.length] }));
  const total = slices.reduce((sum, d) => sum + d.value, 0);
  const percentOf = (value: number) => (total === 0 ? 0 : Math.round((value / total) * 100));
  const formatValue = (value: number) => (valueFormatter ? valueFormatter(value) : formatNumber(value));

  if (total <= 0) return <Empty description={emptyLabel} />;

  // Zero-value slices are dropped rather than handed to recharts: it hides their arc but still draws a
  // label at the collapsed mid-angle, which printed two categories' text on top of each other.
  const visible = slices.filter(d => d.value > 0);

  // Flat text at the middle of the ring band. Recharts' own 'inside*' label positions draw along a
  // textPath arc, which smears the text around a wide wedge, and its outside labels are the ones that
  // used to overflow the card, so the point is computed here instead.
  const renderLabel = (props: PieLabelRenderProps) => {
    const percent = percentOf(Number(props.value));
    if (percent < MIN_LABEL_PERCENT) return null;
    const radius = (props.innerRadius + props.outerRadius) / 2;
    const angle = (-(props.midAngle ?? 0) * Math.PI) / 180;
    return (
      <text
        x={props.cx + radius * Math.cos(angle)}
        y={props.cy + radius * Math.sin(angle)}
        fill="#fff"
        fontSize={12}
        textAnchor="middle"
        dominantBaseline="central"
      >
        {`${percent}%`}
      </text>
    );
  };

  return (
    <>
      <ResponsiveContainer width="100%" height={height}>
        <PieChart>
          <Pie
            data={visible}
            cx="50%"
            cy="50%"
            innerRadius={innerRadius}
            outerRadius={outerRadius}
            labelLine={false}
            label={renderLabel}
            dataKey="value"
            nameKey="name"
          >
            {visible.map((d, i) => <Cell key={`${d.name}-${i}`} fill={d.color} />)}
          </Pie>
          <Tooltip formatter={(value) => formatValue(Number(value))} />
        </PieChart>
      </ResponsiveContainer>
      {/* Names and counts are plain DOM, so they cannot be clipped or overlap inside a narrow card. */}
      {showLegend && (
        <Space size="large" wrap style={{ width: '100%', justifyContent: 'center', marginTop: 8 }}>
          {slices.map(d => (
            <Text key={d.name} style={{ fontSize: 12, whiteSpace: 'nowrap' }}>
              <span style={{ display: 'inline-block', width: 8, height: 8, borderRadius: '50%', background: d.color, marginRight: 6 }} />
              {`${d.name} ${formatValue(d.value)} (${percentOf(d.value)}%)`}
            </Text>
          ))}
        </Space>
      )}
    </>
  );
}
