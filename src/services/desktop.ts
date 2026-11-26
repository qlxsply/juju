import { enable, disable } from "@tauri-apps/plugin-autostart";
import { open } from "@tauri-apps/plugin-dialog";
import { invoke, isTauri } from "@tauri-apps/api/core";
import { WebviewWindow } from "@tauri-apps/api/webviewWindow";
import { currentMonitor, getCurrentWindow } from "@tauri-apps/api/window";
import { parseTotoQuickEntry } from "./toto-quick-entry";

export interface AppSettings {
    startAtLogin: boolean;
    startMinimized: boolean;
    compactLauncher: boolean;
    autoHideLauncher: boolean;
    theme: "system" | "light" | "dark";
    dataRoot: string;
    pendingDataRoot: string | null;
    showToolNotifications: boolean;
    returnToLauncherOnToolClose: boolean;
    launcherTimeoutMilliseconds: number;
    leaderShortcut: string;
    toolKeys: Record<string, string>;
    toolGlobalShortcuts: Record<string, string>;
    toolSettings: Record<string, Record<string, unknown>>;
}

export interface TimeContext {
    now: string;
    timeZone: string;
    locale: string;
}

export async function getTimeContext(): Promise<TimeContext> {
    if (!isTauri()) {
        throw new Error("浏览器模式必须由示例显式提供 TimeContext。");
    }
    return invoke<TimeContext>("get_time_context");
}

export interface TotoItem {
    id: string;
    content: string;
    planned_at: string | null;
    reminder_at: string | null;
    created_at: string;
    created_order: number;
    reminder_status: string;
    reminded_at: string | null;
    notes: string;
}

export interface TotoHistoryItem extends TotoItem {
    end_status: string;
    ended_at: string;
}

export interface TotoHoliday {
    name: string;
    date: string;
    isOffDay: boolean;
}

export const defaultAppSettings: AppSettings = {
    startAtLogin: false,
    startMinimized: false,
    compactLauncher: false,
    autoHideLauncher: true,
    theme: "system",
    dataRoot: "",
    pendingDataRoot: null,
    showToolNotifications: true,
    returnToLauncherOnToolClose: false,
    launcherTimeoutMilliseconds: 2000,
    leaderShortcut: "Ctrl+Alt+Space",
    toolKeys: {
        json: "J",
        toto: "T",
        echo: "E",
        kairos: "K",
        mysqlschema: "M",
        "design-system": "D",
    },
    toolGlobalShortcuts: {
        json: "None",
        toto: "None",
        echo: "None",
        kairos: "None",
    },
    toolSettings: {},
};

const SETTINGS_KEY = "juju-app-settings";
const browserTasks = new Map<string, number>();
const TOTO_ITEMS_KEY = "juju-toto-items";
const TOTO_HISTORY_KEY = "juju-toto-history";
const TOTO_CONFIG_KEY = "juju-toto-config";
const TOTO_WINDOW_WIDTH = 1200;
// 1200 / φ ≈ 742: enough room for the date-time popup without excess height.
const TOTO_WINDOW_HEIGHT = 742;
const TOTO_HISTORY_WIDTH = 1360;
const TOTO_HISTORY_HEIGHT = Math.round(TOTO_HISTORY_WIDTH / 1.618_033_988_75);

export async function loadAppSettings(): Promise<AppSettings> {
    if (isTauri()) {
        return invoke<AppSettings>("load_app_settings");
    }

    try {
        const saved = localStorage.getItem(SETTINGS_KEY);
        return saved
            ? { ...defaultAppSettings, ...JSON.parse(saved) }
            : defaultAppSettings;
    } catch {
        return defaultAppSettings;
    }
}

export async function saveAppSettings(settings: AppSettings): Promise<void> {
    if (isTauri()) {
        await invoke("save_app_settings", { settings });
    } else {
        localStorage.setItem(SETTINGS_KEY, JSON.stringify(settings, null, 2));
    }
}

export async function chooseDataRoot(): Promise<string | null> {
    if (!isTauri()) return null;
    const selected = await open({
        directory: true,
        multiple: false,
        title: "选择 Juju 数据目录",
    });
    return typeof selected === "string" ? selected : null;
}

