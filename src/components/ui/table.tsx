// shadcn/ui Table composition, adapted to Juju's existing CSS and scroll container.
import type { ComponentProps } from "react";

export function Table(props: ComponentProps<"table">) {
    return <table data-slot="table" {...props} />;
}
export function TableHeader(props: ComponentProps<"thead">) {
    return <thead data-slot="table-header" {...props} />;
}
export function TableBody(props: ComponentProps<"tbody">) {
    return <tbody data-slot="table-body" {...props} />;
}
export function TableFooter(props: ComponentProps<"tfoot">) {
    return <tfoot data-slot="table-footer" {...props} />;
}
export function TableRow(props: ComponentProps<"tr">) {
    return <tr data-slot="table-row" {...props} />;
}
export function TableHead(props: ComponentProps<"th">) {
    return <th data-slot="table-head" {...props} />;
}
export function TableCell(props: ComponentProps<"td">) {
    return <td data-slot="table-cell" {...props} />;
}
export function TableCaption(props: ComponentProps<"caption">) {
    return <caption data-slot="table-caption" {...props} />;
}
