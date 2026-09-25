# Locale resources

One JSON file per namespace per locale, loaded statically by `src/i18n/index.ts`.

```
locales/
  en/*.json   the source of truth — every key is authored here first
  es/*.json   Spanish, machine-generated    (see below)
  ar/*.json   Arabic, machine-generated + hand-adapted plurals, right-to-left
```

## Spanish and Arabic are machine-translated and unreviewed

Both were produced by machine translation of `en/*.json`. The keys are a byte-for-byte mirror
of English — same nesting, same `{{placeholders}}`, same `\n` line breaks — so a missing or
extra key is a bug the coverage test catches. The *wording* has never been read by a native
speaker. Either is good enough to ship a usable UI, and both must be reviewed before they are
treated as final copy.

One deviation from "byte-for-byte", and it is deliberate: **Arabic has six plural forms where
English has one.** A key like `health:intervalDays` (English: `{{count}} days`) is `_zero`,
`_one`, `_two`, `_few`, `_many` and `_other` in Arabic, because that is what Arabic's own
plural rules require — a count of two is a *dual* form that says "two days" with no numeral in
it at all. The coverage guard compares keys by their base name for exactly this reason, lets a
plural form drop a placeholder it does not need while still refusing one English does not have,
and — the rule that matters when editing these files — requires **every** category the language
selects from, read from ICU. Deleting `intervalDays_many` by accident would not break anything
visible (i18next falls back to `_other`), so nothing else would catch it.
The sampled forms came from the same machine translation and were then adapted by hand, so
**they are the first thing a reviewer should check.**

Which languages read right-to-left is not recorded here: it is `RTL_LOCALES` in
`src/i18n/locale.ts`, so it is one list rather than a note in a document.

That is recorded in `translation-status.json` next to this file, so the state is visible from
the app's own tree rather than only in this document:

```json
{
  "es": { "source": "machine-translation", "reviewed": false, ... },
  "ar": { "source": "machine-translation", "reviewed": false, ... }
}
```

`npm run lint` runs `tools/i18n/check-locale-coverage.mjs`, which fails if any locale drifts
away from the English key set (a missing key, an invented one, a blank value, a renamed
`{{placeholder}}`), and fails as well on a value left identical to English unless it is listed
in `untranslated-allowlist.json` with a written reason.

It also runs `tools/i18n/check-physical-directions.mjs`, which is about layout rather than
words: a `margin-left`, a `textAlign: 'left'` or a raw `ArrowLeftOutlined` in a page would read
fine in English and Spanish and be wrong in Arabic. Fix by using the logical property
(`margin-inline-start`, `text-align: start`) or the matching role from
`src/i18n/DirectionalIcon.tsx` — not by adding to the allowlist.

## Terminology (keep these consistent when reviewing)

### Spanish

| English | Spanish | Notes |
| --- | --- | --- |
| farm | granja | not *finca*: farm names are data, and this reads as an organisation |
| herd | rebaño | |
| animal / livestock | animal / ganado | |
| tag / tag number | arete / número de arete | the physical ear tag; *crotal* in Spain, *arete* is understood across LatAm |
| dam / sire | madre / padre | deliberately not *reproductora*, which reads as a machine |
| breeding | reproducción | the module; a *breeding record* is a *registro de reproducción* |
| gestation / pregnant | gestación / preñada | |
| feed | alimento | not *pienso*, which is a concentrate feed specifically |
| vaccination / vaccine | vacunación / vacuna | |
| medicine | medicamento | not *medicina*, which is the discipline |
| vet / veterinarian | veterinario | |
| employee / attendance / payroll | empleado / asistencia / nómina | |
| department / role | departamento / rol | |
| expense / income | gasto / ingreso | |
| payment method | método de pago | |
| cost | costo | *coste* in Spain; Costa here for LatAm-first neutrality |
| amount | importe | |
| inventory / stock | inventario / stock | *stock* is understood and shorter than *existencias* |
| supplier / customer | proveedor / cliente | |
| schedule (noun) | programación | |
| task | tarea | |
| offline | sin conexión | |
| sync (verb / noun) | sincronizar / sincronización | |
| notification / alert / severity | notificación / alerta / gravedad | |
| audit log | registro de auditoría | |
| report / chart / dashboard | informe / gráfico / panel | |
| data export | exportación de datos | |