export async function setStartAtLogin(enabled: boolean): Promise<void> {
    if (!isTauri()) return;
    if (enabled) await enable();
    else await disable();
}

export async function hideMainWindow(): Promise<void> {
    if (isTauri()) await invoke("hide_main_window");
}

export async function showMainWindow(): Promise<void> {
    if (isTauri()) await invoke("show_main_window");
}

export interface TotoDailyReview {
    kind: "work_start" | "work_end";
    date: string;
    activeItems: TotoItem[];
    finishedItems: TotoHistoryItem[];
}

export async function loadTotoDailyReview(date: string, kind: string): Promise<TotoDailyReview> {
    const review = await invoke<{ kind: "work_start" | "work_end"; date: string; activeItems: Record<string, unknown>[]; finishedItems: Record<string, unknown>[] }>(
        "load_toto_daily_review", { date, kind },
    );
    return { ...review, activeItems: review.activeItems.map(mapNativeTotoItem), finishedItems: review.finishedItems.map(mapNativeTotoHistoryItem) };
}

export interface TotoQuickPreview {
    content: string;
    plannedAt: string | null;
    reminderAt: string | null;
}

export interface JsonDocument {
    id: string;
    fileName: string;
    order: number;
    createdAtUtc: string;
    updatedAtUtc: string;
    source: { kind: string; originalFileName?: string };
}

export interface JsonDocumentSnapshot {
    document: JsonDocument;
    content: string;
    revision: string;
}

export type TotoToolSettings = Record<string, string>;

export const defaultTotoToolSettings: TotoToolSettings = {
    hotkey: "None",
    shortcut_add: "Alt+A",
    shortcut_history: "Alt+Q",
    shortcut_refresh: "Alt+R",
    shortcut_detail: "Alt+D",
    shortcut_edit: "Alt+E",
    shortcut_complete: "Alt+F",
    shortcut_cancel: "Alt+C",
    default_remind_minutes: "5",
    daily_review_start_time: "",
    daily_review_end_time: "",
    daily_review_workdays: "0",
};

export async function openSettingsWindow(section?: string): Promise<void> {
    if (isTauri()) {
        const existing = await WebviewWindow.getByLabel("settings");
        if (existing) {
            await existing.show();
            await existing.setFocus();
            if (section) await invoke("navigate_settings_window", { section });
            return;
        }
        const url = section
            ? `/?window=settings&section=${encodeURIComponent(section)}`
            : "/?window=settings";
        await openTauriWindow("settings", url, "Juju 设置");
    } else {
        const query = section ? `&section=${encodeURIComponent(section)}` : "";
        window.open(`/?window=settings${query}`, "juju-settings", "width=860,height=590");
    }
}

export async function listJsonDocuments(): Promise<JsonDocument[]> {
    if (isTauri()) return invoke<JsonDocument[]>("list_json_documents");
    return readBrowserList<JsonDocument>("juju-json-documents");
}

export async function openJsonDocument(id: string): Promise<JsonDocumentSnapshot> {
    if (isTauri()) return invoke<JsonDocumentSnapshot>("open_json_document", { id });
    const docs = await listJsonDocuments();
    const content = localStorage.getItem(`juju-json-content-${id}`) ?? "{}";
    return { document: docs.find((doc) => doc.id === id)!, content, revision: content };
}

export async function createJsonDocument(): Promise<JsonDocumentSnapshot> {
    if (isTauri()) return invoke<JsonDocumentSnapshot>("create_json_document");
    const docs = await listJsonDocuments();
    const now = new Date().toISOString();
    const document: JsonDocument = {
        id: crypto.randomUUID(),
        fileName: `${now.slice(0, 10).replaceAll("-", "")}001.json`,
        order: 0,
        createdAtUtc: now,
        updatedAtUtc: now,
        source: { kind: "created" },
    };
    const next = [document, ...docs.map((item) => ({ ...item, order: item.order + 1 }))];
    localStorage.setItem("juju-json-documents", JSON.stringify(next));
    localStorage.setItem(`juju-json-content-${document.id}`, "{}");
    return { document, content: "{}", revision: "{}" };
}

