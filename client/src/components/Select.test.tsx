/// <reference types="node" />
import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import Select from './Select';

/*
 * The wrapper exists because antd's virtualised dropdown keeps the `listbox` role on a
 * zero-size mirror holding only the active option and its two neighbours, so a screen
 * reader can never enumerate the list. These two tests are the regression net: one opens a
 * list the size of the app's largest real one (the 100-row animal/inventory/customer
 * lookups) and reads it back through the accessibility tree, and one keeps anything from
 * reaching around the wrapper to antd's own Select.
 */

const options = Array.from({ length: 100 }, (_, index) => ({
  value: `id-${index}`,
  label: `Option ${index}`,
}));

describe('Select', () => {
  it('exposes every option, not just the neighbourhood of the active one', async () => {
    const user = userEvent.setup();
    render(<Select options={options} style={{ width: 240 }} />);

    await user.click(screen.getByRole('combobox'));

    const listbox = await screen.findByRole('listbox');
    const rendered = within(listbox).getAllByRole('option');
    expect(rendered.map((option) => option.textContent)).toEqual(
      options.map((option) => option.label),
    );
  }, 20000);

  it('is the only module that imports antd’s Select', () => {
    // A page that imports antd's Select silently loses the fix, which no rendered test
    // would notice because it would only ever look at the wrapper.
    const src = path.resolve(process.cwd(), 'src');
    const files = (dir: string): string[] =>
      readdirSync(dir, { withFileTypes: true }).flatMap((entry) =>
        entry.isDirectory() ? files(path.join(dir, entry.name)) : [path.join(dir, entry.name)],
      );

    const offenders = files(src)
      .filter((file) => /\.tsx?$/.test(file) && !/\.test\.tsx?$/.test(file))
      .filter((file) =>
        [...readFileSync(file, 'utf8').matchAll(/import\s*(?:type\s*)?\{([^}]*)\}\s*from\s*'antd'/g)]
          .some((match) =>
            match[1]
              .split(',')
              .some((name) => name.trim().split(/\s+as\s+/)[0] === 'Select'),
          ),
      )
      .map((file) => path.relative(src, file).split(path.sep).join('/'));

    expect(offenders).toEqual(['components/Select.tsx']);
  });
});
