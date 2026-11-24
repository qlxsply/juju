import type { KeyboardEventHandler, ReactNode } from "react";
import { Table, TableBody, TableCaption, TableCell, TableHead, TableHeader, TableRow } from "../ui/table";

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
            <Table className="ju-table">
                {caption && <TableCaption className="sr-only">{caption}</TableCaption>}
                <TableHeader>
                    <TableRow>
                        {columns.map((column) => (
                            <TableHead key={column.id} scope="col">
                                {column.header}
                            </TableHead>
                        ))}
                    </TableRow>
                </TableHeader>
                <TableBody>
                    {data.length > 0 ? (
                        data.map((row) => (
                            <TableRow
                                key={getRowId(row)}
                                className={selectedRowId === getRowId(row) ? "is-selected" : undefined}
                                aria-selected={selectedRowId === getRowId(row)}
                                onClick={() => onRowClick?.(row)}
                                onDoubleClick={() => onRowDoubleClick?.(row)}
                            >
                                {columns.map((column) => (
                                    <TableCell key={column.id}>{column.cell(row)}</TableCell>
                                ))}
                            </TableRow>
                        ))
                    ) : (
                        <TableRow>
                            <TableCell
                                className="ju-table-empty"
                                colSpan={Math.max(columns.length, 1)}
                            >
                                {emptyMessage}
                            </TableCell>
                        </TableRow>
                    )}
                </TableBody>
            </Table>
        </div>
    );
}
