import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import VetLookupOrText, { type VetLookupValue } from './VetLookupOrText';
import { vetsApi } from '../api/health';

vi.mock('../api/health', () => ({
  vetsApi: {
    list: vi.fn(),
  },
}));

const res = (data: unknown) => ({ data }) as never;

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(vetsApi.list).mockResolvedValue(res([
    { id: 'vet-1', name: 'Dr. Alpha' },
    { id: 'vet-2', name: 'Dr. Beta' },
  ]));
});

describe('VetLookupOrText', () => {
  it('loads the vet list and writes both the id and the name when one is selected', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    const Wrapper = () => {
      const [value, setValue] = useState<VetLookupValue | undefined>();
      return (
        <VetLookupOrText
          value={value}
          onChange={(next) => {
            setValue(next);
            onChange(next);
          }}
        />
      );
    };

    render(<Wrapper />);

    await waitFor(() => expect(vetsApi.list).toHaveBeenCalledWith(false));

    await user.click(screen.getByRole('combobox'));
    await user.click(await screen.findByText(
      'Dr. Alpha',
      { selector: '.ant-select-item-option-content' },
    ));

    expect(onChange).toHaveBeenLastCalledWith({ vetId: 'vet-1', vetName: 'Dr. Alpha' });
    expect(screen.getByRole('textbox')).toHaveValue('Dr. Alpha');
  });

  it('keeps the free-text fallback and clears the id when the name is typed', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    const Wrapper = () => {
      const [value, setValue] = useState<VetLookupValue | undefined>({
        vetId: 'vet-1',
        vetName: 'Dr. Alpha',
      });
      return (
        <VetLookupOrText
          value={value}
          onChange={(next) => {
            setValue(next);
            onChange(next);
          }}
        />
      );
    };

    render(<Wrapper />);
    const text = screen.getByRole('textbox');
    await user.clear(text);
    await user.type(text, 'Unlisted vet');

    expect(onChange).toHaveBeenLastCalledWith({ vetName: 'Unlisted vet' });
    expect(screen.getByRole('textbox')).toHaveValue('Unlisted vet');
  });
});
