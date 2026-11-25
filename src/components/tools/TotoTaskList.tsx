import { useId, useRef, type KeyboardEvent } from "react";
import type { TotoItem } from "../../services/desktop";
import { JuOverflowText, JuText } from "../ju";

/** Toto's task list has row navigation, rather than a focusable table container. */
export function TotoTaskList({ items, selectedId, onSelect, onOpen }: {
    items: TotoItem[];
    selectedId: string | null;
    onSelect: (item: TotoItem) => void;
    onOpen: (item: TotoItem) => void;
}) {
    const containerRef = useRef<HTMLDivElement>(null);
    const instructionsId = useId();
    const activeId = items.some((item) => item.id === selectedId) ? selectedId : items[0]?.id;

    const onRowKeyDown = (event: KeyboardEvent<HTMLTableRowElement>, index: number) => {
        if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
        const textControls = Array.from(event.currentTarget.querySelectorAll<HTMLElement>(".ju-overflow-text[role='button']"));
        if (event.key === "F2" && textControls.length) {
            event.preventDefault();
            textControls[0].focus({ preventScroll: true });
            return;
        }
        if (event.target instanceof HTMLElement && textControls.includes(event.target)) {
            if (event.key === "ArrowLeft" || event.key === "ArrowRight") {
                event.preventDefault();
                const current = textControls.indexOf(event.target);
                const next = (current + (event.key === "ArrowRight" ? 1 : -1) + textControls.length) % textControls.length;
                textControls[next].focus({ preventScroll: true });
            } else if (event.key === "Escape") {
                event.preventDefault();
                event.currentTarget.focus({ preventScroll: true });
            }
            return;
        }
        if (event.key === "Enter") {
            event.preventDefault();
            onOpen(items[index]);
        } else if (event.key === "ArrowLeft" || event.key === "ArrowRight") {
            event.preventDefault();
        } else if (event.key === "ArrowDown" || event.key === "ArrowUp") {
            event.preventDefault();
            const next = (index + (event.key === "ArrowDown" ? 1 : -1) + items.length) % items.length;
            const container = containerRef.current;
            const row = container?.querySelectorAll<HTMLTableRowElement>("tbody tr[data-toto-row]")[next];
            onSelect(items[next]);
            row?.focus({ preventScroll: true });
            if (!container || !row) return;
            const top = row.getBoundingClientRect().top - container.getBoundingClientRect().top + container.scrollTop;
            const headerHeight = container.querySelector("thead")?.getBoundingClientRect().height ?? 0;
            if (top < container.scrollTop + headerHeight) container.scrollTop = top - headerHeight;
            else if (top + row.offsetHeight > container.scrollTop + container.clientHeight) container.scrollTop = top + row.offsetHeight - container.clientHeight;
        }
    };

    return <div ref={containerRef} className="toto-task-list">
        <JuText id={instructionsId} className="sr-only">使用上下方向键选择事项，回车查看详情；按 F2 进入截断文本，使用左右方向键切换文本，回车查看完整内容。</JuText>
        <table className="ju-table toto-task-table" role="grid" aria-label="Toto 进行中事项" aria-describedby={instructionsId}>
            <thead><tr>{["事项内容", "计划时间", "提醒时间", "提醒状态", "创建时间", "备注"].map((header) => <th key={header} scope="col"><JuText>{header}</JuText></th>)}</tr></thead>
            <tbody>
                {items.length ? items.map((item, index) => <tr key={item.id} data-toto-row=""
                    tabIndex={item.id === activeId ? 0 : -1}
                    className={item.id === activeId ? "is-selected" : undefined}
                    aria-selected={item.id === activeId}
                    onKeyDown={(event) => onRowKeyDown(event, index)}
                    onClickCapture={(event) => {
                        if (event.target instanceof Element && event.target.closest(".ju-overflow-text[role='button']")) onSelect(item);
                    }}
                    onClick={(event) => { onSelect(item); event.currentTarget.focus({ preventScroll: true }); }}
                    onDoubleClick={() => onOpen(item)}>
                    <td><JuOverflowText text={item.content} tabIndex={-1} /></td>
                    <td><JuText>{item.planned_at ?? "—"}</JuText></td>
                    <td><JuText>{item.reminder_at ?? "—"}</JuText></td>
                    <td><JuText className={`toto-status status-${item.reminder_status === "已提醒" ? "reminded" : item.reminder_status === "未提醒" ? "pending" : "none"}`}>{item.reminder_status}</JuText></td>
                    <td><JuText>{item.created_at}</JuText></td>
                    <td><JuOverflowText text={item.notes || "—"} tabIndex={-1} /></td>
                </tr>) : <tr><td className="ju-table-empty" colSpan={6}><JuText>暂无进行中事项，按 Alt+A 新增。</JuText></td></tr>}
            </tbody>
        </table>
    </div>;
}
