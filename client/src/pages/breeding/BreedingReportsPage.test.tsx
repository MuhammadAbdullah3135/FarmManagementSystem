import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Children, cloneElement, type ComponentProps, type ReactElement, type ReactNode } from 'react';
import BreedingReportsPage from './BreedingReportsPage';
import { breedingReportsApi } from '../../api/breeding';
import type { BreedingSummaryReport } from '../../types';

// jsdom reports a zero-size container and never advances recharts' animation frames, so the pie
// would draw nothing at all. Give the chart an explicit 400x220 size and skip the entrance animation.
vi.mock('recharts', async (importOriginal) => {
  const actual = await importOriginal<typeof import('recharts')>();
  return {
    ...actual,
    ResponsiveContainer: ({ children }: { children?: ReactNode }) => {
      const child = Children.only(children) as ReactElement<{ width?: number; height?: number }>;
      return cloneElement(child, { width: 400, height: 220 });
    },
    Pie: (props: ComponentProps<typeof actual.Pie>) => <actual.Pie {...props} isAnimationActive={false} />,
  };
});

vi.mock('../../api/breeding', () => ({
  breedingReportsApi: {
    summary: vi.fn(),
    trend: vi.fn(),
    methods: vi.fn(),
    sires: vi.fn(),
    calendar: vi.fn(),
  },
}));

const summaryFixture = (overrides: Partial<BreedingSummaryReport> = {}): BreedingSummaryReport => ({
  totalBreedingRecords: 0,
  pendingCount: 0,
  confirmedCount: 0,
  failedCount: 0,
  confirmationRate: 0,
  activePregnancies: 0,
  totalBirths: 0,
  totalOffspring: 0,
  aliveOffspring: 0,
  averageGestationDays: 0,
  ...overrides,
});

// Centre of the mocked 400x220 chart, matching the cx="50%" cy="50%" on the donut.
const CENTER = { x: 200, y: 110 };

const renderedDonutLabels = () =>
  [...document.querySelectorAll('.recharts-surface text')]
    .map(el => ({
      text: el.textContent ?? '',
      radius: Math.hypot(Number(el.getAttribute('x')) - CENTER.x, Number(el.getAttribute('y')) - CENTER.y),
    }))
    .filter(label => label.text.includes('%'));

beforeEach(() => {
  vi.mocked(breedingReportsApi.trend).mockResolvedValue({ data: [] } as never);
  vi.mocked(breedingReportsApi.methods).mockResolvedValue({ data: [] } as never);
  vi.mocked(breedingReportsApi.sires).mockResolvedValue({ data: [] } as never);
  vi.mocked(breedingReportsApi.calendar).mockResolvedValue({ data: [] } as never);
});

describe('BreedingReportsPage result distribution', () => {
  it('draws one in-ring label and lists every category, including zero counts', async () => {
    vi.mocked(breedingReportsApi.summary).mockResolvedValue({
      data: summaryFixture({ totalBreedingRecords: 12, pendingCount: 12 }),
    } as never);

    render(<BreedingReportsPage />);

    expect(await screen.findByText('Pending 12 (100%)')).toBeInTheDocument();
    expect(screen.getByText('Confirmed 0 (0%)')).toBeInTheDocument();
    expect(screen.getByText('Failed 0 (0%)')).toBeInTheDocument();

    // The reported bug: each zero-value slice drew its own label at a collapsed mid-angle, so
    // "Confirmed 0%" and "Failed 0%" were printed on the same spot outside the ring. Only the
    // single slice with data may be labelled, and the text must sit inside the ring band
    // (innerRadius 60, outerRadius 90) so the narrow card can never clip it.
    const labels = renderedDonutLabels();
    expect(labels.map(l => l.text)).toEqual(['100%']);
    expect(labels[0].radius).toBeGreaterThan(60);
    expect(labels[0].radius).toBeLessThan(90);

    // Nothing may be drawn outside the ring, so no connector lines either.
    expect(document.querySelectorAll('.recharts-pie-label-line')).toHaveLength(0);
  });

  it('reports each category share when the results are mixed', async () => {
    vi.mocked(breedingReportsApi.summary).mockResolvedValue({
      data: summaryFixture({ totalBreedingRecords: 12, confirmedCount: 8, pendingCount: 2, failedCount: 2 }),
    } as never);

    render(<BreedingReportsPage />);

    expect(await screen.findByText('Confirmed 8 (67%)')).toBeInTheDocument();
    expect(screen.getByText('Pending 2 (17%)')).toBeInTheDocument();
    expect(screen.getByText('Failed 2 (17%)')).toBeInTheDocument();
    expect(renderedDonutLabels().map(l => l.text)).toEqual(['67%', '17%', '17%']);
  });

  it('shows no legend at all when there are no breeding results', async () => {
    vi.mocked(breedingReportsApi.summary).mockResolvedValue({ data: summaryFixture() } as never);

    render(<BreedingReportsPage />);

    expect((await screen.findAllByText('No data')).length).toBeGreaterThan(0);
    expect(screen.queryByText('Confirmed 0 (0%)')).not.toBeInTheDocument();
    expect(renderedDonutLabels()).toHaveLength(0);
  });
});