export async function saveJsonDocument(
    id: string,
    content: string,
    expectedRevision: string,
): Promise<string> {
    if (isTauri()) {
        return invoke<string>("save_json_document", {
            id,
            content,
            expectedRevision,
        });
    }
    localStorage.setItem(`juju-json-content-${id}`, content);
    return content;
}

export async function overwriteJsonDocument(id: string, content: string): Promise<string> {
    if (isTauri()) return invoke<string>("overwrite_json_document", { id, content });
    localStorage.setItem(`juju-json-content-${id}`, content);
    return content;
}

export async function renameJsonDocument(id: string, name: string): Promise<JsonDocument> {
    if (isTauri()) return invoke<JsonDocument>("rename_json_document", { id, name });
    const docs = await listJsonDocuments();
    const document = docs.find((item) => item.id === id);
    if (!document) throw new Error("文档不存在。");
    document.fileName = name.endsWith(".json") ? name : `${name}.json`;
    localStorage.setItem("juju-json-documents", JSON.stringify(docs));
    return document;
}

export async function deleteJsonDocument(id: string): Promise<void> {
    if (isTauri()) return invoke("delete_json_document", { id });
    localStorage.setItem(
        "juju-json-documents",
        JSON.stringify((await listJsonDocuments()).filter((item) => item.id !== id)),
    );
    localStorage.removeItem(`juju-json-content-${id}`);
}

export async function reorderJsonDocument(id: string, order: number): Promise<JsonDocument[]> {
    if (isTauri()) return invoke<JsonDocument[]>("reorder_json_document", { id, order });
    const docs = await listJsonDocuments();
    const from = docs.findIndex((item) => item.id === id);
    if (from < 0) return docs;
    const [document] = docs.splice(from, 1);
    docs.splice(Math.min(order, docs.length), 0, document);
    docs.forEach((item, index) => (item.order = index));
    localStorage.setItem("juju-json-documents", JSON.stringify(docs));
    return docs;
}

export async function importJsonDocuments(paths: string[]): Promise<JsonDocument[]> {
    if (isTauri()) return invoke<JsonDocument[]>("import_json_documents", { paths });
    const docs = await listJsonDocuments();
    for (const path of paths) {
        const fileName = path.split(/[\\/]/).pop() ?? "import.json";
        docs.push({
            id: crypto.randomUUID(),
            fileName,
            order: docs.length,
            createdAtUtc: new Date().toISOString(),
            updatedAtUtc: new Date().toISOString(),
            source: { kind: "imported", originalFileName: fileName },
        });
    }
    localStorage.setItem("juju-json-documents", JSON.stringify(docs));
    return docs;
}

export async function copyJsonDocumentFile(id: string): Promise<string> {
    if (!isTauri()) throw new Error("浏览器预览不支持 Windows 文件剪贴板。");
    return invoke<string>("copy_json_document_file", { id });
}

export async function chooseJsonImportFiles(): Promise<string[]> {
    if (!isTauri()) return [];
    const selected = await open({
        multiple: true,
        directory: false,
        title: "导入 JSON 文档",
        filters: [{ name: "JSON", extensions: ["json"] }],
    });
    return Array.isArray(selected) ? selected : selected ? [selected] : [];
}

export async function scheduleDemoTask(delayMs: number): Promise<string> {
    if (isTauri()) {
        return invoke<string>("schedule_demo_task", { delayMs });
    }

    const id = `web-${Date.now()}`;
    const timer = window.setTimeout(() => {
        browserTasks.delete(id);
        window.dispatchEvent(
            new CustomEvent("juju:scheduler-task-fired", { detail: id }),
        );
    }, delayMs);
    browserTasks.set(id, timer);
    return id;
}

export async function cancelScheduledTask(taskId: string): Promise<boolean> {
    if (isTauri()) {
        return invoke<boolean>("cancel_scheduled_task", { taskId });
    }
    const timer = browserTasks.get(taskId);
    if (timer === undefined) return false;
    window.clearTimeout(timer);
    browserTasks.delete(taskId);
    return true;
}

export async function openDemoToolWindow(): Promise<void> {
    if (isTauri()) {
        await openTauriWindow(
            "demo-tool",
            "/?window=tool",
            "Juju 工具窗口演示",
        );
    } else {
        window.open("/?window=tool", "juju-demo-tool", "width=640,height=440");
    }
}

