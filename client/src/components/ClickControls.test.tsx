import { useState } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import EntityPicker from './EntityPicker';
import ChoiceChips from './ChoiceChips';
import ReasonSelect, { type ReasonValue } from './ReasonSelect';
import TemplatePicker from './TemplatePicker';
import RatingGrid from './RatingGrid';
import CollapsedNote from './CollapsedNote';

beforeEach(() => {
  window.localStorage.clear();
});

describe('CollapsedNote', () => {
  it('stays collapsed until asked, then reports the typed note', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    const Harness = () => {
      const [note, setNote] = useState<string>();
      return (
        <CollapsedNote
          value={note}
          onChange={(next) => {
            setNote(next);
            onChange(next);
          }}
        />
      );
    };
    render(<Harness />);

    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: /add note/i }));
    const box = screen.getByRole('textbox', { name: /note \(optional\)/i });
    await user.type(box, 'Lame on the left side');

    expect(onChange).toHaveBeenCalled();
    expect(onChange.mock.calls.at(-1)?.[0]).toBe('Lame on the left side');
  });

  it('shows an existing note without hiding it behind the link', () => {
    render(<CollapsedNote value="Follow up next week" />);

    expect(screen.getByRole('textbox')).toHaveValue('Follow up next week');
  });
});

describe('ChoiceChips', () => {
  it('toggles values in and out of the selected array', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(
      <ChoiceChips
        options={[
          { value: 'Forage', label: 'Forage' },
          { value: 'Mineral', label: 'Mineral' },
        ]}
        value={['Forage']}
        onChange={onChange}
      />,
    );

    await user.click(screen.getByText('Mineral'));
    expect(onChange).toHaveBeenCalledWith(['Forage', 'Mineral']);

    await user.click(screen.getByText('Forage'));
    expect(onChange).toHaveBeenCalledWith([]);
  });

  it('keeps a stored value that is missing from the option list visible', () => {
    render(<ChoiceChips options={[]} value={['Legacy category']} />);

    expect(screen.getByText('Legacy category')).toBeInTheDocument();
  });

  it('adds a custom value when allowed', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<ChoiceChips options={[]} value={[]} allowCustom onChange={onChange} />);

    await user.type(screen.getByRole('textbox'), 'Silage');
    await user.click(screen.getByRole('button', { name: /^add$/i }));

    expect(onChange).toHaveBeenCalledWith(['Silage']);
  });
});

describe('ReasonSelect', () => {
  const Harness = () => {
    const [value, setValue] = useState<ReasonValue>({});
    return (
      <>
        <ReasonSelect
          options={[
            { value: 'sold', label: 'Sold' },
            { value: 'other', label: 'Other' },
          ]}
          value={value}
          onChange={setValue}
        />
        <output data-testid="reason">{JSON.stringify(value)}</output>
      </>
    );
  };

  it('reveals the note only for Other and clears it when a preset is chosen', async () => {
    const user = userEvent.setup();
    render(<Harness />);

    await user.click(screen.getByRole('combobox'));
    await user.click(await screen.findByTitle('Other'));
    const note = await screen.findByRole('textbox', { name: 'Reason' });
    await user.type(note, 'Moved to another farm');

    expect(screen.getByTestId('reason')).toHaveTextContent('Moved to another farm');

    await user.click(screen.getByRole('combobox'));
    await user.click(await screen.findByTitle('Sold'));
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
    expect(screen.getByTestId('reason')).toHaveTextContent('{"code":"sold"}');
  });
});

describe('TemplatePicker', () => {
  it('applies the chosen template and reports Custom as no values', async () => {
    const user = userEvent.setup();
    const onApply = vi.fn();
    const templates = [
      {
        key: 'routine',
        label: 'Routine check',
        description: 'Weight and temperature',
        values: { diagnosis: 'Routine', cost: 0 },
      },
    ];
    render(<TemplatePicker templates={templates} onApply={onApply} />);

    await user.click(screen.getByRole('combobox'));
    await user.click(await screen.findByText('Routine check — Weight and temperature'));
    expect(onApply).toHaveBeenCalledWith(templates[0].values, templates[0]);

    await user.click(screen.getByRole('combobox'));
    await user.click(await screen.findByTitle('Custom'));
    expect(onApply).toHaveBeenCalledWith(undefined);
  });
});

describe('RatingGrid', () => {
  it('reports a score per criterion', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(
      <RatingGrid
        criteria={[
          { key: 'quality', label: 'Quality' },
          { key: 'speed', label: 'Speed' },
        ]}
        value={{ quality: 3 }}
        onChange={onChange}
      />,
    );

    const group = screen.getByRole('radiogroup', { name: 'Speed' });
    await user.click(within(group).getByText('5'));

    expect(onChange).toHaveBeenCalledWith({ quality: 3, speed: 5 });
  });
});

describe('EntityPicker', () => {
  it('loads remote options, selects an id, and remembers it', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    const loadOptions = vi.fn(async () => [
      { value: 'vet-1', label: 'Dr Ahmed' },
      { value: 'vet-2', label: 'Dr Sara' },
    ]);
    render(
      <EntityPicker
        kind={undefined}
        label="Vet"
        loadOptions={loadOptions}
        onChange={onChange}
        recentStorageKey="vets"
      />,
    );

    await user.click(screen.getByRole('combobox'));
    await user.click(await screen.findByTitle('Dr Ahmed'));

    expect(loadOptions).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith('vet-1');
    expect(window.localStorage.getItem('fms.entityPicker.vets')).toBe('["vet-1"]');
  });
});
