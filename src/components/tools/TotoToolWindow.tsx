import { useCallback, useEffect, useRef, useState } from "react";
import { isTauri } from "@tauri-apps/api/core";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { CalendarClock, ClipboardList, Plus, RefreshCw, Settings2, X } from "lucide-react";
import { Temporal } from "@js-temporal/polyfill";
import {
    JuButton,
    JuCheckbox,
    JuDateTimePicker,
    JuDialog,
    JuInput,
    JuStatusBar,
    JuText,
    JuTextarea,
} from "../ju";
import { TotoTaskList } from "./TotoTaskList";
import { totoCurrentWallTime, totoPickerValueToWallTime, totoReminderFromPlan, totoWallTimeToPickerValue } from "./toto-date-time";
import type { TotoItem, TotoQuickPreview, TotoToolSettings } from "../../services/desktop";
import { useWindowDrag } from "../../hooks/useWindowDrag";
import { useTimeContext } from "../date-time/time-context";
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

const quickEntryHint = [
    "完整语法：[事项内容] @计划时间@提前提醒分钟",
    "普通内容可不加方括号；内容包含 @ 时使用方括号。",
    "[确认邮箱test@gamil.com] @1600",
    "[[这是事项内容] @1700 → 事项内容以 [ 开头",
    "",
    "绝对时间",
    "HHmm（当天） · ddHHmm（本月）",
    "MMddHHmm（本年） · yyyyMMddHHmm",
    "+HHmm（每个 +：一天，如 ++1600）",
    "W星期HHmm（每个 W：一周；星期 1–7 为周一至周日）",
    "WHHmm（下一周的同一星期）；W 不区分大小写。",
    "",
    "相对时间（H、M 不区分大小写）",
    "H小时M分钟 · H小时 · M分钟",
    "例如 H2M30、h2、m45",
    "",
    "提醒时间",
    "@提前分钟：可省略或留空，使用设置中的默认提前分钟。",
].join("\n");