export async function openTotoToolWindow(): Promise<void> {
    if (isTauri()) {
        await openTauriWindow("toto", "/?tool=toto", "ToTo 日程");
    } else {
        window.open("/?tool=toto", "juju-toto", `width=${TOTO_WINDOW_WIDTH},height=${TOTO_WINDOW_HEIGHT}`);
    }
}

export async function openTotoHistoryWindow(): Promise<void> {
    if (isTauri()) {
        await openTauriWindow(
            "toto-history",
            "/?tool=toto-history",
            "ToTo 历史事项",
        );
    } else {
        window.open("/?tool=toto-history", "juju-toto-history", `width=${TOTO_HISTORY_WIDTH},height=${TOTO_HISTORY_HEIGHT}`);
    }
}

export async function openTotoReminderWindow(content: string): Promise<void> {
    if (!isTauri()) return;
    const existing = await WebviewWindow.getByLabel("toto-reminder");
    if (existing) {
        await existing.emit("toto:reminder-update", content);
        await existing.show();
        await existing.setFocus();
        return;
    }
    await openTauriWindow(
        "toto-reminder",
        `/?window=toto-reminder&content=${encodeURIComponent(content)}`,
        "Toto 提醒",
    );
}

export async function openToolWindow(
    toolId: string,
    title: string,
): Promise<void> {
    if (isTauri()) {
        await openTauriWindow(
            `tool-${toolId}`,
            `/?tool=${encodeURIComponent(toolId)}`,
            title,
        );
    } else {
        window.open(
            `/?tool=${encodeURIComponent(toolId)}`,
            `juju-${toolId}`,
            "width=900,height=680",
        );
    }
}

export async function closeCurrentToolWindow(): Promise<void> {
    if (!isTauri()) {
        window.close();
        return;
    }
    const current = getCurrentWindow();
    await invoke("close_window_command", { label: current.label });
}

async function openTauriWindow(label: string, url: string, title: string) {
    const jsonSettings = label === "tool-json"
        ? (await loadAppSettings()).toolSettings.json ?? {}
        : undefined;
    const existing = await WebviewWindow.getByLabel(label);
    if (existing) {
        await existing.show();
        await existing.unminimize();
        await existing.setFocus();
        return;
    }
    const rememberedPosition = await invoke<{ x: number; y: number; width: number | null; height: number | null; maximized: boolean } | null>(
            "get_window_position",
            { label },
        ).catch(() => null);
    const monitor = label === "toto" ? await currentMonitor().catch(() => null) : null;
    const availableHeight = monitor && monitor.scaleFactor > 0
        ? Math.floor(monitor.workArea.size.height / monitor.scaleFactor) - 24
        : TOTO_WINDOW_HEIGHT;
    const totoHeight = Math.max(420, Math.min(TOTO_WINDOW_HEIGHT, availableHeight));
    const webviewWindow = new WebviewWindow(label, {
        url,
        title,
        width: rememberedPosition?.width ?? (label === "settings" ? 860 : label === "toto-history" ? TOTO_HISTORY_WIDTH : label === "toto" ? TOTO_WINDOW_WIDTH : label === "tool-json" ? 1200 : label === "tool-design-system" ? 1180 : label === "toto-reminder" ? 520 : 640),
        height: rememberedPosition?.height ?? (label === "settings" ? 590 : label === "toto-history" ? TOTO_HISTORY_HEIGHT : label === "toto" ? totoHeight : label === "tool-json" ? 760 : label === "tool-design-system" ? 820 : label === "toto-reminder" ? 320 : 440),
        minWidth: label === "settings" ? 670 : label === "toto-history" || label === "toto" ? 1200 : label === "tool-json" ? 900 : label === "tool-design-system" ? 960 : label === "toto-reminder" ? 360 : 560,
        minHeight: label === "settings" ? 470 : label === "toto-history" ? 425 : label === "toto" ? 420 : label === "tool-json" ? 600 : label === "tool-design-system" ? 640 : label === "toto-reminder" ? 240 : 360,
        center: !rememberedPosition,
        visible: false,
        x: rememberedPosition?.x,
        y: rememberedPosition?.y,
        resizable: label !== "toto" && label !== "toto-history" && label !== "toto-reminder",
        decorations: ["settings", "tool-json", "toto", "toto-history", "toto-reminder"].includes(label) ? false : undefined,
    });
    await new Promise<void>((resolve, reject) => {
        webviewWindow.once("tauri://created", () => resolve());
        webviewWindow.once("tauri://error", (event) => reject(event.payload));
    });
    if (label === "toto" || (label.startsWith("tool-") && label !== "tool-json")) {
        await webviewWindow.onCloseRequested((event) => {
            event.preventDefault();
            // This listener runs in the window that created the child, so
            // getCurrentWindow() would identify the opener, not the child.
            void invoke("close_window_command", { label }).catch((reason: unknown) => console.error(reason));
        });
    }
    if (label === "tool-json" && isTauri()) {
        await webviewWindow.onCloseRequested((event) => {
            event.preventDefault();
            void invoke("close_window_command", { label }).catch((reason: unknown) => console.error(reason));
        });
    }
    if (rememberedPosition?.maximized || (!rememberedPosition && jsonSettings?.openMaximized === true)) await webviewWindow.maximize();
    await webviewWindow.show();
    await webviewWindow.unminimize();
    await webviewWindow.setFocus();
}

