import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { isTauri } from "@tauri-apps/api/core";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { CalendarClock, ClipboardList, Plus, RefreshCw, Settings2, X } from "lucide-react";
import {
    JuButton,
    JuCheckbox,
    JuDataTable,
    JuDatePicker,
    JuDialog,
    JuInput,
    JuStatusBar,
    JuTemporalInput,
    JuTextarea,
    type JuColumn,
} from "../ju";
import type { TotoItem, TotoQuickPreview, TotoToolSettings } from "../../services/desktop";
import { useWindowDrag } from "../../hooks/useWindowDrag";
import {
    addTotoQuickItem,
    closeCurrentToolWindow,
    defaultTotoToolSettings,
    finishTotoItemWithNote,
    loadTotoItems,
    loadTotoSettings,
    openSettingsWindow,
    openTotoHistoryWindow,
    previewTotoQuickItem,
    updateTotoItemFull,
} from "../../services/desktop";

type ItemDialogMode = "add" | "edit" | "detail" | "complete" | "cancel" | null;

export function TotoToolWindow() {
    const startWindowDrag = useWindowDrag();
    const mainRef = useRef<HTMLElement | null>(null);
    const [items, setItems] = useState<TotoItem[]>([]);
    const [selectedId, setSelectedId] = useState<string | null>(null);
    const [toolSettings, setToolSettings] = useState<TotoToolSettings>(defaultTotoToolSettings);
    const [mode, setMode] = useState<ItemDialogMode>(null);
    const [target, setTarget] = useState<TotoItem | null>(null);
    const [quickInput, setQuickInput] = useState("");
    const [quickPreview, setQuickPreview] = useState<TotoQuickPreview | null>(null);
    const [content, setContent] = useState("");
    const [usePlan, setUsePlan] = useState(false);
    const [planDate, setPlanDate] = useState("");
    const [planTime, setPlanTime] = useState("");
    const [useReminder, setUseReminder] = useState(false);
    const [reminderDate, setReminderDate] = useState("");
    const [reminderTime, setReminderTime] = useState("");
    const [notes, setNotes] = useState("");
    const [error, setError] = useState("");
    const [notice, setNotice] = useState("");
    const [busy, setBusy] = useState(false);
    const busyRef = useRef(false);
    const selected = items.find((item) => item.id === selectedId) ?? null;

    const refresh = useCallback(async () => {
        try {
            const rows = await loadTotoItems();
            setItems(rows);
            setSelectedId((id) => rows.some((item) => item.id === id) ? id : rows[0]?.id ?? null);
            setError("");
        } catch (reason) {
            setError(`读取 Toto 数据失败：${String(reason)}`);
        }
    }, []);

    useEffect(() => {
        mainRef.current?.focus({ preventScroll: true });
        if (!isTauri()) return;
        let active = true;
        let unlisten: (() => void) | undefined;
        void getCurrentWindow().onFocusChanged(({ payload: focused }) => {
            if (!focused) return;
            mainRef.current?.focus({ preventScroll: true });
            void refresh();
            void loadTotoSettings()
                .then((loaded) => { if (active) setToolSettings(loaded); })
                .catch((reason: unknown) => { if (active) setError(`读取 Toto 设置失败：${String(reason)}`); });
        }).then((stopListening) => {
            if (active) unlisten = stopListening;
            else stopListening();
        });
        return () => {
            active = false;
            unlisten?.();
        };
    }, [refresh]);

    useEffect(() => {
        if (!notice) return;
        const timer = window.setTimeout(() => setNotice(""), 3500);
        return () => window.clearTimeout(timer);
    }, [notice]);

    useEffect(() => {
        let active = true;
        void Promise.all([loadTotoItems(), loadTotoSettings()])
            .then(([rows, loadedSettings]) => {
                if (!active) return;
                setItems(rows);
                setSelectedId(rows[0]?.id ?? null);
                setToolSettings(loadedSettings);
            })
            .catch((reason: unknown) => setError(`读取 Toto 数据失败：${String(reason)}`));
        return () => { active = false; };
    }, [refresh]);

    useEffect(() => {
        if (!quickInput.trim() || mode !== "add") {
            return;
        }
        let active = true;
        const timer = window.setTimeout(() => {
            void previewTotoQuickItem(quickInput)
                .then((preview) => { if (active) { setQuickPreview(preview); setError(""); } })
                .catch((reason: unknown) => { if (active) { setQuickPreview(null); setError(String(reason)); } });
        }, 120);
        return () => { active = false; window.clearTimeout(timer); };
    }, [quickInput, mode]);

    const openAdd = () => {
        setQuickInput("");
        setQuickPreview(null);
        setError("");
        setMode("add");
    };

    const openEdit = (item: TotoItem) => {
        setTarget(item);
        setContent(item.content);
        setUsePlan(item.planned_at !== null);
        setPlanDate(item.planned_at?.slice(0, 10) ?? "");
        setPlanTime(item.planned_at?.slice(11, 16) ?? "");
        setUseReminder(item.reminder_at !== null);
        setReminderDate(item.reminder_at?.slice(0, 10) ?? "");
        setReminderTime(item.reminder_at?.slice(11, 16) ?? "");
        setNotes(item.notes);
        setError("");
        setMode("edit");
    };

    const openEnd = (item: TotoItem, endMode: "complete" | "cancel") => {
        setTarget(item);
        setNotes(item.notes);
        setError("");
        setMode(endMode);
    };

    const save = useCallback(async () => {
        if (busyRef.current) return;
        busyRef.current = true;
        setBusy(true);
        setError("");
        try {
            if (mode === "add") {
                const preview = await previewTotoQuickItem(quickInput);
                setQuickPreview(preview);
                await addTotoQuickItem(quickInput);
                setNotice("事项已新增。");
            } else if (mode === "edit" && target) {
                if (usePlan && (!planDate || !planTime)) {
                    throw new Error("请选择有效计划日期和 24 小时制时间。");
                }
                if (useReminder && (!reminderDate || !reminderTime)) {
                    throw new Error("请选择有效提醒日期和 24 小时制时间。");
                }
                const plannedAt = usePlan && planDate && planTime ? `${planDate} ${planTime}` : null;
                let remindAt = useReminder && reminderDate && reminderTime ? `${reminderDate} ${reminderTime}` : null;
                const originalPlan = target.planned_at?.slice(0, 16) ?? null;
                const originalReminder = target.reminder_at?.slice(0, 16) ?? null;
                if (plannedAt !== originalPlan && remindAt === originalReminder && plannedAt) {
                    const planned = new Date(`${plannedAt.replace(" ", "T")}:00`);
                    planned.setMinutes(planned.getMinutes() - Number(toolSettings.default_remind_minutes ?? 5));
                    remindAt = `${localDateInput(planned)} ${String(planned.getHours()).padStart(2, "0")}:${String(planned.getMinutes()).padStart(2, "0")}`;
                }
                await updateTotoItemFull(target.id, content, plannedAt, remindAt, notes);
                setNotice("事项已更新。");
            } else if ((mode === "complete" || mode === "cancel") && target) {
                await finishTotoItemWithNote(target.id, mode === "complete", notes);
                setNotice(mode === "complete" ? "事项已完成。" : "事项已取消。");
            }
            setMode(null);
            await refresh();
        } catch (reason) {
            setError(String(reason));
        } finally {
            busyRef.current = false;
            setBusy(false);
        }
    }, [content, mode, notes, planDate, planTime, quickInput, refresh, reminderDate, reminderTime, target, toolSettings.default_remind_minutes, usePlan, useReminder]);

    const showDetails = (item: TotoItem) => {
        setTarget(item);
        setMode("detail");
    };

    const close = useCallback(async () => {
        await closeCurrentToolWindow();
    }, []);

    useEffect(() => {
        const onKeyDown = (event: KeyboardEvent) => {
            if (mode) {
                if (event.key === "Escape") {
                    event.preventDefault();
                    setMode(null);
                    return;
                }
                if (event.key === "Enter" && !event.isComposing && !event.shiftKey && !event.altKey
                    && event.target instanceof HTMLInputElement) {
                    event.preventDefault();
                    void save();
                    return;
                }
                if (event.key === "Enter" && !event.isComposing && event.target instanceof HTMLTextAreaElement
                    && (!event.shiftKey && !event.altKey || event.ctrlKey)) {
                    event.preventDefault();
                    void save();
                    return;
                }
                return;
            }
            if (event.key === "Escape") { void close(); return; }
            const shortcut = Object.entries(toolSettings).find(([key, value]) =>
                key.startsWith("shortcut_") && shortcutMatches(String(value), event),
            )?.[0];
            if (!shortcut || event.repeat) return;
            event.preventDefault();
            if (shortcut === "shortcut_add") openAdd();
            else if (shortcut === "shortcut_history") void openTotoHistoryWindow();
            else if (shortcut === "shortcut_refresh") void refresh();
            else if (shortcut === "shortcut_detail" && selected) showDetails(selected);
            else if (shortcut === "shortcut_edit" && selected) openEdit(selected);
            else if (shortcut === "shortcut_complete" && selected) openEnd(selected, "complete");
            else if (shortcut === "shortcut_cancel" && selected) openEnd(selected, "cancel");
        };
        window.addEventListener("keydown", onKeyDown);
        return () => window.removeEventListener("keydown", onKeyDown);
    }, [close, mode, refresh, save, selected, toolSettings]);

    const columns: JuColumn<TotoItem>[] = useMemo(() => [
        { id: "content", header: "事项内容", cell: (item) => item.content },
        { id: "planned", header: "计划时间", cell: (item) => item.planned_at ?? "—" },
        { id: "remind", header: "提醒时间", cell: (item) => item.reminder_at ?? "—" },
        { id: "status", header: "提醒状态", cell: (item) => <span className={`toto-status status-${item.reminder_status === "已提醒" ? "reminded" : item.reminder_status === "未提醒" ? "pending" : "none"}`}>{item.reminder_status}</span> },
        { id: "created", header: "创建时间", cell: (item) => item.created_at },
        { id: "notes", header: "备注", cell: (item) => item.notes || "—" },
    ], []);

    const dialogTitle = mode === "add" ? "新增事项" : mode === "edit" ? "编辑事项" : mode === "complete" ? "完成事项" : mode === "cancel" ? "取消事项" : "事项详情";

    return (
        <div className="toto-tool-window">
            <header className="toto-tool-header draggable-titlebar" onMouseDown={startWindowDrag}>
                <div className="toto-tool-brand"><CalendarClock size={19} /><strong>Toto 日程提醒</strong></div>
                <div className="toto-tool-actions">
                    <JuButton onClick={openAdd}><Plus size={15} /> 新增</JuButton>
                    <JuButton variant="outline" onClick={() => void openTotoHistoryWindow()}><ClipboardList size={15} /> 历史</JuButton>
                    <JuButton variant="outline" onClick={() => void refresh()}><RefreshCw size={15} /> 刷新</JuButton>
                    <JuButton variant="outline" onClick={() => void openSettingsWindow("toto")}><Settings2 size={15} /> 设置</JuButton>
                    <JuButton variant="ghost" onMouseDown={(event) => event.stopPropagation()} onClick={() => void close()}><X size={16} /> 关闭</JuButton>
                </div>
            </header>
            {error && mode === null && <p className="settings-error toto-tool-error" role="alert">{error}</p>}
            <main ref={mainRef} className="toto-main-grid" onKeyDown={(event) => {
                if (event.key === "ArrowLeft" || event.key === "ArrowRight") event.preventDefault();
                if (event.key === "ArrowDown" || event.key === "ArrowUp") {
                    event.preventDefault();
                    if (!items.length) return;
                    const index = items.findIndex((item) => item.id === selectedId);
                    const nextIndex = index < 0
                        ? event.key === "ArrowDown" ? 0 : items.length - 1
                        : (index + (event.key === "ArrowDown" ? 1 : -1) + items.length) % items.length;
                    setSelectedId(items[nextIndex].id);
                }
                if (event.key === "Enter" && selected) showDetails(selected);
            }} tabIndex={0}>
                <JuDataTable
                    caption="Toto 进行中事项"
                    columns={columns}
                    data={items}
                    getRowId={(item) => item.id}
                    emptyMessage="暂无进行中事项，按 Alt+A 新增。"
                    selectedRowId={selectedId}
                    onRowClick={(item) => setSelectedId(item.id)}
                    onRowDoubleClick={showDetails}
                />
            </main>
            <footer className="toto-tool-footer"><JuStatusBar><span role={notice ? "status" : undefined}>{notice || `进行中：${items.length} 项`}</span><span>Alt+A 新增 · Alt+Q 历史 · Alt+R 刷新 · Alt+D 详情 · Alt+E 编辑 · Alt+F 完成 · Alt+C 取消</span></JuStatusBar></footer>
            <JuDialog
                title={dialogTitle}
                description={mode === "add" ? "输入事项内容及计划时间。快速语法可在一行中输入内容@时间[@提醒分钟]。" : "Toto 事项"}
                open={mode !== null}
                onOpenChange={(open) => { if (!open) setMode(null); }}
                footer={mode === "detail" ? <JuButton onClick={() => setMode(null)}>关闭</JuButton> : <><JuButton variant="outline" onClick={() => setMode(null)}>取消</JuButton><JuButton loading={busy} onClick={() => void save()}>{mode === "complete" || mode === "cancel" ? "确认" : mode === "edit" ? "保存" : "新增"}</JuButton></>}
            >
                {mode === "add" && <div className="toto-quick-editor"><JuInput label="事项内容 @计划时间[@提前提醒分钟]" autoFocus value={quickInput} onChange={(event) => { setQuickInput(event.currentTarget.value); setQuickPreview(null); }} placeholder="写周报@1630" /><p className="toto-syntax-help">日期支持 HHmm、ddHHmm、MMddHHmm、yyyyMMddHHmm、+1600、W11600；相对时间支持 H2M30、M45。第三段为提醒提前分钟。</p><div className={`toto-quick-preview${error ? " invalid" : ""}`}>{error || (quickPreview ? `${quickPreview.content} ${quickPreview.plannedAt ?? "无计划时间"}${quickPreview.reminderAt ? ` 提醒 ${quickPreview.reminderAt}` : ""}` : "输入事项内容及计划时间")}</div></div>}
                {mode === "edit" && <div className="toto-edit-form"><JuInput label="事项内容" value={content} onChange={(event) => setContent(event.currentTarget.value)} /><div className="toto-time-pair"><section><JuCheckbox ariaLabel="启用计划时间" label="计划时间" checked={usePlan} onCheckedChange={setUsePlan} /><div className="toto-time-controls"><JuDatePicker label="计划日期" value={planDate} disabled={!usePlan} onChange={setPlanDate} /><JuTemporalInput label="计划时间" type="time" value={planTime} disabled={!usePlan} onChange={setPlanTime} /></div></section><section><JuCheckbox ariaLabel="启用提醒时间" label="提醒时间" checked={useReminder} onCheckedChange={setUseReminder} /><div className="toto-time-controls"><JuDatePicker label="提醒日期" value={reminderDate} disabled={!useReminder} onChange={setReminderDate} /><JuTemporalInput label="提醒时间" type="time" value={reminderTime} disabled={!useReminder} onChange={setReminderTime} /></div></section></div><JuTextarea label="备注" value={notes} onChange={(event) => setNotes(event.currentTarget.value)} /></div>}
                {(mode === "complete" || mode === "cancel") && <div className="toto-end-form"><p>确认{mode === "complete" ? "完成" : "取消"}：{target?.content}</p><JuTextarea label={mode === "complete" ? "完成备注" : "取消备注"} value={notes} onChange={(event) => setNotes(event.currentTarget.value)} /></div>}
                {mode === "detail" && target && <dl className="toto-item-detail">{[["事项内容", target.content], ["计划时间", target.planned_at ?? "—"], ["提醒时间", target.reminder_at ?? "—"], ["创建时间", target.created_at], ["提醒状态", target.reminder_status], ["响铃时间", target.reminded_at ?? "—"], ["备注", target.notes || "—"]].map(([label, value]) => <div key={label}><dt>{label}</dt><dd>{value}</dd></div>)}</dl>}
                {error && mode !== "add" && <p className="settings-error" role="alert">{error}</p>}
            </JuDialog>
        </div>
    );
}

function shortcutMatches(value: string, event: KeyboardEvent): boolean {
    if (!value || value === "None") return false;
    const parts = value.toLowerCase().split("+").map((part) => part.trim());
    const key = parts.pop();
    if (!key || event.key.toLowerCase() !== key.toLowerCase()) return false;
    return parts.includes("alt") === event.altKey
        && parts.includes("ctrl") === event.ctrlKey
        && parts.includes("shift") === event.shiftKey
        && parts.includes("win") === event.metaKey;
}

function localDateInput(date: Date): string {
    return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
}
