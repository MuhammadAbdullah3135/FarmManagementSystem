import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactElement, ReactNode } from 'react';

/** An anchor element whose props the tree-walk may inspect without casts at every use. */
type AnchorElement = ReactElement<{ children?: ReactNode; onClick?: () => void }>;
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { message } from 'antd';
import BreedingRecordsPage from './BreedingRecordsPage';
import { breedingRecordsApi } from '../../api/breeding';
import { lookupsApi } from '../../api/attendance';
import type { BreedingRecord, PagedResult } from '../../types';

vi.mock('../../api/breeding', () => ({
  breedingRecordsApi: {
    list: vi.fn(),
    get: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    delete: vi.fn(),
  },
}));

vi.mock('../../api/attendance', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/attendance')>();
  return {
    ...actual,
    lookupsApi: {
      ...actual.lookupsApi,
      animals: vi.fn(),
      sexOptions: vi.fn(),
    },
  };
});

// The static `message` API renders through antd's global holder, which jsdom mounts fine but
// which no assertion targets directly; spying keeps the test on what the page decides.
vi.spyOn(message, 'success').mockImplementation(() => ({ then: () => undefined, remove: () => undefined } as never));
const messageError = vi
  .spyOn(message, 'error')
  .mockImplementation(() => ({ then: () => undefined, remove: () => undefined } as never));

const record = (overrides: Partial<BreedingRecord> = {}): BreedingRecord =>
  ({
    id: 'br-1',
    sireId: 's1',
    damId: 'd1',
    sireTagNumber: 'BULL-1',
    damTagNumber: 'COW-1',
    breedingDate: '2026-05-01T00:00:00Z',
    method: 0,
    result: 1,
    ...overrides,
  }) as BreedingRecord;

const pageOf = (items: BreedingRecord[]): { data: PagedResult<BreedingRecord> } =>
  ({ data: { items, totalCount: items.length, page: 1, pageSize: 10, totalPages: 1 } }) as never;

const renderPage = () =>
  render(
    <MemoryRouter initialEntries={['/dashboard/breeding/records']}>
      <Routes>
        <Route path="/dashboard/breeding/records" element={<BreedingRecordsPage />} />
        <Route path="/dashboard/breeding/gestation" element={<div>GESTATION PAGE</div>} />
      </Routes>
    </MemoryRouter>,
  );

/** Every `<a>` React element inside a rendered node, however nested. */
const findAllAnchors = (node: ReactNode): AnchorElement[] => {
  const anchors: AnchorElement[] = [];
  const walk = (current: ReactNode) => {
    if (!current || typeof current !== 'object') return;
    const element = current as AnchorElement;
    if (element.type === 'a') {
      anchors.push(element);
      return;
    }
    const children = element.props?.children;
    if (Array.isArray(children)) children.forEach(walk);
    else walk(children);
  };
  walk(node);
  return anchors;
};

const refusalBody = {
  status: 409,
  title: 'Conflict',
  detail: "Cannot delete breeding record 'BULL-1 and COW-1 on 2026-05-01': still used by 1 gestation record. Delete that gestation record first.",
  blockers: [
    { id: 'g-1', kind: 'gestationRecord', label: '005 · expected 2026-11-30' },
  ],
};

beforeEach(() => {
  vi.mocked(breedingRecordsApi.list).mockResolvedValue(pageOf([record()]) as never);
  vi.mocked(lookupsApi.animals).mockResolvedValue({ data: { items: [], totalCount: 0 } } as never);
  vi.mocked(lookupsApi.sexOptions).mockResolvedValue({ data: [] } as never);
});

afterEach(() => {
  messageError.mockClear();
});

const deleteTheRow = async (user: ReturnType<typeof userEvent.setup>) => {
  await screen.findByText('BULL-1');
  // The row's icon-only buttons render without an accessible name until the Popconfirm opens;
  // pick the delete button inside the row's actions cell, then confirm in its popover —
  // the same shape MembersPage's removal test asserts through.
  const deleteButton = document.querySelector('.ant-table-cell button.ant-btn-dangerous') as HTMLButtonElement;
  expect(deleteButton).not.toBeNull();
  await user.click(deleteButton);
  const confirm = await screen.findAllByRole('button', { name: /ok|yes|delete/i }, { timeout: 5000 });
  await user.click(confirm[confirm.length - 1]);
};

describe('BreedingRecordsPage delete refusal', () => {
  it('shows the sentence with a link to the gestation records when the refusal names its blockers', async () => {
    const user = userEvent.setup();
    vi.mocked(breedingRecordsApi.delete).mockRejectedValue(
      Object.assign(new Error('Request failed with status code 409'), { response: { status: 409, data: refusalBody } }),
    );
    renderPage();

    await deleteTheRow(user);

    // The English sentence the server sent, as the toast's text…
    await waitFor(() =>
      expect(messageError).toHaveBeenCalledWith(
        expect.objectContaining({
          duration: 8,
          content: expect.objectContaining({ props: expect.objectContaining({ children: expect.anything() }) }),
        }),
      ),
    );
    // …and the way out, as a link the user can click. The blockers' presence is what makes the
    // link appear: the same page must not offer navigation for a refusal about something else.
    // The element tree here is span > [text, ' ', a]; the anchor is found by walking it rather
    // than by index, so reordering the text and the link keeps the test honest.
    const call = messageError.mock.calls.at(-1)![0] as { content: ReactElement };
    const anchor = findAllAnchors(call.content);
    expect(anchor).toHaveLength(1);
    expect(anchor[0].props.children).toBe('View the gestation records');
  });

  it('navigates to the gestation page when the link is used', async () => {
    const user = userEvent.setup();
    vi.mocked(breedingRecordsApi.delete).mockRejectedValue(
      Object.assign(new Error('Request failed with status code 409'), { response: { status: 409, data: refusalBody } }),
    );
    renderPage();

    await deleteTheRow(user);

    const call = messageError.mock.calls.at(-1)![0] as { content: ReactElement };
    const anchor = findAllAnchors(call.content);
    anchor[0].props.onClick?.();

    expect(await screen.findByText('GESTATION PAGE')).toBeInTheDocument();
  });

  it('keeps the plain error for a failure that names no blockers', async () => {
    const user = userEvent.setup();
    vi.mocked(breedingRecordsApi.delete).mockRejectedValue(
      Object.assign(new Error('Request failed with status code 404'), { response: { status: 404, data: 'Breeding record not found' } }),
    );
    renderPage();

    await deleteTheRow(user);

    await waitFor(() => expect(messageError).toHaveBeenCalledWith('Breeding record not found'));
    // And no link-shaped toast was opened for it.
    expect(messageError).toHaveBeenCalledTimes(1);
    expect(messageError.mock.calls[0][0]).not.toHaveProperty('content');
  });
});
