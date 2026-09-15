import { Table } from 'antd';
import type { TableProps } from 'antd';
import { useIsMobile } from '../hooks/useIsMobile';

export interface MobileTableProps<RecordType> extends TableProps<RecordType> {
  /**
   * dataIndex/key of the identity column (Tag, Name, Employee, ...) to pin to
   * the left edge while the table is swiped horizontally on phones.
   */
  fixedKeyColumn?: string;
  /** Width applied to the pinned key column on mobile. Default 130. */
  fixedKeyColumnWidth?: number;
  /** Pin the Actions column to the right edge on phones. Default true. */
  fixedActions?: boolean;
  /** Width applied to the pinned Actions column on mobile. Default 120. */
  fixedActionsWidth?: number;
  /**
   * Overrides the horizontal scroll width on mobile. Defaults to
   * 'max-content' so columns keep their natural width and headers stay on
   * one line.
   */
  minScrollWidth?: number | string;
}

const DEFAULT_KEY_WIDTH = 130;
const DEFAULT_ACTIONS_WIDTH = 120;
/** Fallback width for widthless columns when pinning makes auto-sizing impossible. */
const DEFAULT_COLUMN_WIDTH = 130;

/**
 * Width hints for widthless columns, matched against key/dataIndex then
 * title. antd collapses widthless columns into unreadable slivers once any
 * column is fixed, so every column needs a width; these hints keep common
 * column kinds from getting a one-size-fits-all guess.
 */
const WIDTH_HINTS: Array<{ match: RegExp; width: number }> = [
  { match: /(date|time|timestamp|fedat)$/i, width: 110 },
  { match: /^(method|result|status|stage|sex|outcome|type|category|role|unit)$/i, width: 100 },
  { match: /^(qty|quantity|count|amount|cost|price|total|weight|days.*)$/i, width: 96 },
];

function columnKey(column: NonNullable<TableProps<any>['columns']>[number]): string | undefined {
  if (!column) return undefined;
  if (column.key !== undefined) return String(column.key);
  if ('dataIndex' in column && column.dataIndex !== undefined) {
    return Array.isArray(column.dataIndex) ? column.dataIndex.join('.') : String(column.dataIndex);
  }
  return undefined;
}

/**
 * Mobile-friendly antd Table. Identical to `Table` on desktop; on phones it
 * enables horizontal swiping and optionally pins the identity/Actions columns
 * so they stay visible while the rest of the table scrolls sideways.
 *
 * When any column is pinned, every widthless column gets an explicit width —
 * antd's fixed-column layout collapses widthless columns into unreadable
 * slivers otherwise. Columns that already declare a width keep it.
 */
function MobileTable<RecordType extends object = any>({
  fixedKeyColumn,
  fixedKeyColumnWidth = DEFAULT_KEY_WIDTH,
  fixedActions = true,
  fixedActionsWidth = DEFAULT_ACTIONS_WIDTH,
  minScrollWidth,
  columns,
  scroll,
  ...rest
}: MobileTableProps<RecordType>) {
  const isMobile = useIsMobile();

  if (!isMobile) {
    return <Table<RecordType> columns={columns} scroll={scroll} {...rest} />;
  }

  const pinning = fixedKeyColumn !== undefined || fixedActions;

  const nextColumns = columns?.map((column) => {
    const key = columnKey(column);
    const titleText = typeof column.title === 'string' ? column.title : undefined;
    // Some pages define the Actions column without a key; fall back to the title.
    const isActionsColumn = key === 'actions' || (fixedActions && titleText === 'Actions');
    if (fixedKeyColumn !== undefined && key === fixedKeyColumn) {
      return {
        ...column,
        fixed: 'left' as const,
        width: column.width ?? fixedKeyColumnWidth,
      };
    }
    if (fixedActions && isActionsColumn) {
      return {
        ...column,
        fixed: 'right' as const,
        width: column.width ?? fixedActionsWidth,
      };
    }
    // With fixed columns antd cannot auto-size widthless columns — they
    // collapse into slivers. Give them an explicit width so headers and
    // cells stay readable while swiping.
    if (pinning && column.width === undefined) {
      const hinted = WIDTH_HINTS.find((h) =>
        (key !== undefined && h.match.test(key)) ||
        (titleText !== undefined && h.match.test(titleText)),
      );
      return { ...column, width: hinted?.width ?? DEFAULT_COLUMN_WIDTH };
    }
    return column;
  });

  const nextScroll = {
    ...scroll,
    x: minScrollWidth ?? scroll?.x ?? 'max-content',
  };

  return <Table<RecordType> columns={nextColumns} scroll={nextScroll} {...rest} />;
}

export default MobileTable;