export function TotoToolWindow() {
    const timeContext = useTimeContext();
    const clockRef = useRef<{ now: string | number; startedAt: number } | null>(null);
    const startWindowDrag = useWindowDrag();
    const mainRef = useRef<HTMLElement | null>(null);
    const initialFocusDone = useRef(false);
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
    const [planValidationError, setPlanValidationError] = useState<string | null>(null);
    const [reminderValidationError, setReminderValidationError] = useState<string | null>(null);
    const [reminderSyncError, setReminderSyncError] = useState("");
    const [reminderPickerVersion, setReminderPickerVersion] = useState(0);
    const [notes, setNotes] = useState("");
    const [error, setError] = useState("");
    const [notice, setNotice] = useState("");
    const [busy, setBusy] = useState(false);
    const busyRef = useRef(false);
    const selected = items.find((item) => item.id === selectedId) ?? null;
    useEffect(() => {
        clockRef.current = { now: timeContext.now, startedAt: performance.now() };
    }, [timeContext.now]);
    useEffect(() => {
        if (initialFocusDone.current || !selectedId || mode) return;
        initialFocusDone.current = true;
        if (document.activeElement === document.body) mainRef.current?.querySelector<HTMLElement>("tr[data-toto-row][tabindex='0']")?.focus({ preventScroll: true });
    }, [selectedId, mode]);

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
        if (!isTauri()) return;
        let active = true;
        let unlisten: (() => void) | undefined;
        void getCurrentWindow().onFocusChanged(({ payload: focused }) => {
            if (!focused) return;
            if (document.activeElement === document.body && !document.querySelector('[role="dialog"]')) {
                mainRef.current?.querySelector<HTMLElement>("tr[data-toto-row][tabindex='0']")?.focus({ preventScroll: true });
            }
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
            void previewTotoQuickItem(quickInput, timeContext)
                .then((preview) => { if (active) { setQuickPreview(preview); setError(""); } })
                .catch((reason: unknown) => { if (active) { setQuickPreview(null); setError(String(reason)); } });
        }, 120);
        return () => { active = false; window.clearTimeout(timer); };
    }, [quickInput, mode, timeContext]);

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
        setPlanValidationError(null);
        setReminderValidationError(null);
        setReminderSyncError("");
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

    const syncReminderFromPlan = (date: string, time: string) => {
        setUseReminder(true);
        setReminderValidationError(null);
        setReminderSyncError("");
        if (reminderValidationError) setReminderPickerVersion((version) => version + 1);
        try {
            const next = totoReminderFromPlan(date, time, Number(toolSettings.default_remind_minutes ?? 5), timeContext.timeZone);
            setReminderDate(next.date);
            setReminderTime(next.time);
        } catch {
            if (!reminderDate || !reminderTime) {
                const clock = clockRef.current;
                const fallback = totoCurrentWallTime({ ...timeContext, now: clock?.now ?? timeContext.now }, clock ? performance.now() - clock.startedAt : 0);
                setReminderDate(fallback.date);
                setReminderTime(fallback.time);
            }
            setReminderSyncError("无法按默认提前分钟计算有效的提醒时间，请手动设置提醒时间。");
        }
    };

    const save = useCallback(async () => {
        if (busyRef.current) return;
        busyRef.current = true;
        setBusy(true);
        setError("");
        try {
            if (mode === "add") {
                const preview = await previewTotoQuickItem(quickInput, timeContext);
                setQuickPreview(preview);
                await addTotoQuickItem(quickInput, timeContext);
                setNotice("事项已新增。");
            } else if (mode === "edit" && target) {
                if (usePlan && planValidationError) throw new Error(`计划时间无效：${planValidationError}`);
                if (useReminder && reminderValidationError) throw new Error(`提醒时间无效：${reminderValidationError}`);
                if (useReminder && reminderSyncError) throw new Error(reminderSyncError);
                if (usePlan && (!planDate || !planTime)) {
                    throw new Error("请选择有效计划日期和 24 小时制时间。");
                }
                if (useReminder && (!reminderDate || !reminderTime)) {
                    throw new Error("请选择有效提醒日期和 24 小时制时间。");
                }
                if (usePlan && planDate && planTime) {
                    Temporal.PlainDateTime.from(`${planDate}T${planTime}`)
                        .toZonedDateTime(timeContext.timeZone, { disambiguation: "reject" });
                }
                if (useReminder && reminderDate && reminderTime) {
                    Temporal.PlainDateTime.from(`${reminderDate}T${reminderTime}`)
                        .toZonedDateTime(timeContext.timeZone, { disambiguation: "reject" });
                }
                const plannedAt = usePlan && planDate && planTime ? `${planDate} ${planTime}` : null;
                const remindAt = useReminder && reminderDate && reminderTime ? `${reminderDate} ${reminderTime}` : null;
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
    }, [content, mode, notes, planDate, planTime, planValidationError, quickInput, refresh, reminderDate, reminderTime, reminderValidationError, reminderSyncError, target, timeContext, usePlan, useReminder]);

    const showDetails = (item: TotoItem) => {
        setTarget(item);
        setMode("detail");
    };

    const close = useCallback(async () => {
        await closeCurrentToolWindow();
    }, []);

    useEffect(() => {
        const onKeyDown = (event: KeyboardEvent) => {
            if (event.defaultPrevented || (event.key === "Escape" && event.target instanceof Element && event.target.closest('[role="dialog"]'))) return;
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

    const dialogTitle = mode === "add" ? "新增" : mode === "edit" ? "编辑" : mode === "complete" ? "完成" : mode === "cancel" ? "取消" : "详情";

    return (
        <div className="toto-tool-window">
            <header className="toto-tool-header draggable-titlebar" onMouseDown={startWindowDrag}>
                <div className="toto-tool-brand"><CalendarClock size={19} /><strong>ToTo 日程</strong></div>
                <div className="toto-tool-actions">
                    <JuButton size="compact" onClick={openAdd}><Plus size={15} /> 新增</JuButton>
                    <JuButton size="compact" variant="outline" onClick={() => void openTotoHistoryWindow()}><ClipboardList size={15} /> 历史</JuButton>
                    <JuButton size="compact" variant="outline" onClick={() => void refresh()}><RefreshCw size={15} /> 刷新</JuButton>
                    <JuButton size="compact" variant="outline" onClick={() => void openSettingsWindow("toto")}><Settings2 size={15} /> 设置</JuButton>
                    <JuButton size="compact" variant="ghost" onMouseDown={(event) => event.stopPropagation()} onClick={() => void close()}><X size={16} /> 关闭</JuButton>
                </div>
            </header>
            {error && mode === null && <p className="settings-error toto-tool-error" role="alert">{error}</p>}
            <main ref={mainRef} className="toto-main-grid">
                <TotoTaskList items={items} selectedId={selectedId}
                    onSelect={(item) => setSelectedId(item.id)} onOpen={showDetails} />
            </main>
            <footer className="toto-tool-footer"><JuStatusBar><span role={notice ? "status" : undefined}>{notice || `进行中：${items.length} 项`}</span><span>Alt+A 新增 · Alt+Q 历史 · Alt+R 刷新 · Alt+D 详情 · Alt+E 编辑 · Alt+F 完成 · Alt+C 取消</span></JuStatusBar></footer>
            <JuDialog
                title={dialogTitle}
                className={`toto-item-dialog${mode === "edit" ? " toto-item-dialog--edit" : mode === "detail" || mode === "complete" || mode === "cancel" ? " toto-item-dialog--expanded" : ""}`}
                open={mode !== null}
                onOpenChange={(open) => { if (!open) setMode(null); }}
                footer={mode === "detail" ? <JuButton onClick={() => setMode(null)}>关闭</JuButton> : <><JuButton variant="outline" onClick={() => setMode(null)}>取消</JuButton><JuButton loading={busy} onClick={() => void save()}>{mode === "complete" || mode === "cancel" ? "确认" : mode === "edit" ? "保存" : "新增"}</JuButton></>}
            >
                {mode === "add" && <div className="toto-quick-editor">
                    <JuInput label="事项内容" hint={quickEntryHint} autoFocus value={quickInput}
                        onChange={(event) => { setQuickInput(event.currentTarget.value); setQuickPreview(null); }}
                        placeholder="写周报 @1630" />
                    <div className={`toto-quick-preview${error ? " invalid" : ""}`}><JuText>{error || (quickPreview ? `${quickPreview.content} ${quickPreview.plannedAt ?? "无计划时间"}${quickPreview.reminderAt ? ` 提醒 ${quickPreview.reminderAt}` : ""}` : "输入事项内容及计划时间")}</JuText></div>
                </div>}
                {mode === "edit" && <div className="toto-edit-form">
                    <JuInput label="事项内容" value={content} onChange={(event) => setContent(event.currentTarget.value)} />
                    <div className="toto-time-pair">
                        <section>
                            <JuCheckbox ariaLabel="启用计划时间" label="计划时间" checked={usePlan} onCheckedChange={(checked) => {
                                setUsePlan(checked);
                                if (checked) {
                                    const clock = clockRef.current;
                                    const next = planDate && planTime
                                        ? { date: planDate, time: planTime }
                                        : totoCurrentWallTime({ ...timeContext, now: clock?.now ?? timeContext.now }, clock ? performance.now() - clock.startedAt : 0);
                                    setPlanDate(next.date);
                                    setPlanTime(next.time);
                                    syncReminderFromPlan(next.date, next.time);
                                }
                                else { setUseReminder(false); setReminderSyncError(""); }
                            }} />
                            <JuDateTimePicker {...timeContext} label="计划时间" labelHidden disabled={!usePlan} required={usePlan}
                                value={totoWallTimeToPickerValue(planDate, planTime, timeContext.timeZone)}
                                onValidationError={setPlanValidationError}
                                onChange={(value) => {
                                    const next = totoPickerValueToWallTime(value, timeContext.timeZone);
                                    setPlanDate(next.date);
                                    setPlanTime(next.time);
                                    syncReminderFromPlan(next.date, next.time);
                                }} />
                        </section>
                        <section>
                            <JuCheckbox ariaLabel="启用提醒时间" label="提醒时间" checked={useReminder} onCheckedChange={(checked) => {
                                if (checked && (!reminderDate || !reminderTime)) {
                                    const clock = clockRef.current;
                                    const next = totoCurrentWallTime({ ...timeContext, now: clock?.now ?? timeContext.now }, clock ? performance.now() - clock.startedAt : 0);
                                    setReminderDate(next.date);
                                    setReminderTime(next.time);
                                    setReminderValidationError(null);
                                    setReminderSyncError("");
                                    if (reminderValidationError) setReminderPickerVersion((version) => version + 1);
                                }
                                setUseReminder(checked);
                            }} />
                            <JuDateTimePicker key={reminderPickerVersion} {...timeContext} label="提醒时间" labelHidden disabled={!useReminder} required={useReminder}
                                value={totoWallTimeToPickerValue(reminderDate, reminderTime, timeContext.timeZone)}
                                onValidationError={setReminderValidationError}
                                onChange={(value) => { const next = totoPickerValueToWallTime(value, timeContext.timeZone); setReminderDate(next.date); setReminderTime(next.time); setReminderSyncError(""); }} />
                            {useReminder && reminderSyncError && <p className="settings-error" role="alert"><JuText>{reminderSyncError}</JuText></p>}
                        </section>
                    </div>
                    <JuTextarea label="备注" value={notes} onChange={(event) => setNotes(event.currentTarget.value)} />
                </div>}
                {(mode === "complete" || mode === "cancel") && <div className="toto-end-form"><p><JuText>确认{mode === "complete" ? "完成" : "取消"}：{target?.content}</JuText></p><JuTextarea label={mode === "complete" ? "完成备注" : "取消备注"} value={notes} onChange={(event) => setNotes(event.currentTarget.value)} /></div>}
                {mode === "detail" && target && <dl className="toto-item-detail">{[["事项内容", target.content], ["计划时间", target.planned_at ?? "—"], ["提醒时间", target.reminder_at ?? "—"], ["创建时间", target.created_at], ["提醒状态", target.reminder_status], ["响铃时间", target.reminded_at ?? "—"], ["备注", target.notes || "—"]].map(([label, value]) => <div key={label}><dt><JuText>{label}</JuText></dt><dd><JuText>{value}</JuText></dd></div>)}</dl>}
                {error && mode !== "add" && <p className="settings-error" role="alert"><JuText>{error}</JuText></p>}
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
