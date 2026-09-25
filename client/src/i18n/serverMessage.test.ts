import { describe, expect, it } from 'vitest';
import { canRenderMessageKey, messageKeyFromResponse, renderKeyedMessage } from './serverMessage';

describe('renderKeyedMessage', () => {
  it('renders a server validation key with its interpolation args', () => {
    // The same key the server sends; the sentence belongs to the client's resources.
    expect(renderKeyedMessage('validation.employee.firstNameMaxLength', { max: 100 }, 'ignored'))
      .toBe('First name cannot exceed 100 characters');
  });

  it('falls back to the server’s English text when the key is unknown', () => {
    expect(renderKeyedMessage('validation.doesNotExistYet', null, 'Some English sentence'))
      .toBe('Some English sentence');
  });

  it('falls back when there is no key at all', () => {
    expect(renderKeyedMessage(null, null, 'Tag number is required')).toBe('Tag number is required');
    expect(canRenderMessageKey(null)).toBe(false);
  });
});

describe('messageKeyFromResponse', () => {
  it('reads the header the API attaches to a keyed failure', () => {
    expect(messageKeyFromResponse({ 'x-message-key': 'validation.supplier.nameRequired' }, 'Supplier name is required'))
      .toEqual({ key: 'validation.supplier.nameRequired', args: null });
  });

  it('reads a structured body when there is no header', () => {
    expect(messageKeyFromResponse(undefined, {
      errorKey: 'validation.inventoryItem.nameMaxLength',
      errorArgs: { max: 200 },
    })).toEqual({ key: 'validation.inventoryItem.nameMaxLength', args: { max: 200 } });
  });

  it('reports nothing for a plain English body', () => {
    expect(messageKeyFromResponse(undefined, 'An unexpected error occurred')).toBeNull();
    expect(messageKeyFromResponse(undefined, { message: 'nope' })).toBeNull();
  });
});
