import { afterEach, describe, expect, it } from 'vitest';
import i18n from './index';
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

  it('never hands a reader a raw placeholder', async () => {
    // The defect this guards: the server sent the key but dropped its arguments, so
    // i18next left {{max}} in place and the reader got a broken sentence in their own
    // language — worse than the correct English body that was there all along.
    expect(renderKeyedMessage('validation.employee.firstNameMaxLength', null, 'First name cannot exceed 100 characters'))
      .toBe('First name cannot exceed 100 characters');

    await i18n.changeLanguage('es');
    const spanish = renderKeyedMessage('validation.employee.firstNameMaxLength', null, 'First name cannot exceed 100 characters');
    expect(spanish).not.toContain('{{');
    expect(spanish).toBe('First name cannot exceed 100 characters');

    await i18n.changeLanguage('ar');
    const arabic = renderKeyedMessage('validation.employee.firstNameMaxLength', null, 'First name cannot exceed 100 characters');
    expect(arabic).not.toContain('{{');
    expect(arabic).toBe('First name cannot exceed 100 characters');
  });

  it('renders the sentence with its argument when the argument arrives', async () => {
    await i18n.changeLanguage('es');
    expect(renderKeyedMessage('validation.employee.firstNameMaxLength', { max: 100 }, 'ignored'))
      .toBe('El nombre no puede superar los 100 caracteres');

    await i18n.changeLanguage('ar');
    expect(renderKeyedMessage('validation.employee.firstNameMaxLength', { max: 100 }, 'ignored'))
      .toBe('لا يمكن أن يتجاوز الاسم الأول 100 حرفًا');
  });

  it('says the delete refusal in the reader’s language, not the server’s English', async () => {
    // The key the breeding-records delete answers with when gestation records still hang off the
    // mating. It takes no arguments, which is what lets it travel as a bare header.
    expect(renderKeyedMessage('validation.breeding.recordInUse', null, 'ignored'))
      .toBe('This breeding record is still referenced by gestation records, so it cannot be deleted. Delete those records first.');

    await i18n.changeLanguage('es');
    const spanish = renderKeyedMessage('validation.breeding.recordInUse', null, 'ignored');
    expect(spanish).not.toBe('ignored');
    expect(spanish).not.toContain('breeding record');

    await i18n.changeLanguage('ar');
    const arabic = renderKeyedMessage('validation.breeding.recordInUse', null, 'ignored');
    expect(arabic).not.toBe('ignored');
    expect(arabic).not.toContain('breeding record');
  });

  afterEach(async () => {
    await i18n.changeLanguage('en');
  });
});

describe('messageKeyFromResponse', () => {
  it('reads the header the API attaches to a keyed failure', () => {
    expect(messageKeyFromResponse({ 'x-message-key': 'validation.supplier.nameRequired' }, 'Supplier name is required'))
      .toEqual({ key: 'validation.supplier.nameRequired', args: null });
  });

  it('reads the arguments the API attaches beside the key', () => {
    // What the server writes: percent-encoded JSON, so an interpolated value cannot put a
    // quote or a newline into a header.
    const headers = {
      'x-message-key': 'validation.employee.firstNameMaxLength',
      'x-message-args': encodeURIComponent(JSON.stringify({ max: 100 })),
    };
    expect(messageKeyFromResponse(headers, 'First name cannot exceed 100 characters'))
      .toEqual({ key: 'validation.employee.firstNameMaxLength', args: { max: 100 } });
  });

  it('reads a key with no arguments beside it', () => {
    expect(messageKeyFromResponse({ 'x-message-key': 'validation.auth.emailRegistered' }, 'Email already registered'))
      .toEqual({ key: 'validation.auth.emailRegistered', args: null });
  });

  it.each([
    ['missing', undefined],
    ['blank', '   '],
    ['not percent-encoded', 'not json at all'],
    ['a broken escape', '%zz'],
    ['JSON that is not an object', encodeURIComponent('[1,2,3]')],
  ])('treats %s arguments as no arguments at all', (_label, raw) => {
    // A header the client cannot read is a missing argument, not a thrown error: the
    // caller's fallback is a whole correct sentence.
    expect(messageKeyFromResponse({ 'x-message-key': 'validation.employee.firstNameMaxLength', 'x-message-args': raw }, 'English body'))
      .toEqual({ key: 'validation.employee.firstNameMaxLength', args: null });
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
