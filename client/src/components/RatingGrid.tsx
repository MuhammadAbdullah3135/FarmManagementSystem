import { Radio, Space, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

export interface RatingCriterion {
  key: string;
  label: string;
}

interface RatingGridProps {
  /**
   * One row per criterion. Omit it for a single overall rating; the row then uses
   * `label` (or the translated word "Rating").
   */
  criteria?: RatingCriterion[];
  label?: string;
  value?: Record<string, number>;
  onChange?: (value: Record<string, number>) => void;
  /** Highest score; a 1..5 scale by default. */
  max?: number;
  disabled?: boolean;
}

/**
 * A grid of 1..N segmented scores, one row per criterion. The value is a plain
 * `{ criterion: score }` object so a page can keep storing the single overall
 * rating it already has, or grow into several criteria without a schema change.
 */
const RatingGrid = ({
  criteria,
  label,
  value = {},
  onChange,
  max = 5,
  disabled = false,
}: RatingGridProps) => {
  const { t } = useTranslation('common');
  const rows = criteria ?? [{ key: 'overall', label: label ?? t('rating') }];
  const scores = Array.from({ length: max }, (_, index) => index + 1);

  const setScore = (key: string, score: number) => {
    const next = { ...value };
    if (score === 0) delete next[key];
    else next[key] = score;
    onChange?.(next);
  };

  return (
    <Space orientation="vertical" size={8} style={{ width: '100%' }}>
      {rows.map((row) => (
        <div key={row.key} style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 8 }}>
          <Typography.Text style={{ minWidth: 140 }}>{row.label}</Typography.Text>
          <Radio.Group
            value={value[row.key]}
            onChange={(event) => setScore(row.key, Number(event.target.value))}
            optionType="button"
            buttonStyle="solid"
            disabled={disabled}
            aria-label={row.label}
          >
            {scores.map((score) => (
              <Radio.Button key={score} value={score}>
                {score}
              </Radio.Button>
            ))}
          </Radio.Group>
        </div>
      ))}
    </Space>
  );
};

export default RatingGrid;