### Arabic

The same table, for the terms a reviewer will see most often. Where a choice looks arbitrary it
is not: these are the words the UI uses consistently, and changing one in one file is how a UI
starts calling the same thing two names.

| English | Arabic | Notes |
| --- | --- | --- |
| farm | مزرعة | |
| herd | قطيع | a managed group, not a species |
| animal / livestock | الحيوان / المواشي | |
| tag (number) | الوسم | the physical ear tag; kept short because it appears in table headers |
| dam / sire | الأم / الأب | the plain words, not a technical breeding term |
| breeding / mating | التزاوج | the act; the module is التزاوج too |
| gestation / pregnant | الحمل | one word covers both, as in English usage here |
| birth / offspring | الولادة / النسل | |
| feed | العلف | the feed itself, not التغذية (feeding as an activity) |
| vaccination / vaccine | التطعيم / اللقاح | |
| medicine | الدواء | in inventory and dosage contexts |
| medical record | السجل الطبي | |
| vet / veterinarian | الطبيب البيطري | |
| employee / attendance / payroll | الموظف / الحضور / الرواتب | |
| department / role | القسم / الدور | |
| salary / payment | الراتب / الدفعة | |
| expense / income | المصروف / الإيراد | |
| payment method | طريقة الدفع | |
| cost / amount / total | التكلفة / المبلغ / الإجمالي | |
| inventory / stock | المخزون | Arabic has its own word, so unlike Spanish this is **not** kept in English |
| supplier / customer | المورد / العميل | |
| item / unit / quantity | الصنف / الوحدة / الكمية | |
| task | المهمة | |
| schedule | الجدول | a table of times |
| notification / alert / severity | الإشعار / التنبيه / الخطورة | |
| offline / online | غير متصل / متصل | |
| sync | المزامنة | |
| audit log | سجل التدقيق | |
| report / chart / dashboard | التقرير / الرسم البياني / لوحة المعلومات | |
| data export | تصدير البيانات | |
| queue (of unsent records) | قائمة الانتظار | offline writes waiting to sync |

### Not translated in either language

Deliberately **not** translated: the currency symbol (`$` is a bare literal in the
UI — see `docs/I18N.md`), the technical values used by parsers (`yyyy-MM-dd`), and
farm-owned lookup data seeded by `FarmDefaultsSeeder` (breed names, location names,
statuses, payment methods), which the user edits per farm.

Values that are legitimately identical to English are listed in
`untranslated-allowlist.json` with a reason each, so "untouched" is a reviewed decision rather
than something a coverage check has to guess at. An entry may be restricted to the languages it
was reviewed for (`"locales": ["es"]`), because a word can be a genuine cognate in one language
and a real translation in another: *total* is the same word in Spanish and `الإجمالي` in Arabic,
and *stock* is kept in English for Spanish but not for Arabic.

## Adding a namespace or key

1. Add the key to `locales/en/<namespace>.json`.
2. Add the same key to every other locale (the coverage test fails otherwise). If the value
   contains `{{count}}`, add that language's own plural forms: `_other` is mandatory and the rest
   are whichever categories `Intl.PluralRules('<lang>').resolvedOptions().pluralCategories`
   reports — two for English, six for Arabic, four for Polish.
3. If the text needs a direction of its own (an arrow, an elbow, a gradient), use
   `src/i18n/DirectionalIcon.tsx` rather than a literal.
4. Run `npm run test -- i18n` and `npm run lint`.
5. If the key is a **server** message key, run the .NET parity test too — it checks the key
   against every bundle, not just English:

```bash
dotnet test tests/FMS.Domain.Tests --filter "FullyQualifiedName~AlertMessageKeyParity"
```
