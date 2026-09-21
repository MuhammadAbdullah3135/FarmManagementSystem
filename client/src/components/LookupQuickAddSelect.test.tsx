import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, within, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import LookupQuickAddSelect from './LookupQuickAddSelect';
import { expenseCategoriesApi } from '../api/finance';

vi.mock('../api/finance', () => ({
  expenseCategoriesApi: { create: vi.fn() },
  incomeCategoriesApi: { create: vi.fn() },
  paymentMethodsApi: { create: vi.fn() },
}));

const res = (data: unknown) => ({ data }) as never;
type User = ReturnType<typeof userEvent.setup>;

const createDeferred = <T,>() => {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((r) => {
    resolve = r;
  });
  return { promise, resolve };
};

/** The Add button lives in the select's dropdown portal, so open it first. */
const openFooter = async (user: User) => {
  await user.click(screen.getByRole('combobox'));
};

const addModal = () =>
  screen.getByText(/^Add /, { selector: '.ant-modal-title' }).closest('.ant-modal') as HTMLElement;

beforeEach(() => {
  vi.clearAllMocks();
});

describe('LookupQuickAddSelect (registry-backed)', () => {
  it('offers the registry label on the dropdown footer', async () => {
    const user = userEvent.setup();
    render(<LookupQuickAddSelect kind="expenseCategory" options={[]} />);

    await openFooter(user);

    const button = await screen.findByRole('button', { name: /Add Expense Category/i });
    expect(button).toBeInTheDocument();
    expect(button).toHaveAttribute('data-testid', 'quick-add-Expense Category');
  });

  it('replaces the Add button with the registry hint when a dependency is missing', async () => {
    const user = userEvent.setup();
    // A breed cannot exist without its animal type, so creation is gated.
    render(<LookupQuickAddSelect kind="breed" ctx={{}} options={[]} />);

    await openFooter(user);

    expect(
      await screen.findByText(/select an animal type first, then add breeds for it/i),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /add breed/i })).not.toBeInTheDocument();
  });

  it('creates through the registry, then appends and selects before reporting success', async () => {
    vi.mocked(expenseCategoriesApi.create).mockResolvedValue(res({ id: 'ec-1', name: 'Utilities' }));

    const order: string[] = [];
    const deferred = createDeferred<void>();
    const onChange = vi.fn(() => order.push('onChange'));
    const onValueSelected = vi.fn(() => order.push('onValueSelected'));
    const onCreated = vi.fn(async () => {
      // Stands in for a parent that refetches its options.
      await deferred.promise;
      order.push('onCreated');
    });

    const user = userEvent.setup();
    render(
      <LookupQuickAddSelect
        kind="expenseCategory"
        options={[]}
        onChange={onChange}
        onValueSelected={onValueSelected}
        onCreated={onCreated}
      />,
    );

    await openFooter(user);
    await user.click(await screen.findByRole('button', { name: /Add Expense Category/i }));

    const modal = addModal();
    await user.type(within(modal).getByLabelText('Name'), 'Utilities');
    await user.click(within(modal).getByRole('button', { name: 'Add Expense Category' }));

    expect(expenseCategoriesApi.create).toHaveBeenCalledWith({
      name: 'Utilities',
      description: undefined,
    });
    // Nothing is selected while the parent's refresh is still in flight.
    await waitFor(() =>
      expect(onCreated).toHaveBeenCalledWith({ id: 'ec-1', label: 'Utilities' }, 'expenseCategory'),
    );
    expect(onChange).not.toHaveBeenCalled();

    deferred.resolve();
    await waitFor(() => expect(onChange).toHaveBeenCalledWith('ec-1'));
    expect(order).toEqual(['onCreated', 'onChange', 'onValueSelected']);
    expect(await screen.findByText('Expense Category "Utilities" created')).toBeInTheDocument();
  });

  it('keeps the modal open, preserves the input, and does not spoil the parent on failure', async () => {
    vi.mocked(expenseCategoriesApi.create).mockRejectedValue({
      response: { data: 'Category already exists' },
    });

    const onCreated = vi.fn();
    const user = userEvent.setup();
    render(
      <LookupQuickAddSelect kind="expenseCategory" options={[]} onCreated={onCreated} />,
    );

    await openFooter(user);
    await user.click(await screen.findByRole('button', { name: /Add Expense Category/i }));

    const modal = addModal();
    await user.type(within(modal).getByLabelText('Name'), 'Utilities');
    await user.click(within(modal).getByRole('button', { name: 'Add Expense Category' }));

    // The server's own message is surfaced verbatim, and the typed value survives.
    expect(await screen.findByText('Category already exists')).toBeInTheDocument();
    expect(within(modal).getByLabelText('Name')).toHaveValue('Utilities');
    expect(onCreated).not.toHaveBeenCalled();
    expect(expenseCategoriesApi.create).toHaveBeenCalledTimes(1);
  });

  it('still supports an explicit field/handler pair for lookups outside the registry', async () => {
    const onQuickAdd = vi.fn(async () => 'custom-1');

    const user = userEvent.setup();
    render(
      <LookupQuickAddSelect
        label="Widget"
        options={[]}
        fields={[
          { name: 'name', label: 'Name', widget: 'input', required: true, placeholder: 'e.g. Crank' },
        ]}
        onQuickAdd={onQuickAdd}
      />,
    );

    await openFooter(user);
    await user.click(await screen.findByRole('button', { name: /Add Widget/i }));

    const modal = addModal();
    await user.type(within(modal).getByLabelText('Name'), 'Crank');
    await user.click(within(modal).getByRole('button', { name: 'Add Widget' }));

    expect(onQuickAdd).toHaveBeenCalledWith({ name: 'Crank' });
    expect(await screen.findByText('Widget "Crank" created')).toBeInTheDocument();
  });
});
