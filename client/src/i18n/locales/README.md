# Locale resources

One JSON file per namespace per locale, loaded statically by `src/i18n/index.ts`.

```
locales/
  en/*.json   the source of truth — every key is authored here first
  es/*.json   Spanish, machine-generated (see below)
```

## Spanish is machine-translated and unreviewed

`es/*.json` was produced by machine translation of `en/*.json`. The keys are a
byte-for-byte mirror of English — same keys, same nesting, same `{{placeholders}}`,
same `\n` line breaks — so a missing or extra key is a bug the coverage test catches.
The *wording* has never been read by a native speaker. It is good enough to ship a
usable Spanish UI, and it must be reviewed before it is treated as final copy.

That is recorded in `translation-status.json` next to this file, so the state is visible from
the app's own tree rather than only in this document:

```json
{ "es": { "source": "machine-translation", "reviewed": false, ... } }
```

`npm run lint` runs `tools/i18n/check-locale-coverage.mjs`, which fails if any locale drifts
away from the English key set (a missing key, an invented one, a blank value, a renamed
`{{placeholder}}`), and fails as well on a value left identical to English unless it is listed
in `untranslated-allowlist.json` with a written reason.

## Terminology (keep these consistent when reviewing)

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

Deliberately **not** translated: the currency symbol (`$` is a bare literal in the
UI — see `docs/I18N.md`), the technical values used by parsers (`yyyy-MM-dd`), and
farm-owned lookup data seeded by `FarmDefaultsSeeder` (breed names, location names,
statuses, payment methods), which the user edits per farm.

Values that are legitimately identical in both languages (true cognates such as *animal*,
*total* and *no*, the unit symbol `kg`, the acronym *FMS*) are listed in
`untranslated-allowlist.json` with a reason each, so "untouched" is a reviewed decision rather
than something a coverage check has to guess at.

## Adding a namespace or key

1. Add the key to `locales/en/<namespace>.json`.
2. Add the same key to every other locale (the coverage test fails otherwise).
3. Run `npm run test -- i18n` and `npm run lint`.