export async function loadTotoItems(): Promise<TotoItem[]> {
    if (isTauri()) {
        const items =
            await invoke<Record<string, unknown>[]>("load_toto_items");
        return items.map(mapNativeTotoItem);
    }
    return readBrowserList<TotoItem>(TOTO_ITEMS_KEY);
}

export async function loadTotoHistory(): Promise<TotoHistoryItem[]> {
    if (isTauri()) {
        const items =
            await invoke<Record<string, unknown>[]>("load_toto_history");
        return items.map(mapNativeTotoHistoryItem);
    }
    return readBrowserList<TotoHistoryItem>(TOTO_HISTORY_KEY);
}

export async function loadTotoCalendar(year: number): Promise<TotoHoliday[]> {
    if (isTauri()) return invoke<TotoHoliday[]>("load_toto_calendar", { year });
    return readBrowserList<TotoHoliday>(`juju-holidays-${year}`);
}

export async function saveTotoCalendar(
    year: number,
    days: TotoHoliday[],
): Promise<void> {
    if (isTauri()) {
        await invoke("save_toto_calendar", { year, days });
    } else {
        localStorage.setItem(
            `juju-holidays-${year}`,
            JSON.stringify(days, null, 2),
        );
    }
}

export async function addTotoItem(
    content: string,
    plannedAt: string | null,
    notes: string,
): Promise<TotoItem> {
    if (isTauri()) {
        const item = await invoke<Record<string, unknown>>("add_toto_item", {
            content,
            plannedAt,
            notes,
        });
        return mapNativeTotoItem(item);
    }
    const items = readBrowserList<TotoItem>(TOTO_ITEMS_KEY);
    const planned = plannedAt ? `${plannedAt}:00` : null;
    const item: TotoItem = {
        id: crypto.randomUUID(),
        content: content.trim(),
        planned_at: planned,
        reminder_at: planned,
        created_at: new Date().toISOString().slice(0, 19).replace("T", " "),
        created_order:
            Math.max(0, ...items.map((row) => row.created_order)) + 1,
        reminder_status: planned ? "未提醒" : "无提醒",
        reminded_at: null,
        notes,
    };
    localStorage.setItem(TOTO_ITEMS_KEY, JSON.stringify([...items, item]));
    return item;
}

export async function addTotoQuickItem(input: string, context: TimeContext): Promise<TotoItem> {
    if (isTauri()) {
        const item = await invoke<Record<string, unknown>>(
            "add_toto_quick_item",
            { input },
        );
        return mapNativeTotoItem(item);
    }
    const parsed = parseTotoQuickEntry(input, context, await loadTotoDefaultRemindMinutes());
    const items = readBrowserList<TotoItem>(TOTO_ITEMS_KEY);
    const item: TotoItem = {
        id: crypto.randomUUID(),
        content: parsed.content,
        planned_at: parsed.plannedAt,
        reminder_at: parsed.reminderAt,
        created_at: new Date().toISOString().slice(0, 19).replace("T", " "),
        created_order: Math.max(0, ...items.map((row) => row.created_order)) + 1,
        reminder_status: parsed.plannedAt ? "未提醒" : "无提醒",
        reminded_at: null,
        notes: "",
    };
    localStorage.setItem(TOTO_ITEMS_KEY, JSON.stringify([...items, item]));
    return item;
}

