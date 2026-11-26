import { useCallback, useEffect, useMemo, useState, type KeyboardEvent } from "react";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { isTauri } from "@tauri-apps/api/core";
import { CalendarClock, X } from "lucide-react";
import { Temporal } from "@js-temporal/polyfill";
import { JuButton, JuDataTable, JuDialog, JuInput, JuSelect, JuPresetDateRangePicker, JuOverflowText, JuText } from "../ju";
import { presetDateRange } from "../date-time/date-range-presets";
import type { JuColumn } from "../ju";
import type { TotoHistoryItem } from "../../services/desktop";
import { closeCurrentToolWindow, loadTotoHistory } from "../../services/desktop";
import { useWindowDrag } from "../../hooks/useWindowDrag";
import { useTimeContext } from "../date-time/time-context";

const pageSizes = [10, 20, 50, 100, 200, 500];

export function TotoHistoryWindow() {
    const startWindowDrag = useWindowDrag();
    const appTimeContext = useTimeContext();
    const [now, setNow] = useState(() => Date.now());
    useEffect(() => {
        const timer = window.setInterval(() => setNow(Date.now()), 60_000);
        return () => window.clearInterval(timer);
    }, []);
    const timeContext = { ...appTimeContext, now };
    const currentInstant = typeof timeContext.now === "number"
        ? Temporal.Instant.fromEpochMilliseconds(timeContext.now)
        : Temporal.Instant.from(timeContext.now);
    const today = currentInstant.toZonedDateTimeISO(timeContext.timeZone).toPlainDate();
    const todayValue = today.with({ day: today.daysInMonth }).toString();
    const [items, setItems] = useState<TotoHistoryItem[]>([]);
    const [searchDraft, setSearchDraft] = useState("");
    const [search, setSearch] = useState("");
    const [statusDraft, setStatusDraft] = useState("all");
    const [status, setStatus] = useState("all");
    const [from, setFrom] = useState(() => today.with({ day: 1 }).toString());
    const [to, setTo] = useState(todayValue);
    const [appliedFrom, setAppliedFrom] = useState(() => today.with({ day: 1 }).toString());
    const [appliedTo, setAppliedTo] = useState(todayValue);
    const [page, setPage] = useState(1);
    const [pageSize, setPageSize] = useState(10);
    const [selected, setSelected] = useState<TotoHistoryItem | null>(null);
    const [detailItem, setDetailItem] = useState<TotoHistoryItem | null>(null);
    const [error, setError] = useState("");
    const resetBounds = presetDateRange("本月", timeContext.now, timeContext.timeZone);

    const refresh = useCallback(async () => {
        try {
            const rows = await loadTotoHistory();
            setItems(rows.sort((a, b) => b.ended_at.localeCompare(a.ended_at) || b.created_order - a.created_order));
            setError("");
        } catch (reason) {
            setError(`读取历史事项失败：${String(reason)}`);
        }
    }, [setItems, setError]);

    useEffect(() => {
        void Promise.resolve().then(refresh);
    }, [refresh]);

    const filtered = useMemo(() => items.filter((item) => {
        const term = search.trim().toLocaleLowerCase();
        return (!term || `${item.content} ${item.notes}`.toLocaleLowerCase().includes(term))
            && (status === "all" || item.end_status === status)
            && (!appliedFrom || item.ended_at.slice(0, 10) >= appliedFrom)
            && (!appliedTo || item.ended_at.slice(0, 10) <= appliedTo);
    }), [items, search, status, appliedFrom, appliedTo]);
    const pageCount = Math.max(1, Math.ceil(filtered.length / pageSize));
    const currentPage = Math.min(page, pageCount);
    const rows = useMemo(
        () => filtered.slice((currentPage - 1) * pageSize, currentPage * pageSize),
        [currentPage, filtered, pageSize],
    );
    const selectedRow = rows.find((item) => item.id === selected?.id) ?? rows[0] ?? null;
    const handleHistoryKeys = (event: KeyboardEvent<HTMLDivElement>) => {
        if (event.altKey || event.ctrlKey || event.metaKey || !rows.length) return;
        if (event.key === "ArrowLeft" || event.key === "ArrowRight") {
            event.preventDefault();
            return;
        }
        if (event.key === "ArrowDown" || event.key === "ArrowUp") {
            event.preventDefault();
            const index = rows.findIndex((item) => item.id === selectedRow?.id);
            const delta = event.key === "ArrowDown" ? 1 : -1;
            const nextIndex = index < 0 ? (delta > 0 ? 0 : rows.length - 1) : (index + delta + rows.length) % rows.length;
            setSelected(rows[nextIndex]);
        }
        if (event.key === "Enter" && selectedRow) {
            event.preventDefault();
            setDetailItem(selectedRow);
        }
    };

    const columns: JuColumn<TotoHistoryItem>[] = [
        { id: "content", header: "事项内容", cell: (item) => <JuOverflowText text={item.content} tabIndex={-1} /> },
        { id: "planned", header: "计划时间", cell: (item) => item.planned_at ?? "—" },
        { id: "reminder", header: "提醒时间", cell: (item) => item.reminder_at ?? "—" },
        { id: "ended", header: "结束时间", cell: (item) => item.ended_at },
        { id: "created", header: "创建时间", cell: (item) => item.created_at },
        { id: "status", header: "状态", cell: (item) => item.end_status },
        { id: "notes", header: "备注", cell: (item) => <JuOverflowText text={item.notes || "—"} tabIndex={-1} /> },
    ];

    const runQuery = () => {
        if (from && to && from > to) {
            setError("结束日期的起始值不能晚于结束值。");
            return;
        }
        setSearch(searchDraft);
        setStatus(statusDraft);
        setAppliedFrom(from);
        setAppliedTo(to);
        setPage(1);
        setError("");
    };

    const reset = () => {
        setSearchDraft("");
        setSearch("");
        setStatusDraft("all");
        setStatus("all");
        setFrom(resetBounds?.start ?? "");
        setTo(resetBounds?.end ?? "");
        setAppliedFrom(resetBounds?.start ?? "");
        setAppliedTo(resetBounds?.end ?? "");
        setPage(1);
        setError("");
        void refresh();
    };

    const close = useCallback(async () => {
        if (isTauri() && getCurrentWindow().label === "toto-history") await closeCurrentToolWindow();
        else window.close();
    }, []);

    useEffect(() => {
        const onKeyDown = (event: globalThis.KeyboardEvent) => {
            if (event.key !== "Escape" || event.defaultPrevented || detailItem) return;
            if (event.target instanceof Element && event.target.closest('[role="dialog"], [role="listbox"]')) return;
            if (document.querySelector(".air-datepicker.-active-")) return;
            void close();
        };
        window.addEventListener("keydown", onKeyDown);
        return () => window.removeEventListener("keydown", onKeyDown);
    }, [close, detailItem]);

    return (
        <div className="toto-history-window">
            <header className="toto-tool-header draggable-titlebar" onMouseDown={startWindowDrag}>
                <div className="toto-tool-brand"><CalendarClock size={19} /><strong>ToTo 历史事项</strong></div>
                <JuButton size="compact" variant="ghost" onMouseDown={(event) => event.stopPropagation()} onClick={() => void close()}><X size={16} /> 关闭</JuButton>
            </header>
            <div className="toto-history-filterbar">
                <JuInput label="搜索事项内容或备注" labelHidden value={searchDraft} placeholder="搜索事项内容或备注" onChange={(event) => setSearchDraft(event.currentTarget.value)} onKeyDown={(event) => { if (event.key === "Enter") runQuery(); }} />
                <JuSelect ariaLabel="历史事项状态" value={statusDraft} onValueChange={setStatusDraft} options={[{ value: "all", label: "全部状态" }, { value: "已完成", label: "已完成" }, { value: "已取消", label: "已取消" }]} />
                <JuPresetDateRangePicker
                    {...timeContext}
                    label="结束时间范围"
                    labelHidden
                    presetLabel="历史时间范围预设"
                    value={{ start: from || null, end: to || null }}
                    onChange={(value) => { setFrom(value.start ?? ""); setTo(value.end ?? ""); }}
                />
                <JuButton onClick={runQuery}>查询</JuButton>
                <JuButton variant="outline" onClick={reset}>重置</JuButton>
            </div>
            {error && <p className="settings-error toto-history-error" role="alert">{error}</p>}
            <div className="toto-history-table" onClick={(event) => {
                if (event.target instanceof Element && !event.target.closest(".ju-overflow-text[role='button']")) {
                    event.currentTarget.querySelector<HTMLElement>(".ju-table-container")?.focus({ preventScroll: true });
                }
            }}>
            <JuDataTable
                caption="ToTo 历史事项"
                columns={columns}
                data={rows}
                getRowId={(item) => item.id}
                emptyMessage="暂无符合条件的历史事项"
                selectedRowId={selectedRow?.id}
                onRowClick={setSelected}
                onRowDoubleClick={setDetailItem}
                onKeyDown={handleHistoryKeys}
                tabIndex={-1}
            />
            </div>
            <footer className="toto-history-pagination">
                    <span>共 {filtered.length} 条，第 {currentPage}/{pageCount} 页</span>
                    <div>
                    <JuButton variant="outline" disabled={currentPage <= 1} onClick={() => setPage(1)}>首页</JuButton>
                    <JuButton variant="outline" disabled={currentPage <= 1} onClick={() => setPage(currentPage - 1)}>上一页</JuButton>
                    <JuButton variant="outline" disabled={currentPage >= pageCount} onClick={() => setPage(currentPage + 1)}>下一页</JuButton>
                    <JuButton variant="outline" disabled={currentPage >= pageCount} onClick={() => setPage(pageCount)}>末页</JuButton>
                    <span>每页</span>
                    <JuSelect ariaLabel="每页条数" value={String(pageSize)} onValueChange={(value) => { setPageSize(Number(value)); setPage(1); }} options={pageSizes.map((size) => ({ value: String(size), label: String(size) }))} />
                </div>
            </footer>
            <JuDialog title="详情" className="toto-item-dialog toto-item-dialog--expanded" open={detailItem !== null} onOpenChange={(open) => { if (!open) setDetailItem(null); }} footer={<JuButton onClick={() => setDetailItem(null)}>关闭</JuButton>}>
                {detailItem && <dl className="toto-history-detail">{[["事项内容", detailItem.content], ["计划时间", detailItem.planned_at ?? "—"], ["提醒时间", detailItem.reminder_at ?? "—"], ["创建时间", detailItem.created_at], ["结束状态", detailItem.end_status], ["结束时间", detailItem.ended_at], ["备注", detailItem.notes || "—"]].map(([label, value]) => <div key={label}><dt><JuText>{label}</JuText></dt><dd><JuText>{value}</JuText></dd></div>)}</dl>}
            </JuDialog>
        </div>
    );
}
