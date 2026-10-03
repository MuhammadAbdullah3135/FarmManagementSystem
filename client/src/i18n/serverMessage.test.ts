import { afterEach, describe, expect, it } from 'vitest';
import i18n from './index';
import { argKindsFor, canRenderMessageKey, messageKeyFromResponse, renderKeyedMessage } from './serverMessage';
import validationEn from './locales/en/validation.json';

/**
 * Every placeholder in a server-keyed sentence has a declared kind.
 *
 * An undeclared argument is the quiet failure this whole table exists to prevent: it renders as
 * its raw wire value, so an English client is correct, the translations still pass the coverage
 * check, and only a reader in another language sees a value nobody formatted. Nothing else in
 * the build notices — which is why this test exists rather than a comment.
 *
 * The placeholder set comes from the English bundle, so a key whose sentence grows a new
 * argument fails here before the call site can send the wrong shape for it.
 */
describe('argument kinds', () => {
  // `unauthorized` joined the families in item 27, and the alternation is the test's coverage
  // gate: a family missing from it is skipped without a word, so adding a key to a family the
  // regex does not name is how a whole new family ends up unchecked while the suite is green.
  const SERVER_PREFIX = /^validation\.(lookup|conflict|supersede|validation|unauthorized)\./;

  it('declares a kind for every placeholder in every keyed sentence', () => {
    const undeclared: string[] = [];

    for (const [group, entries] of Object.entries(validationEn)) {
      for (const [name, sentence] of Object.entries(entries as Record<string, string>)) {
        const key = `validation.${group}.${name}`;
        if (!SERVER_PREFIX.test(key)) continue;

        const placeholders = [...(sentence as string).matchAll(/\{\{(\w+)\}\}/g)].map((m) => m[1]);
        if (placeholders.length === 0) continue;

        const kinds = argKindsFor(key);
        if (!kinds) {
          undeclared.push(`${key} takes ${placeholders.join(', ')} but declares no kinds at all`);
          continue;
        }
        for (const placeholder of placeholders) {
          if (!(placeholder in kinds)) {
            undeclared.push(`${key}: {{${placeholder}}} has no declared kind`);
          }
        }
      }
    }

    expect(undeclared).toEqual([]);
  });

  it('declares nothing for an argument a sentence does not have', () => {
    // The other direction: a stale entry would render an argument the sentence never mentions,
    // which is harmless today and quietly wrong the day the sentence is reworded. Cheap to
    // check, and it keeps the table honest rather than merely large.
    const stale: string[] = [];

    for (const [group, entries] of Object.entries(validationEn)) {
      for (const [name, sentence] of Object.entries(entries as Record<string, string>)) {
        const key = `validation.${group}.${name}`;
        const kinds = argKindsFor(key);
        if (!kinds) continue;
        for (const declared of Object.keys(kinds)) {
          if (!(sentence as string).includes(`{{${declared}}}`)) {
            stale.push(`${key} declares ${declared} but its sentence has no {{${declared}}}`);
          }
        }
      }
    }

    expect(stale).toEqual([]);
  });
});

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

  it('says the "already happened" refusals in the reader’s language', async () => {
    // Slice 2's first half: the conflict and supersede sentences that take no argument. These
    // travel as a bare header, so this is the whole mechanism end to end for them — the key the
    // server attaches, the bundle entry, and the language it comes out in.
    const refusals: [string, string, string][] = [
      ['validation.conflict.emailAlreadyRegistered', 'El correo electrónico ya está registrado', 'البريد الإلكتروني مسجل بالفعل'],
      ['validation.conflict.dietPlanHasGeneratedTasks', 'No se puede eliminar un plan de dieta que ya generó tareas de alimentación; desactívelo en su lugar', 'لا يمكن حذف نظام غذائي أنشأ مهام تغذية بالفعل؛ عطّله بدلاً من ذلك'],
      ['validation.supersede.taskAlreadyCompleted', 'La tarea ya está completada', 'المهمة مكتملة بالفعل'],
      ['validation.conflict.inventoryItemHasHistory', 'No se puede eliminar un artículo de inventario con historial de movimientos de existencias', 'لا يمكن حذف عنصر مخزون لديه سجل حركات مخزون'],
    ];

    for (const [key, spanish, arabic] of refusals) {
      await i18n.changeLanguage('es');
      const inSpanish = renderKeyedMessage(key, null, 'the English the server sent');
      expect(inSpanish).toBe(spanish);
      expect(inSpanish).not.toContain('{{');

      await i18n.changeLanguage('ar');
      const inArabic = renderKeyedMessage(key, null, 'the English the server sent');
      expect(inArabic).toBe(arabic);
      expect(inArabic).not.toContain('{{');
    }
  });

  it('says the argument-taking refusals in the reader’s language', async () => {
    // Slice 2's second half: the 21 conflict and supersede sentences that carry a value. The
    // server sends the value unformatted — a number as a number, an enum as its wire value —
    // so the renderer is what decides how it reads. Each row is [key, args, es, ar].
    const refusals: [string, Record<string, unknown>, string, string][] = [
      [
        'validation.conflict.animalTagExists', { tag: 'A-12' },
        "Ya existe en esta granja un animal con la etiqueta 'A-12'",
        "يوجد بالفعل في هذه المزرعة حيوان بالوسم 'A-12'",
      ],
      [
        'validation.conflict.scheduleTimeExists', { time: '07:30' },
        'Ya existe un horario a las 07:30 para este plan de dieta',
        'يوجد بالفعل جدول في الساعة 07:30 لهذا النظام الغذائي',
      ],
    ];

    for (const [key, args, spanish, arabic] of refusals) {
      await i18n.changeLanguage('es');
      const inSpanish = renderKeyedMessage(key, args, 'the English the server sent');
      expect(inSpanish).toBe(spanish);
      expect(inSpanish).not.toContain('{{');

      await i18n.changeLanguage('ar');
      const inArabic = renderKeyedMessage(key, args, 'the English the server sent');
      expect(inArabic).toBe(arabic);
      expect(inArabic).not.toContain('{{');
    }
  });

  it('formats a stock quantity in the reader’s language rather than the server’s', async () => {
    // The decision this slice rests on: the server sends `1234.5` and lets the client decide
    // how a number is written. Spanish wants 1234,5 and Arabic wants Arabic-Indic digits, so
    // a server-side format string would have frozen English separators into every sentence.
    await i18n.changeLanguage('en');
    expect(renderKeyedMessage(
      'validation.conflict.insufficientStockToFeed',
      { current: 1234.5, unit: 'Kilogram', attempted: 2000 },
      'ignored',
    )).toBe('Insufficient stock: current stock is 1,234.5 kg, attempted to feed 2,000');

    await i18n.changeLanguage('es');
    expect(renderKeyedMessage(
      'validation.conflict.insufficientStockToFeed',
      { current: 1234.5, unit: 'Kilogram', attempted: 2000 },
      'ignored',
    )).toBe('Existencias insuficientes: el stock actual es 1234,5 kg, se intentó alimentar con 2000');

    await i18n.changeLanguage('ar');
    const arabic = renderKeyedMessage(
      'validation.conflict.insufficientStockToFeed',
      { current: 1234.5, unit: 'Kilogram', attempted: 2000 },
      'ignored',
    );
    expect(arabic).toContain('كجم');
    expect(arabic).not.toContain('Kilogram');
    expect(arabic).not.toContain('{{');
  });

  it('renders an unauthorized refusal with the action from its vocabulary', async () => {
    // The eight refusals that say "Only farm owners and managers can …" share one frame and
    // differ only in the action, so the action travels as a wire value and is resolved here.
    // English has to come out byte-identical to the sentence the API has always returned,
    // because that sentence is what every response-body assertion is written against.
    await i18n.changeLanguage('en');
    expect(renderKeyedMessage(
      'validation.unauthorized.farmOwnerOrManagerOnly',
      { action: 'ChangeMemberRoles' },
      'ignored',
    )).toBe('Only farm owners and managers can change member roles');

    // A Spanish reader gets the frame in Spanish and an action phrase Spanish has already
    // written. A verb stem would have asked Spanish to conjugate it at this point; a phrase
    // does not, which is the whole reason the argument is a vocabulary and not a verb.
    await i18n.changeLanguage('es');
    expect(renderKeyedMessage(
      'validation.unauthorized.farmOwnerOrManagerOnly',
      { action: 'ChangeMemberRoles' },
      'ignored',
    )).toBe('Solo los propietarios y gestores de la granja pueden cambiar los roles de los miembros');

    await i18n.changeLanguage('ar');
    const arabic = renderKeyedMessage(
      'validation.unauthorized.farmOwnerOrManagerOnly',
      { action: 'ChangeMemberRoles' },
      'ignored',
    );
    expect(arabic).toContain('تغيير أدوار الأعضاء');
    // The raw wire value must not survive: it is an English identifier, and seeing one inside
    // an Arabic sentence is the exact failure the vocabulary exists to prevent.
    expect(arabic).not.toContain('ChangeMemberRoles');
    expect(arabic).not.toContain('{{');
  });

  it('keeps the owners-only frame distinct from the owners-and-managers one', async () => {
    // Deleting a farm is not a manager's to do, and that difference is the point of the
    // sentence. Two keys rather than one key with the subject as an argument, because no
    // language can fill "and managers" from a slot without being asked to.
    await i18n.changeLanguage('en');
    expect(renderKeyedMessage(
      'validation.unauthorized.farmOwnerOnly',
      { action: 'DeleteFarms' },
      'ignored',
    )).toBe('Only farm owners can delete farms');

    await i18n.changeLanguage('es');
    expect(renderKeyedMessage(
      'validation.unauthorized.farmOwnerOnly',
      { action: 'DeleteFarms' },
      'ignored',
    )).toBe('Solo los propietarios de la granja pueden eliminar granjas');
  });

  it('falls back to the raw action when the vocabulary does not know it', async () => {
    // A newer server may name an action this client has never heard of. The refusal must still
    // render rather than throw, which is why `enumLabelOf` returns the value it was given. It
    // degrades to English, which is the honest outcome: there is no correct translation to fall
    // back to, and inventing one would be worse than showing the identifier.
    await i18n.changeLanguage('es');
    const rendered = renderKeyedMessage(
      'validation.unauthorized.farmOwnerOrManagerOnly',
      { action: 'ArchiveMembers' },
      'ignored',
    );
    expect(rendered).toContain('ArchiveMembers');
    expect(rendered).not.toContain('{{');
  });

  it('renders a feed unit from the feed vocabulary, not as the stored enum name', async () => {
    // The server sends `Kilogram`, which is what the database holds. A Spanish reader must
    // see the unit its own language writes, and an Arabic reader must not see the raw enum.
    await i18n.changeLanguage('es');
    expect(renderKeyedMessage(
      'validation.conflict.insufficientStockAvailable',
      { available: 3, unit: 'Bag' },
      'ignored',
    )).toBe('Existencias insuficientes: la cantidad disponible es 3 saco');

    await i18n.changeLanguage('ar');
    const arabic = renderKeyedMessage(
      'validation.conflict.insufficientStockAvailable',
      { available: 3, unit: 'Bag' },
      'ignored',
    );
    expect(arabic).toContain('كيس');
    expect(arabic).not.toContain('Bag');
  });

  it('renders each status argument through the enum its own key belongs to', async () => {
    // Three different enums reach a reader through an argument called `status`, and they
    // disagree about some values. The values here are chosen to disagree on purpose:
    // `InProgress` exists only in a farm task's status, and `Skipped` only in a feeding
    // task's. A renderer that guessed the enum from the value would get one of these wrong,
    // and a pin using only `Completed` would not notice, because all three enums agree on it.
    await i18n.changeLanguage('es');
    expect(renderKeyedMessage(
      'validation.conflict.taskNotPendingToStart', { status: 'InProgress' }, 'ignored',
    )).toBe('Solo las tareas pendientes se pueden iniciar. Estado actual: En progreso');

    await i18n.changeLanguage('ar');
    expect(renderKeyedMessage(
      'validation.conflict.taskNotPendingToStart', { status: 'InProgress' }, 'ignored',
    )).toBe('لا يمكن بدء سوى المهام المعلّقة. الحالة الحالية: قيد التنفيذ');

    // A feeding task's status, which has no `InProgress` at all — this one is `Skipped`.
    await i18n.changeLanguage('ar');
    expect(renderKeyedMessage(
      'validation.conflict.feedingTaskAlreadyStatus', { status: 'Skipped' }, 'ignored',
    )).toBe('المهمة بالفعل تم تخطيها');

    // And an attendance day's, which is a third vocabulary entirely.
    await i18n.changeLanguage('es');
    expect(renderKeyedMessage(
      'validation.supersede.attendanceAlreadyMarked', { status: 'HalfDay' }, 'ignored',
    )).toBe('Ese día ya tiene un registro de asistencia marcado como Medio día; no se modificó');
  });

  it('leaves an argument with no declared vocabulary as it arrived', async () => {
    // The honest fallback: an undeclared argument renders as its raw value rather than as a
    // translation nobody chose. `name` is deliberately not declared — it is the reader's own
    // text, and translating it would be wrong.
    await i18n.changeLanguage('ar');
    const rendered = renderKeyedMessage(
      'validation.conflict.roleNameExists', { name: 'Vet' }, 'ignored',
    );
    expect(rendered).toContain('Vet');
    expect(rendered).not.toContain('{{');
  });

  it('joins a list argument the way the reader’s language joins one', async () => {
    // The server sends `['.jpg', '.png', '.pdf']` rather than ".jpg, .png, .pdf". A server that
    // joined the list itself would have frozen English's punctuation into every language;
    // Spanish wants "y" and Arabic "و", and neither is a character the server chose.
    const args = { extension: '.exe', allowed: ['.jpg', '.png', '.pdf'] };

    await i18n.changeLanguage('en');
    expect(renderKeyedMessage('validation.validation.fileTypeNotAllowed', args, 'ignored'))
      .toBe("File type '.exe' is not allowed. Allowed types: .jpg, .png, and .pdf");

    await i18n.changeLanguage('es');
    expect(renderKeyedMessage('validation.validation.fileTypeNotAllowed', args, 'ignored'))
      .toBe("El tipo de archivo '.exe' no está permitido. Tipos permitidos: .jpg, .png y .pdf");

    await i18n.changeLanguage('ar');
    const arabic = renderKeyedMessage('validation.validation.fileTypeNotAllowed', args, 'ignored');
    expect(arabic).toContain('و');
    expect(arabic).not.toContain('.jpg, .png');
    expect(arabic).not.toContain('{{');
  });

  it('renders a size in the reader’s language and keeps the server’s one decimal', async () => {
    // The server's English is `{n:0.#}` megabytes — at most one decimal. The value goes on the
    // wire as a plain number so the client can place the decimal mark, and the client keeps
    // that one-decimal rule so an English reader sees the same bytes the API always returned.
    //
    // Arabic keeps Latin digits here, and that is the app's standing decision rather than an
    // oversight: `localeTag` asks for `ar-EG-u-nu-latn` because the digits a farm types into a
    // weight or an amount have to be comparable character by character with the source
    // document. A pin that expected `١٢٫٥` here would be pinning a choice this app made
    // deliberately, against it.
    await i18n.changeLanguage('es');
    expect(renderKeyedMessage('validation.validation.fileTooLarge', { max: 12.5 }, 'ignored'))
      .toBe('El archivo supera el tamaño máximo permitido de 12,5 MB');

    await i18n.changeLanguage('ar');
    const arabic = renderKeyedMessage('validation.validation.fileTooLarge', { max: 12.5 }, 'ignored');
    expect(arabic).toContain('12.5');
    expect(arabic).toContain('ميغابايت');
    expect(arabic).not.toContain('MB');

    // A value that would round under the server's `:0.#` still renders with at most one
    // decimal, so the English the API returns and the English the client renders agree.
    await i18n.changeLanguage('en');
    expect(renderKeyedMessage('validation.validation.fileTooLarge', { max: 12.46 }, 'ignored'))
      .toBe('File exceeds the maximum allowed size of 12.5 MB');
  });

  it('leaves an exception’s own message alone while translating the frame around it', async () => {
    // A CsvHelper or S3 message is not copy — translating it would invent a sentence the
    // library never sent. So the sentence is the reader's language and the diagnostic inside
    // it stays as it arrived, which is the most a system fault can honestly offer.
    await i18n.changeLanguage('es');
    const spanish = renderKeyedMessage(
      'validation.validation.importCsvUnreadable',
      { detail: 'Wrong number of fields' },
      'ignored',
    );
    expect(spanish).toBe('No se pudo leer el archivo CSV: Wrong number of fields');
    expect(spanish).not.toContain('{{');
  });

  it('groups a count the reader’s language groups', async () => {
    await i18n.changeLanguage('en');
    expect(renderKeyedMessage('validation.validation.pushDeviceLimitReached', { count: 1234 }, 'ignored'))
      .toBe('This account already has 1,234 devices receiving push notifications. Turn one of them off first.');

    await i18n.changeLanguage('es');
    expect(renderKeyedMessage('validation.validation.pushDeviceLimitReached', { count: 1234 }, 'ignored'))
      .toBe('Esta cuenta ya tiene 1234 dispositivos recibiendo notificaciones push. Desactiva uno primero.');
  });

  it('renders a free-text unit as the reader wrote it, not through the feed vocabulary', async () => {
    // A medicine's unit is whatever that farm typed — `kg` on a Medicine means what they meant
    // by it. The feed-unit vocabulary belongs to FeedType, and running this through it would
    // translate a farm's own word into a word nobody asked for.
    await i18n.changeLanguage('ar');
    const arabic = renderKeyedMessage(
      'validation.validation.medicineStockInsufficient',
      { available: 5, unit: 'bottles', requested: 9 },
      'ignored',
    );
    expect(arabic).toContain('bottles');
    expect(arabic).not.toContain('{{');
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