export async function updateTotoItem(
    id: string,
    content: string,
    plannedAt: string | null,
    notes: string,
): Promise<TotoItem> {
    if (isTauri()) {
        const item = await invoke<Record<string, unknown>>("update_toto_item", {
            id,
            content,
            plannedAt,
            notes,
        });
        return mapNativeTotoItem(item);
    }
    const items = readBrowserList<TotoItem>(TOTO_ITEMS_KEY);
    const original = items.find((item) => item.id === id);
    if (!original) throw new Error("事项不存在。");
    const planned = plannedAt ? `${plannedAt}:00` : null;
    const updated: TotoItem = {
        ...original,
        content: content.trim(),
        notes,
        planned_at: planned,
        reminder_at: planned,
        reminder_status: planned ? "未提醒" : "无提醒",
    };
    localStorage.setItem(
        TOTO_ITEMS_KEY,
        JSON.stringify(items.map((item) => (item.id === id ? updated : item))),
    );
    return updated;
}

export async function downloadTotoCalendar(year: number): Promise<TotoHoliday[]> {
    if (isTauri()) return invoke<TotoHoliday[]>("download_toto_calendar", { year });
    const response = await fetch(`https://raw.githubusercontent.com/NateScarlet/holiday-cn/master/${year}.json`);
    if (!response.ok) throw new Error(`下载节假日数据失败：${response.status}`);
    return response.json() as Promise<TotoHoliday[]>;
}

export async function previewTotoQuickItem(input: string, context: TimeContext): Promise<TotoQuickPreview> {
    if (isTauri()) return invoke<TotoQuickPreview>("preview_toto_quick_item", { input });
    return parseTotoQuickEntry(input, context, await loadTotoDefaultRemindMinutes());
}

export async function updateTotoItemFull(
    id: string,
    content: string,
    plannedAt: string | null,
    reminderAt: string | null,
    notes: string,
): Promise<TotoItem> {
    if (isTauri()) {
        const item = await invoke<Record<string, unknown>>("update_toto_item_full", {
            id,
            content,
            plannedAt,
            reminderAt,
            notes,
        });
        return mapNativeTotoItem(item);
    }
    const items = readBrowserList<TotoItem>(TOTO_ITEMS_KEY);
    const existing = items.find((item) => item.id === id);
    if (!existing) throw new Error("事项不存在或已被外部修改。");
    const planned = plannedAt ? `${plannedAt}:00` : null;
    const reminder = reminderAt ? `${reminderAt}:00` : null;
    const changed = existing.reminder_at !== reminder;
    const updated: TotoItem = {
        ...existing,
        content: content.trim(),
        notes,
        planned_at: planned,
        reminder_at: reminder,
        reminder_status: reminder ? changed ? "未提醒" : existing.reminder_status : "无提醒",
        reminded_at: changed || !reminder ? null : existing.reminded_at,
    };
    localStorage.setItem(TOTO_ITEMS_KEY, JSON.stringify(items.map((item) => item.id === id ? updated : item)));
    return updated;
}

export async function finishTotoItem(
    id: string,
    completed: boolean,
): Promise<void> {
    if (isTauri()) {
        await invoke("finish_toto_item", { id, completed });
        return;
    }
    const items = readBrowserList<TotoItem>(TOTO_ITEMS_KEY);
    const item = items.find((row) => row.id === id);
    if (!item) throw new Error("事项不存在。");
    const history = readBrowserList<TotoHistoryItem>(TOTO_HISTORY_KEY);
    history.push({
        ...item,
        end_status: completed ? "已完成" : "已取消",
        ended_at: new Date().toISOString().slice(0, 19).replace("T", " "),
    });
    localStorage.setItem(TOTO_HISTORY_KEY, JSON.stringify(history));
    localStorage.setItem(
        TOTO_ITEMS_KEY,
        JSON.stringify(items.filter((row) => row.id !== id)),
    );
}

