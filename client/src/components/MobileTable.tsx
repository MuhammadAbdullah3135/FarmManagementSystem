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

/**
 * Mobile-friendly antd Table. Identical to `Table` on desktop; on phones it
 * enables horizontal swiping and optionally pins the identity/Actions columns
 * so they stay visible while the rest of the table scrolls sideways.
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

  const nextColumns = columns?.map((column) => {
    const key = column.key ?? ('dataIndex' in column
      ? Array.isArray(column.dataIndex)
        ? column.dataIndex.join('.')
        : column.dataIndex
      : undefined);
    // Some pages define the Actions column without a key; fall back to the title.
    const isActionsColumn = key === 'actions' || (fixedActions && column.title === 'Actions');
    if (fixedKeyColumn !== undefined && key === fixedKeyColumn) {
      return {
        ...column,
        fixed: 'left' as const,
        width: fixedKeyColumnWidth,
      };
    }
    if (fixedActions && isActionsColumn) {
      return {
        ...column,
        fixed: 'right' as const,
        width: column.width ?? fixedActionsWidth,
      };
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
