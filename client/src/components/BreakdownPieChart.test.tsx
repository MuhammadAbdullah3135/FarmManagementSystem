import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Children, cloneElement, type ComponentProps, type ReactElement, type ReactNode } from 'react';
import BreakdownPieChart from './BreakdownPieChart';

// jsdom reports a zero-size container and never advances recharts' animation frames, so the pie would
// draw nothing at all. Give the chart an explicit 400x220 size and skip the entrance animation.
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

// Centre of the mocked 400x220 chart, matching the cx="50%" cy="50%" on the pie.
const CENTER = { x: 200, y: 110 };

const renderedLabels = (outerRadius = 90) =>
  [...document.querySelectorAll('.recharts-surface text')]
    .map(el => ({
      text: el.textContent ?? '',
      radius: Math.hypot(Number(el.getAttribute('x')) - CENTER.x, Number(el.getAttribute('y')) - CENTER.y),
    }))
    .filter(label => {
      const percent = Number(label.text.replace('%', ''));
      // Labels recharts draws outside the ring are pushed past outerRadius; report those as "clipped".
      return label.text.includes('%') && Number.isFinite(percent);
    })
    .map(label => ({ ...label, clipped: label.radius > outerRadius || label.radius < 0 }));

describe('BreakdownPieChart', () => {
  it('labels a single category inside the ring and lists it below', () => {
    render(<BreakdownPieChart data={[{ name: 'FMD Vaccine', value: 2 }]} innerRadius={50} outerRadius={90} />);

    expect(screen.getByText('FMD Vaccine 2 (100%)')).toBeInTheDocument();

    const labels = renderedLabels(90);
    expect(labels.map(l => l.text)).toEqual(['100%']);
    expect(labels.some(l => l.clipped)).toBe(false);
    expect(labels[0].radius).toBeGreaterThan(50);
    expect(labels[0].radius).toBeLessThan(90);
    // Outside labels are what used to be parked beyond the card edge; no connectors may be drawn.
    expect(document.querySelectorAll('.recharts-pie-label-line')).toHaveLength(0);
  });

  it('keeps the legend complete and skips labels that would not fit their wedge', () => {
    render(
      <BreakdownPieChart
        data={[
          { name: 'Cattle', value: 90 },
          { name: 'Goats', value: 5 },
          { name: 'Sheep', value: 5 },
        ]}
      />,
    );

    // Only the wide wedge is labelled; the thin ones are readable in the legend instead.
    expect(renderedLabels().map(l => l.text)).toEqual(['90%']);
    expect(screen.getByText('Cattle 90 (90%)')).toBeInTheDocument();
    expect(screen.getByText('Goats 5 (5%)')).toBeInTheDocument();
    expect(screen.getByText('Sheep 5 (5%)')).toBeInTheDocument();
  });

  it('drops a zero-value slice from the ring without losing it from the legend', () => {
    render(
      <BreakdownPieChart
        data={[
          { name: 'Confirmed', value: 8, color: '#52c41a' },
          { name: 'Failed', value: 0, color: '#ff4d4f' },
        ]}
        innerRadius={60}
        outerRadius={90}
      />,
    );

    expect(screen.getByText('Confirmed 8 (100%)')).toBeInTheDocument();
    expect(screen.getByText('Failed 0 (0%)')).toBeInTheDocument();
    expect(renderedLabels().map(l => l.text)).toEqual(['100%']);
    expect(document.querySelectorAll('.recharts-pie-sector')).toHaveLength(1);
  });

  it('renders the empty state when every value is zero, and can hide the legend', () => {
    const { rerender } = render(<BreakdownPieChart data={[{ name: 'Confirmed', value: 0 }]} />);
    expect(screen.getAllByText('No data').length).toBeGreaterThan(0);
    expect(document.querySelectorAll('.recharts-surface')).toHaveLength(0);
    expect(screen.queryByText('Confirmed 0 (0%)')).not.toBeInTheDocument();

    rerender(<BreakdownPieChart data={[{ name: 'Cattle', value: 4 }]} showLegend={false} emptyText="Nothing here" />);
    expect(document.querySelectorAll('.recharts-surface').length).toBeGreaterThan(0);
    expect(screen.queryByText('Cattle 4 (100%)')).not.toBeInTheDocument();
  });
});
