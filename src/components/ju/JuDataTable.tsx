import type { KeyboardEventHandler, ReactNode } from "react";

export interface JuColumn<T> {
    id: string;
    header: string;
    cell: (row: T) => ReactNode;
}

interface JuDataTableProps<T> {
    columns: JuColumn<T>[];
    data: T[];
    getRowId: (row: T) => string;
    caption?: string;
    emptyMessage?: string;
    selectedRowId?: string | null;
    onRowClick?: (row: T) => void;
    onRowDoubleClick?: (row: T) => void;
    onKeyDown?: KeyboardEventHandler<HTMLDivElement>;
}

export function JuDataTable<T>({
    columns,
    data,
    getRowId,
    caption,
    emptyMessage = "暂无数据",
    selectedRowId,
    onRowClick,
    onRowDoubleClick,
    onKeyDown,
}: JuDataTableProps<T>) {
    return (
        <div
            className="ju-table-container"
            role="region"
            aria-label={caption ?? "数据表格"}
            tabIndex={0}
            onKeyDown={onKeyDown}
        >
            <table className="ju-table">
                {caption && <caption className="sr-only">{caption}</caption>}
                <thead>
                    <tr>
                        {columns.map((column) => (
                            <th key={column.id} scope="col">
                                {column.header}
                            </th>
                        ))}
                    </tr>
                </thead>
                <tbody>
                    {data.length > 0 ? (
                        data.map((row) => (
                            <tr
                                key={getRowId(row)}
                                className={selectedRowId === getRowId(row) ? "is-selected" : undefined}
                                aria-selected={selectedRowId === getRowId(row)}
                                onClick={() => onRowClick?.(row)}
                                onDoubleClick={() => onRowDoubleClick?.(row)}
                            >
                                {columns.map((column) => (
                                    <td key={column.id}>{column.cell(row)}</td>
                                ))}
                            </tr>
                        ))
                    ) : (
                        <tr>
                            <td
                                className="ju-table-empty"
                                colSpan={Math.max(columns.length, 1)}
                            >
                                {emptyMessage}
                            </td>
                        </tr>
                    )}
                </tbody>
            </table>
        </div>
    );
}
