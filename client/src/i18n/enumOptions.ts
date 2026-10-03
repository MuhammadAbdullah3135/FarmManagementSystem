import i18n from './index';
import { useTranslation } from 'react-i18next';

/**
 * One option for a Select or Radio.Group: the wire value the API receives,
 * plus the label for the active language. The wire value is never translated —
 * a filter or form submits `Create` or `0` regardless of what is on screen.
 */
export interface EnumOption<V extends string | number = string> {
  value: V;
  label: string;
}

/**
 * Every enum the client renders as chrome, declared once: the wire values are
 * the single source of truth for both the option lists and the label keys, so
 * the compiler keeps the pages, the locale files and this table honest.
 *
 * `keyOf` maps a wire value onto its key segment in the enums locale files —
 * the identity for string enums, an explicit mapping where the wire value is a
 * number, because locale files never carry numeric-looking keys.
 */
export const ENUM_DEFS = {
  auditAction: { values: ['Create', 'Update', 'Delete'], keyOf: (v: string) => v },
  taskStatus: { values: ['Pending', 'InProgress', 'Completed', 'Cancelled'], keyOf: (v: string) => v },
  taskPriority: { values: ['Low', 'Medium', 'High'], keyOf: (v: string) => v },
  movementType: { values: ['Purchase', 'Consumption', 'Transfer', 'Adjustment'], keyOf: (v: string) => v },
  medicalStatus: { values: ['Open', 'InProgress', 'Resolved'], keyOf: (v: string) => v },
  feedingTaskStatus: { values: ['Pending', 'Completed', 'Skipped'], keyOf: (v: string) => v },
  attendanceStatus: { values: ['Present', 'Absent', 'Late', 'HalfDay', 'Leave', 'Holiday'], keyOf: (v: string) => v },
  breedingMethod: { values: [0, 1], keyOf: (v: number) => (v === 0 ? 'natural' : 'artificialInsemination') },
  breedingResult: { values: [0, 1, 2], keyOf: (v: number) => (v === 0 ? 'pending' : v === 1 ? 'confirmed' : 'failed') },
  feedTargetMode: { values: ['animal', 'location'], keyOf: (v: string) => v },
  // Not chrome: a vocabulary the server sends to be dropped into a sentence. The values are
  // FarmOwnerAction member names on the wire, and the labels are whole phrases rather than
  // short labels, because the frame they land in is "Only farm owners and managers can …".
  // It is listed here rather than special-cased in serverMessage so that
  // `FarmOwnerActionVocabularyTests` has one table to compare the C# enum against.
  farmOwnerAction: {
    values: [
      'ChangeMemberRoles', 'RemoveMembers', 'InviteMembers', 'ViewInvitations',
      'RevokeInvitations', 'UpdateFarmDetails', 'DeleteFarms',
    ],
    keyOf: (v: string) => v,
  },
  // The audit log's entity types are the server's own class names. The filter offers the
  // farm-data subset (ENTITY_TYPE_FILTERS in AuditLogPage); rows can also carry values the
  // filter never offered (RefreshToken, AnimalTimelineEvent), which enumLabelOf still
  // translates — anything genuinely unknown falls back to the raw name.
  auditEntityType: {
    values: [
      'Animal', 'WeightRecord', 'FeedRecord', 'FeedType', 'DietPlan', 'Employee', 'Expense',
      'IncomeRecord', 'MedicalRecord', 'Medicine', 'VaccinationRecord', 'BreedingRecord',
      'BirthRecord', 'InventoryItem', 'StockMovement', 'Supplier', 'Customer', 'FarmTask',
      'SalaryPayment', 'AttendanceRecord', 'PerformanceReview', 'RefreshToken', 'AnimalTimelineEvent',
    ],
    keyOf: (v: string) => v,
  },
} as const;

export type EnumName = keyof typeof ENUM_DEFS;

/** The wire-value type of one enum. */
export type EnumValue<N extends EnumName> = (typeof ENUM_DEFS[N])['values'][number];

/**
 * Options for a Select/Radio.Group, in the active language. A useTranslation
 * consumer, so the options re-render when the language changes.
 */
export function useEnumOptions<N extends EnumName>(name: N): EnumOption<EnumValue<N>>[] {
  const { t } = useTranslation('enums');
  const def = ENUM_DEFS[name];
  // The table pairs each value with its key segment, so the cast only restates
  // what ENUM_DEFS guarantees for the concrete enum the caller named.
  const keyOf = def.keyOf as (v: EnumValue<N>) => string;
  return (def.values as readonly EnumValue<N>[]).map((value) => ({
    value,
    label: t(`${name}.${keyOf(value)}`),
  }));
}

/**
 * The tolerant form for server-supplied values: a value the enum declares is
 * translated, anything else comes back as it arrived — an audit row written by
 * a newer server must still render, not throw.
 */
export function enumLabelOf(name: EnumName, value: string | number): string {
  const def = ENUM_DEFS[name];
  return (def.values as readonly (string | number)[]).includes(value)
    ? enumLabel(name, value as never)
    : String(value);
}

/**
 * One label outside React (a column renderer, a Tag), in the active language.
 * Falls back to the raw value only if the key were somehow absent — the
 * key-resolution audit makes that unreachable in a shipped build.
 */
export function enumLabel<N extends EnumName>(name: N, value: EnumValue<N>): string {
  const def = ENUM_DEFS[name];
  const keyOf = def.keyOf as (v: EnumValue<N>) => string;
  return i18n.t(`${name}.${keyOf(value)}`, { ns: 'enums' }) || String(value);
}
