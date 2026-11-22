import { useCallback, useEffect, useMemo, useState, type KeyboardEvent } from "react";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { isTauri } from "@tauri-apps/api/core";
import { CalendarClock } from "lucide-react";
import { JuButton, JuDataTable, JuDatePicker, JuDialog, JuInput, JuSelect } from "../ju";
import type { JuColumn } from "../ju";
import type { TotoHistoryItem } from "../../services/desktop";
import { closeCurrentToolWindow, loadTotoHistory } from "../../services/desktop";
import { useWindowDrag } from "../../hooks/useWindowDrag";

const pageSizes = [10, 20, 50, 100, 200, 500];

export function TotoHistoryWindow() {
    const startWindowDrag = useWindowDrag();
    const today = localDateInput();
    const [items, setItems] = useState<TotoHistoryItem[]>([]);
    const [searchDraft, setSearchDraft] = useState("");
    const [search, setSearch] = useState("");
    const [statusDraft, setStatusDraft] = useState("all");
    const [status, setStatus] = useState("all");
    const [period, setPeriod] = useState("本月");
    const [from, setFrom] = useState(() => `${today.slice(0, 7)}-01`);
    const [to, setTo] = useState(today);
    const [appliedFrom, setAppliedFrom] = useState(() => `${today.slice(0, 7)}-01`);
    const [appliedTo, setAppliedTo] = useState(today);
    const [page, setPage] = useState(1);
    const [pageSize, setPageSize] = useState(10);
    const [selected, setSelected] = useState<TotoHistoryItem | null>(null);
    const [detailItem, setDetailItem] = useState<TotoHistoryItem | null>(null);
    const [error, setError] = useState("");
    const resetBounds = useMemo(() => getPeriodBounds("本月", new Date()), []);

    const refresh = useCallback(async () => {
        try {
            const rows = await loadTotoHistory();
            setItems(rows.sort((a, b) => b.ended_at.localeCompare(a.ended_at) || b.created_order - a.created_order));
            setError("");
        } catch (reason) {
            setError(`读取历史事项失败：${String(reason)}`);
        }
    }, []);

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
        { id: "content", header: "事项内容", cell: (item) => item.content },
        { id: "planned", header: "计划时间", cell: (item) => item.planned_at ?? "—" },
        { id: "reminder", header: "提醒时间", cell: (item) => item.reminder_at ?? "—" },
        { id: "created", header: "创建时间", cell: (item) => item.created_at },
        { id: "notes", header: "备注", cell: (item) => item.notes || "—" },
        { id: "status", header: "结束状态", cell: (item) => item.end_status },
        { id: "ended", header: "结束时间", cell: (item) => item.ended_at },
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

    const selectPeriod = (value: string) => {
        setPeriod(value);
        if (value === "不限") {
            setFrom("");
            setTo("");
            return;
        }
        const bounds = getPeriodBounds(value, new Date());
        if (bounds) {
            setFrom(bounds[0]);
            setTo(bounds[1]);
        }
    };

    const updateDateRange = (nextFrom: string, nextTo: string) => {
        setFrom(nextFrom);
        setTo(nextTo);
        if (!nextFrom && !nextTo) {
            setPeriod("不限");
            return;
        }
        const presets = ["本周", "上一周", "本月", "上一月", "本年", "上一年"];
        const matching = presets.find((preset) => {
            const bounds = getPeriodBounds(preset, new Date());
            return bounds?.[0] === nextFrom && bounds[1] === nextTo;
        });
        setPeriod(matching ?? "自定义");
    };

    const reset = () => {
        setSearchDraft("");
        setSearch("");
        setStatusDraft("all");
        setStatus("all");
        selectPeriod("本月");
        setAppliedFrom(resetBounds?.[0] ?? "");
        setAppliedTo(resetBounds?.[1] ?? "");
        setPage(1);
        setError("");
        void refresh();
    };

    const close = async () => {
        if (isTauri() && getCurrentWindow().label === "toto-history") await closeCurrentToolWindow();
        else window.close();
    };

    return (
        <div className="toto-history-window">
            <header className="toto-tool-header draggable-titlebar" onMouseDown={startWindowDrag}>
                <div className="toto-tool-brand"><CalendarClock size={19} /><strong>Toto</strong><span>历史事项</span></div>
                <JuButton variant="ghost" onMouseDown={(event) => event.stopPropagation()} onClick={() => void close()}>关闭</JuButton>
            </header>
            <div className="toto-history-filterbar">
                <JuInput label="搜索事项内容或备注" labelHidden value={searchDraft} placeholder="搜索事项内容或备注" onChange={(event) => setSearchDraft(event.currentTarget.value)} onKeyDown={(event) => { if (event.key === "Enter") runQuery(); }} />
                <JuSelect ariaLabel="历史事项状态" value={statusDraft} onValueChange={setStatusDraft} options={[{ value: "all", label: "全部状态" }, { value: "已完成", label: "已完成" }, { value: "已取消", label: "已取消" }]} />
                <JuSelect ariaLabel="历史时间范围预设" value={period} onValueChange={selectPeriod} options={["不限", "本周", "上一周", "本月", "上一月", "本年", "上一年", "自定义"].map((label) => ({ value: label, label }))} />
                <JuDatePicker label="结束时间起" labelHidden value={from} onChange={(value) => updateDateRange(value, to)} />
                <span>至</span>
                <JuDatePicker label="结束时间止" labelHidden value={to} onChange={(value) => updateDateRange(from, value)} />
                <JuButton onClick={runQuery}>查询</JuButton>
                <JuButton variant="outline" onClick={reset}>重置</JuButton>
            </div>
            {error && <p className="settings-error toto-history-error" role="alert">{error}</p>}
            <JuDataTable
                caption="Toto 历史事项"
                columns={columns}
                data={rows}
                getRowId={(item) => item.id}
                emptyMessage="暂无符合条件的历史事项"
                selectedRowId={selectedRow?.id}
                onRowClick={setSelected}
                onRowDoubleClick={setDetailItem}
                onKeyDown={handleHistoryKeys}
            />
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
            <JuDialog title={detailItem?.end_status === "已完成" ? "已完成事项详情" : "已取消事项详情"} description="历史事项为只读记录。" open={detailItem !== null} onOpenChange={(open) => { if (!open) setDetailItem(null); }} footer={<JuButton onClick={() => setDetailItem(null)}>关闭</JuButton>}>
                {detailItem && <dl className="toto-history-detail">{[["事项内容", detailItem.content], ["计划时间", detailItem.planned_at ?? "—"], ["提醒时间", detailItem.reminder_at ?? "—"], ["创建时间", detailItem.created_at], ["结束状态", detailItem.end_status], ["结束时间", detailItem.ended_at], ["备注", detailItem.notes || "—"]].map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}</dl>}
            </JuDialog>
        </div>
    );
}

function getPeriodBounds(period: string, now: Date): [string, string] | null {
    const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    const monday = new Date(today);
    monday.setDate(today.getDate() - ((today.getDay() + 6) % 7));
    const monthStart = new Date(today.getFullYear(), today.getMonth(), 1);
    const yearStart = new Date(today.getFullYear(), 0, 1);
    const bounds: Record<string, [Date, Date]> = {
        本周: [monday, addDays(monday, 6)],
        上一周: [addDays(monday, -7), addDays(monday, -1)],
        本月: [monthStart, addDays(new Date(today.getFullYear(), today.getMonth() + 1, 1), -1)],
        上一月: [new Date(today.getFullYear(), today.getMonth() - 1, 1), addDays(monthStart, -1)],
        本年: [yearStart, new Date(today.getFullYear(), 11, 31)],
        上一年: [new Date(today.getFullYear() - 1, 0, 1), new Date(today.getFullYear() - 1, 11, 31)],
    };
    return bounds[period] ? [localDateInput(bounds[period][0]), localDateInput(bounds[period][1])] : null;
}

function addDays(date: Date, days: number): Date {
    const result = new Date(date);
    result.setDate(result.getDate() + days);
    return result;
}

function localDateInput(date = new Date()): string {
    return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
}