export async function loadTotoDefaultRemindMinutes(): Promise<number> {
    if (isTauri()) return invoke<number>("load_toto_default_remind_minutes");
    try {
        const config = JSON.parse(
            localStorage.getItem(TOTO_CONFIG_KEY) ?? "{}",
        );
        return Number(config.defaultRemindMinutes ?? 5);
    } catch {
        return 5;
    }
}

export async function saveTotoDefaultRemindMinutes(
    minutes: number,
): Promise<void> {
    if (!Number.isInteger(minutes) || minutes < 0) {
        throw new Error("默认提醒提前分钟数必须为非负整数。");
    }
    if (isTauri()) {
        await invoke("save_toto_default_remind_minutes", { minutes });
    } else {
        const config = JSON.parse(
            localStorage.getItem(TOTO_CONFIG_KEY) ?? "{}",
        );
        localStorage.setItem(
            TOTO_CONFIG_KEY,
            JSON.stringify({ ...config, defaultRemindMinutes: minutes }),
        );
    }
}

export async function finishTotoItemWithNote(
    id: string,
    completed: boolean,
    note: string,
): Promise<void> {
    if (isTauri()) {
        await invoke("finish_toto_item_with_note", { id, completed, note });
        return;
    }
    const items = readBrowserList<TotoItem>(TOTO_ITEMS_KEY);
    const item = items.find((row) => row.id === id);
    if (!item) throw new Error("事项不存在或已被外部修改。");
    const history = readBrowserList<TotoHistoryItem>(TOTO_HISTORY_KEY);
    history.push({
        ...item,
        notes: note,
        end_status: completed ? "已完成" : "已取消",
        ended_at: new Date().toISOString().slice(0, 19).replace("T", " "),
    });
    localStorage.setItem(TOTO_HISTORY_KEY, JSON.stringify(history));
    localStorage.setItem(TOTO_ITEMS_KEY, JSON.stringify(items.filter((row) => row.id !== id)));
}

export async function loadTotoSettings(): Promise<TotoToolSettings> {
    if (isTauri()) {
        return {
            ...defaultTotoToolSettings,
            ...(await invoke<TotoToolSettings>("load_toto_settings")),
        };
    }
    try {
        const saved = localStorage.getItem("juju-toto-settings");
        return saved
            ? { ...defaultTotoToolSettings, ...JSON.parse(saved) }
            : defaultTotoToolSettings;
    } catch {
        return defaultTotoToolSettings;
    }
}

export async function saveTotoSettings(
    settings: TotoToolSettings,
): Promise<void> {
    if (isTauri()) {
        await invoke("save_toto_settings", { settings });
    } else {
        localStorage.setItem("juju-toto-settings", JSON.stringify(settings));
    }
}

function readBrowserList<T>(key: string): T[] {
    try {
        return JSON.parse(localStorage.getItem(key) ?? "[]") as T[];
    } catch {
        return [];
    }
}

function mapNativeTotoItem(item: Record<string, unknown>): TotoItem {
    return {
        id: String(item["事项ID"] ?? item.id ?? ""),
        content: String(item["事项内容"] ?? item.content ?? ""),
        planned_at: (item["计划时间"] ?? item.planned_at ?? null) as
            string | null,
        reminder_at: (item["提醒时间"] ?? item.reminder_at ?? null) as
            string | null,
        created_at: String(item["创建时间"] ?? item.created_at ?? ""),
        created_order: Number(item["创建序号"] ?? item.created_order ?? 0),
        reminder_status: String(
            item["提醒状态"] ?? item.reminder_status ?? "无提醒",
        ),
        reminded_at: (item["响铃时间"] ?? item.reminded_at ?? null) as
            string | null,
        notes: String(item["备注"] ?? item.notes ?? ""),
    };
}

function mapNativeTotoHistoryItem(
    item: Record<string, unknown>,
): TotoHistoryItem {
    return {
        ...mapNativeTotoItem(item),
        end_status: String(item["结束状态"] ?? item.end_status ?? ""),
        ended_at: String(item["结束时间"] ?? item.ended_at ?? ""),
    };
}

export { isTauri };
