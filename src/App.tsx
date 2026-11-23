import { lazy, Suspense, useCallback, useEffect, useState, type CSSProperties } from "react";
import { emitTo, listen } from "@tauri-apps/api/event";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { LogicalSize } from "@tauri-apps/api/dpi";
import { Temporal } from "@js-temporal/polyfill";
import {
    ArrowLeftRight,
    Clock3,
    Database,
    Ellipsis,
    Palette,
    Plus,
    Save,
    Settings as SettingsIcon,
} from "lucide-react";
import jujuIcon from "../src-tauri/icons/icon.ico";
import {
    JuBadge,
    JuButton,
    JuCheckbox,
    JuConfirmDialog,
    JuDatePicker,
    JuDateTimePicker,
    JuDataTable,
    type JuColumn,
    JuDialog,
    JuDialogClose,
    JuEmptyState,
    JuInput,
    JuIconButton,
    JuLoading,
    JuPagination,
    JuRadioGroup,
    JuSearchInput,
    JuSelect,
    JuSeparator,
    JuStatusBar,
    JuSwitch,
    JuTimePicker,
    JuTabs,
    JuTextarea,
    JuToast,
    JuToolbar,
    JuTooltip,
} from "./components/ju";
import { useThemeStore } from "./design-system/theme-store";
import { designTokens, tokenCategories } from "./design-system/tokens";
import { parsePickerInput } from "./components/date-time/direct-input";
import { resolvePickerFormat } from "./components/date-time/picker-format";
import { TimeContextProvider } from "./components/date-time/time-context-provider";
import { useTimeContext } from "./components/date-time/time-context";
import { SettingsPage as ReferenceSettingsPage } from "./components/shell/SettingsPage";
import { TotoToolWindow } from "./components/tools/TotoToolWindow";
import { TotoHistoryWindow } from "./components/tools/TotoHistoryWindow";
import { TotoReminderWindow } from "./components/tools/TotoReminderWindow";
import { useWindowDrag } from "./hooks/useWindowDrag";
import {
    chooseDataRoot,
    isTauri,
    hideMainWindow,
    loadAppSettings,
    loadTotoHistory,
    loadTotoItems,
    loadTotoDefaultRemindMinutes,
    closeCurrentToolWindow,
    openToolWindow,
    openTotoToolWindow,
    openTotoReminderWindow,
    saveAppSettings,
    setStartAtLogin,
    addTotoItem,
    addTotoQuickItem,
    finishTotoItem,
    openSettingsWindow,
    saveTotoDefaultRemindMinutes,
    showMainWindow,
    updateTotoItem,
    type AppSettings,
    type TotoDailyReview,
    type TotoHistoryItem,
    type TotoItem,
    defaultAppSettings,
} from "./services/desktop";

const JsonToolWindow = lazy(() =>
    import("./components/tools/JsonToolWindow").then((module) => ({
        default: module.JsonToolWindow,
    })),
);

interface DemoRow {
    name: string;
    category: string;
    status: "Ready" | "Planned";
}

const demoRows: DemoRow[] = [
    { name: "Juju 设计系统", category: "基础能力", status: "Ready" },
    { name: "Toto 提醒", category: "工具", status: "Planned" },
    { name: "JSON 文档", category: "工具", status: "Planned" },
];

const demoColumns: JuColumn<DemoRow>[] = [
    { id: "name", header: "名称", cell: (row) => row.name },
    { id: "category", header: "分类", cell: (row) => row.category },
    {
        id: "status",
        header: "状态",
        cell: (row) => <JuBadge variant={row.status === "Ready" ? "success" : "muted"}>{row.status === "Ready" ? "可用" : "计划中"}</JuBadge>,
    },
];

export function App() {
    return (
        <TimeContextProvider>
            <AppContent />
        </TimeContextProvider>
    );
}

function formatDemoWallTime(instant: string, timeZone: string, locale: string): string {
    const value = Temporal.Instant.from(instant).toZonedDateTimeISO(timeZone);
    const pad = (part: number) => String(part).padStart(2, "0");
    if (locale === "en-US") {
        const hour = value.hour % 12 || 12;
        return `${pad(value.month)}/${pad(value.day)}/${value.year} ${pad(hour)}:${pad(value.minute)} ${value.hour < 12 ? "AM" : "PM"}`;
    }
    return `${value.year}-${pad(value.month)}-${pad(value.day)} ${pad(value.hour)}:${pad(value.minute)}`;
}

function parseDemoWallTime(input: string, timeZone: string, locale: string): string {
    const formats = resolvePickerFormat({ mode: "datetime", locale });
    const fields = parsePickerInput(input, formats.inputFormat, "datetime", locale);
    return Temporal.PlainDateTime.from(fields)
        .toZonedDateTime(timeZone, { disambiguation: "reject" })
        .toInstant()
        .toString();
}

function AppContent() {
    const initialTool = new URLSearchParams(window.location.search).get("tool");
    const nativeLabel = isTauri() ? getCurrentWindow().label : "";
    const [view, setView] = useState(() => {
        if (
            nativeLabel === "settings" ||
            new URLSearchParams(window.location.search).get("window") ===
                "settings"
        ) {
            return "settings";
        }
        if (new URLSearchParams(window.location.search).get("window") === "toto-reminder") {
            return "toto-reminder";
        }
        if (nativeLabel === "toto-history" || initialTool === "toto-history") {
            return "toto-history";
        }
        if (nativeLabel === "toto" || initialTool === "toto") return "toto";
        if (nativeLabel.startsWith("tool-") || initialTool) {
            const toolId = nativeLabel.startsWith("tool-")
                ? nativeLabel.slice("tool-".length)
                : initialTool;
            return toolId === "design-system" ? "showcase" : `tool:${toolId}`;
        }
        if (
            new URLSearchParams(window.location.search).get("window") === "tool"
        ) {
            return "tool";
        }
        return "launcher";
    });
    const [notice, setNotice] = useState("");
    const [settingsWindowOpen, setSettingsWindowOpen] = useState(false);
    const [dailyReviews, setDailyReviews] = useState<TotoDailyReview[]>([]);
    const [appSettings, setAppSettings] =
        useState<AppSettings>(defaultAppSettings);

    const launchTool = useCallback(async (toolId: string) => {
        const tool = toolId.toLowerCase();
        const titles: Record<string, string> = {
            json: "JSON 格式化",
            echo: "EchoHttp",
            echohttp: "EchoHttp",
            kairos: "Kairos",
            mysqlschema: "MySQL Schema",
            "design-system": "设计系统工作台",
        };
        try {
            if (isTauri()) await hideMainWindow();
            if (tool === "toto") await openTotoToolWindow();
            else await openToolWindow(tool, titles[tool] ?? tool);
        } catch (reason) {
            setNotice(`无法打开 ${toolId}：${String(reason)}`);
            if (isTauri()) await showMainWindow().catch(() => undefined);
        }
    }, []);

    useEffect(() => {
        let active = true;
        void loadAppSettings()
            .then((settings) => {
                if (active) {
                    setAppSettings(settings);
                    useThemeStore.getState().setMode(settings.theme);
                }
            })
            .catch((error: unknown) => {
                if (active) setNotice(`读取应用设置失败：${String(error)}`);
            });
        return () => {
            active = false;
        };
    }, []);

    useEffect(() => {
        if (!isTauri() || getCurrentWindow().label !== "main") return;
        const size =
            view === "settings"
                ? new LogicalSize(860, 590)
                : view === "showcase"
                  ? new LogicalSize(1040, 760)
                  : new LogicalSize(750, 398);
        void getCurrentWindow()
            .setSize(size)
            .catch(() => undefined);
        void getCurrentWindow()
            .setResizable(view === "settings")
            .catch(() => undefined);
    }, [view]);

    useEffect(() => {
        if (!isTauri()) return;
        let active = true;
        let unlisten: (() => void) | undefined;
        void listen<AppSettings>("app:settings-changed", (event) => {
            if (!active) return;
            setAppSettings(event.payload);
            useThemeStore.getState().setMode(event.payload.theme);
        }).then((stopListening) => {
            if (active) unlisten = stopListening;
            else stopListening();
        });
        return () => {
            active = false;
            unlisten?.();
        };
    }, []);

    useEffect(() => {
        if (!isTauri() || nativeLabel !== "settings") return;
        let active = true;
        let unlisten: (() => void) | undefined;
        void getCurrentWindow().onCloseRequested(() => {
            void emitTo("main", "app:settings-closed", undefined);
        }).then((stopListening) => {
            if (active) unlisten = stopListening;
            else stopListening();
        });
        return () => { active = false; unlisten?.(); };
    }, [nativeLabel]);

    useEffect(() => {
        if (view === "tool") return;
        let active = true;
        let unlistenNavigate: (() => void) | undefined;
        let unlistenTotoReminder: (() => void) | undefined;
        let unlistenOpenTotoReminder: (() => void) | undefined;
        let unlistenStorageError: (() => void) | undefined;
        let unlistenOpenTool: (() => void) | undefined;
        let unlistenOpenSettings: (() => void) | undefined;
        let unlistenSettingsClosed: (() => void) | undefined;
        let unlistenDailyReview: (() => void) | undefined;

        if (isTauri()) {
            void listen<string>("app:navigate", (event) => {
                if (event.payload === "settings") setView("settings");
                if (event.payload === "launcher") setView("launcher");
            }).then((unlisten) => {
                if (active) unlistenNavigate = unlisten;
                else unlisten();
            });
            void listen<string>("toto:reminder", (event) => {
                setNotice(`Toto 提醒：${event.payload}`);
            }).then((unlisten) => {
                if (active) unlistenTotoReminder = unlisten;
                else unlisten();
            });
            void listen<string>("app:storage-error", (event) => {
                setNotice(`无法应用新的数据目录：${event.payload}`);
            }).then((unlisten) => {
                if (active) unlistenStorageError = unlisten;
                else unlisten();
            });
            if (getCurrentWindow().label === "main") {
                void listen<string>("toto:reminder", (event) => {
                    void openTotoReminderWindow(event.payload).catch((error: unknown) =>
                        setNotice(`无法打开 Toto 提醒窗口：${String(error)}`),
                    );
                }).then((unlisten) => {
                    if (active) unlistenOpenTotoReminder = unlisten;
                    else unlisten();
                });
                void listen<TotoDailyReview>("toto:daily-review", (event) => {
                    setDailyReviews((queue) => [...queue, event.payload]);
                    void showMainWindow();
                }).then((unlisten) => {
                    if (active) unlistenDailyReview = unlisten;
                    else unlisten();
                });
                void listen("app:open-settings", () => {
                    setSettingsWindowOpen(true);
                    window.dispatchEvent(new CustomEvent("juju:suspend-launcher-autohide", { detail: true }));
                    void openSettingsWindow().catch((error: unknown) =>
                        {
                            setSettingsWindowOpen(false);
                            window.dispatchEvent(new CustomEvent("juju:suspend-launcher-autohide", { detail: false }));
                            setNotice(`无法打开设置窗口：${String(error)}`);
                        },
                    );
                }).then((unlisten) => {
                    if (active) unlistenOpenSettings = unlisten;
                    else unlisten();
                });
                void listen<string>("app:open-tool", (event) => {
                    void launchTool(event.payload);
                }).then((unlisten) => {
                    if (active) unlistenOpenTool = unlisten;
                    else unlisten();
                });
                void listen("app:settings-closed", () => {
                    setSettingsWindowOpen(false);
                    window.dispatchEvent(new CustomEvent("juju:suspend-launcher-autohide", { detail: false }));
                }).then((unlisten) => {
                    if (active) unlistenSettingsClosed = unlisten;
                    else unlisten();
                });
            }
        }

        return () => {
            active = false;
            unlistenNavigate?.();
            unlistenTotoReminder?.();
            unlistenOpenTotoReminder?.();
            unlistenStorageError?.();
            unlistenOpenTool?.();
            unlistenOpenSettings?.();
            unlistenSettingsClosed?.();
            unlistenDailyReview?.();
        };
    }, [launchTool, view]);

    if (view === "tool") return <DemoToolWindow />;
    if (view === "tool:json") return <Suspense fallback={<div className="tool-loading">正在加载 JSON 编辑器…</div>}><JsonToolWindow settings={appSettings} /></Suspense>;
    if (view === "toto-history") return <TotoHistoryWindow />;
    if (view === "toto-reminder") return <TotoReminderWindow />;
    if (view.startsWith("tool:"))
        return <PlaceholderToolWindow toolId={view.slice(5)} />;
    if (view === "toto") return <TotoToolWindow />;
    if (view === "showcase")
        return <DesignSystemShowcase />;
    if (view === "settings") {
        return (
            <ReferenceSettingsPage
                settings={appSettings}
                onBack={() => {
                    if (nativeLabel === "settings") {
                        void closeCurrentToolWindow().catch((error: unknown) =>
                            setNotice(`无法关闭设置窗口：${String(error)}`),
                        );
                    } else {
                        setView("launcher");
                    }
                }}
                onSettingsChanged={setAppSettings}
                onMessage={setNotice}
            />
        );
    }

    return (
        <>
          <Launcher
              settings={appSettings}
              notice={notice}
              suppressAutoHide={settingsWindowOpen}
              onOpenSettings={() => {
                if (isTauri()) {
                    setSettingsWindowOpen(true);
                    window.dispatchEvent(new CustomEvent("juju:suspend-launcher-autohide", { detail: true }));
                    void openSettingsWindow().catch((error: unknown) =>
                        {
                            setSettingsWindowOpen(false);
                            window.dispatchEvent(new CustomEvent("juju:suspend-launcher-autohide", { detail: false }));
                            setNotice(`无法打开设置窗口：${String(error)}`);
                        },
                    );
                } else {
                    setView("settings");
                }
            }}
              onSelectTool={launchTool}
              onDismissNotice={() => setNotice("")}
          />
          <TotoDailyReviewDialog review={dailyReviews[0] ?? null} onClose={() => setDailyReviews((queue) => queue.slice(1))} />
        </>
    );
}

function TotoDailyReviewDialog({
    review,
    onClose,
}: {
    review: TotoDailyReview | null;
    onClose: () => void;
}) {
    return (
        <JuDialog
            title={review?.kind === "work_start" ? "开始盘点" : "结束复盘"}
            description={review?.date ?? ""}
            open={review !== null}
            onOpenChange={(open) => { if (!open) onClose(); }}
            footer={<JuButton onClick={onClose}>关闭</JuButton>}
        >
            {review && (
                <div className="toto-review-content">
                    <h3>进行中事项</h3>
                    {review.activeItems.length ? <ul>{review.activeItems.map((item) => <li key={item.id}>{item.content}{item.planned_at ? ` — ${item.planned_at}` : ""}</li>)}</ul> : <p>暂无进行中事项。</p>}
                    {review.kind === "work_end" && <><h3>今日已完成</h3>{review.finishedItems.length ? <ul>{review.finishedItems.map((item) => <li key={item.id}>{item.content}</li>)}</ul> : <p>今日暂无已完成事项。</p>}</>}
                </div>
            )}
        </JuDialog>
    );
}

function DesignSystemShowcase() {
    const [showToast, setShowToast] = useState(false);
    const [category, setCategory] = useState("general");
    const [searchText, setSearchText] = useState("");
    const [newsletter, setNewsletter] = useState(true);
    const [notifications, setNotifications] = useState(false);
    const [preferredMode, setPreferredMode] = useState("desktop");
    const [selectedDate, setSelectedDate] = useState("2026-11-21");
    const [selectedTime, setSelectedTime] = useState("09:30");
    const [selectedDateTime, setSelectedDateTime] = useState("2026-11-21T01:30:00Z");
    const [dstPolicy, setDstPolicy] = useState<"reject" | "earlier" | "later">("reject");
    const appTimeContext = useTimeContext();
    const [demoTimeZone, setDemoTimeZone] = useState(appTimeContext.timeZone);
    const [demoLocale, setDemoLocale] = useState(appTimeContext.locale);
    const [demoNow, setDemoNow] = useState(() => Temporal.Now.instant().toString());
    const [demoTimeInput, setDemoTimeInput] = useState(() => formatDemoWallTime(Temporal.Now.instant().toString(), appTimeContext.timeZone, appTimeContext.locale));
    const [demoTimeError, setDemoTimeError] = useState("");
    const demoTimeContext = { now: demoNow, timeZone: demoTimeZone, locale: demoLocale };
    const [page, setPage] = useState(1);
    const mode = useThemeStore((state) => state.mode);
    const setMode = useThemeStore((state) => state.setMode);

    useEffect(() => {
        if (!isTauri()) return;
        let disposed = false;
        const logo = document.querySelector<SVGSVGElement>(".design-system-tool-icon");
        if (!logo) return;
        void createToolWindowIcon(logo)
            .then((icon) => {
                if (!disposed) return getCurrentWindow().setIcon(icon);
            })
            .catch(() => undefined);
        return () => { disposed = true; };
    }, []);

    const showNotification = () => {
        setShowToast(true);
        window.setTimeout(() => setShowToast(false), 2500);
    };

    return (
        <div className="showcase-shell design-system-workbench">
            <Palette className="design-system-tool-icon sr-only" size={20} aria-hidden="true" />

            <div className="showcase-body">
                <main className="showcase-content">
                    <section className="showcase-section" id="design-tokens">
                        <div className="token-heading-row">
                            <SectionHeading
                                title="Design Tokens"
                                description="颜色、字体、尺寸、形状、状态和动效由令牌统一定义，组件与页面均引用同一套规范。"
                            />
                        </div>
                        <div aria-label="工作环境设置" className="token-environment-settings">
                            <div className="token-environment-heading">
                                <strong>工作环境</strong>
                                <span>下方组件示例共用这些设置；本地时间会按语言解析，并结合时区转换为绝对时间。</span>
                            </div>
                            <div className="token-environment-controls">
                                <JuSelect
                                    label="主题模式"
                                    value={mode}
                                    onValueChange={(value) => setMode(value as typeof mode)}
                                    options={[
                                        { value: "system", label: "跟随系统" },
                                        { value: "light", label: "浅色" },
                                        { value: "dark", label: "深色" },
                                    ]}
                                />
                                <JuSelect
                                    label="业务时区"
                                    value={demoTimeZone}
                                    onValueChange={(nextZone) => {
                                        setDemoTimeZone(nextZone);
                                        try {
                                            setDemoNow(parseDemoWallTime(demoTimeInput, nextZone, demoLocale));
                                            setDemoTimeError("");
                                        } catch (error) {
                                            setDemoTimeError(error instanceof Error ? error.message : String(error));
                                        }
                                    }}
                                    options={[
                                        { value: "Asia/Shanghai", label: "Asia/Shanghai" },
                                        { value: "America/New_York", label: "America/New_York" },
                                        { value: "UTC", label: "UTC" },
                                    ]}
                                />
                                <JuSelect
                                    label="日历语言"
                                    value={demoLocale}
                                    onValueChange={(nextLocale) => {
                                        setDemoLocale(nextLocale);
                                        setDemoTimeInput(formatDemoWallTime(demoNow, demoTimeZone, nextLocale));
                                        setDemoTimeError("");
                                    }}
                                    options={[
                                        { value: "zh-CN", label: "简体中文" },
                                        { value: "en-US", label: "English (US)" },
                                    ]}
                                />
                                <JuInput
                                    label="示例时间（本地）"
                                    value={demoTimeInput}
                                    error={demoTimeError || undefined}
                                    placeholder={resolvePickerFormat({ mode: "datetime", locale: demoLocale }).inputFormat}
                                    onChange={(event) => {
                                        const nextInput = event.currentTarget.value;
                                        setDemoTimeInput(nextInput);
                                        try {
                                            setDemoNow(parseDemoWallTime(nextInput, demoTimeZone, demoLocale));
                                            setDemoTimeError("");
                                        } catch {
                                            // Keep editing text; validate and report a complete value on blur.
                                        }
                                    }}
                                    onBlur={() => {
                                        try {
                                            setDemoNow(parseDemoWallTime(demoTimeInput, demoTimeZone, demoLocale));
                                            setDemoTimeError("");
                                        } catch (error) {
                                            setDemoTimeError(error instanceof Error ? error.message : String(error));
                                        }
                                    }}
                                />
                            </div>
                            <p className="date-time-demo-output">传入组件的 now（绝对时间，UTC）：{demoNow}</p>
                        </div>
                        <div className="token-category-list">
                            {tokenCategories.map((tokenCategory) => (
                                <section className="token-category" key={tokenCategory}>
                                    <h3>{tokenCategory}</h3>
                                    <div className="token-grid">
                                        {designTokens.filter((token) => token.category === tokenCategory).map((token) => (
                                            <TokenSwatch key={token.variable} {...token} />
                                        ))}
                                    </div>
                                </section>
                            ))}
                        </div>
                        <p className="muted-copy theme-hint">
                            当前主题：{mode === "system" ? "跟随系统" : mode === "light" ? "浅色" : "深色"}
                        </p>
                    </section>

                    <section className="showcase-section" id="buttons">
                        <SectionHeading title="按钮与状态" description="主要、次要、危险和禁用状态。" />
                        <div className="component-card">
                            <div className="demo-row">
                                <JuButton>主要操作</JuButton>
                                <JuButton variant="secondary">次要操作</JuButton>
                                <JuButton variant="outline">描边操作</JuButton>
                                <JuButton variant="ghost">轻量操作</JuButton>
                                <JuButton variant="destructive">危险操作</JuButton>
                                <JuButton size="large">大尺寸</JuButton>
                                <JuButton disabled>不可用</JuButton>
                                <JuButton><Plus size={16} />带图标操作</JuButton>
                                <JuButton variant="outline"><Save size={16} />保存</JuButton>
                                <JuTooltip content="更多操作">
                                    <JuIconButton label="更多操作" size="compact"><Ellipsis size={16} aria-hidden="true" /></JuIconButton>
                                </JuTooltip>
                                <JuButton loading>正在处理</JuButton>
                            </div>
                            <div className="demo-row">
                                <JuBadge variant="success">成功</JuBadge>
                                <JuBadge variant="warning">提醒</JuBadge>
                                <JuBadge variant="destructive">错误</JuBadge>
                                <JuBadge>中性</JuBadge>
                                <JuLoading label="组件加载状态" />
                            </div>
                        </div>
                    </section>

                    <section className="showcase-section" id="forms">
                        <SectionHeading title="表单组件" description="输入、选择、日期和状态控制。" />
                        <div className="component-card form-grid">
                            <JuInput label="名称" placeholder="输入名称" hint="名称仅用于本地显示" />
                            <JuInput label="只读字段" value="Juju Desktop" readOnly />
                            <JuSelect
                                label="工具分类"
                                value={category}
                                onValueChange={setCategory}
                                options={[
                                    { value: "general", label: "通用工具" },
                                    { value: "data", label: "数据工具" },
                                ]}
                            />
                            <JuDatePicker {...demoTimeContext} label="计划日期" value={selectedDate} onChange={(next) => setSelectedDate(next ?? "")} />
                            <JuTimePicker {...demoTimeContext} label="计划时间" value={selectedTime} onChange={(next) => setSelectedTime(next ?? "")} />
                            <JuDateTimePicker {...demoTimeContext} label="日期和时间" value={selectedDateTime} onChange={(next) => setSelectedDateTime(next ?? "")} />
                            <JuDateTimePicker {...demoTimeContext} label="now 默认值" />
                            <JuDateTimePicker
                                {...demoTimeContext}
                                label="DST 重复时间策略演示"
                                value="2026-11-01T05:30:00Z"
                                disambiguation={dstPolicy}
                            />
                            <JuSelect
                                label="重复时间处理"
                                value={dstPolicy}
                                onValueChange={(value) => setDstPolicy(value as typeof dstPolicy)}
                                options={[
                                    { value: "reject", label: "拒绝歧义时间" },
                                    { value: "earlier", label: "选择较早时刻" },
                                    { value: "later", label: "选择较晚时刻" },
                                ]}
                            />
                            <JuDatePicker {...demoTimeContext} label="日期范围限制" value="2026-11-21" min="2026-11-01" max="2026-11-30" />
                            <JuTimePicker {...demoTimeContext} label="时间范围与步长" value="09:30" min="08:00" max="18:00" minuteStep={5} />
                            <JuDatePicker {...demoTimeContext} label="禁用日期组件" value="2026-11-21" disabled />
                            <JuDateTimePicker {...demoTimeContext} label="错误状态示例" value="invalid-date-time" />
                            <JuDateTimePicker
                                {...demoTimeContext}
                                label="DST 重复时间示例"
                                value="2026-11-01T05:30:00Z"
                                disambiguation={dstPolicy}
                            />
                            <JuSelect
                                label="重复时间策略"
                                value={dstPolicy}
                                onValueChange={(value) => setDstPolicy(value as typeof dstPolicy)}
                                options={[
                                    { value: "reject", label: "拒绝歧义时间" },
                                    { value: "earlier", label: "选择较早时刻" },
                                    { value: "later", label: "选择较晚时刻" },
                                ]}
                            />
                            <JuDatePicker {...demoTimeContext} label="范围内日期" value="2026-11-21" min="2026-11-01" max="2026-11-30" />
                            <JuTimePicker {...demoTimeContext} label="受限时间（5 分钟步长）" value="09:30" min="08:00" max="18:00" minuteStep={5} />
                            <JuDatePicker {...demoTimeContext} label="禁用日期" value="2026-11-21" disabled />
                            <JuDateTimePicker {...demoTimeContext} label="错误值示例" value="not-an-instant" />
                            <JuButton
                                variant="secondary"
                                onClick={() => setSelectedDateTime(Temporal.Instant.from(demoNow).toString())}
                            >
                                外部设置 DateTime value
                            </JuButton>
                            <p className="date-time-demo-output">Date: {selectedDate} · Time: {selectedTime} · Instant: {selectedDateTime}</p>
                            <JuTextarea label="备注" placeholder="输入备注" rows={3} />
                            <div className="choice-stack">
                                <JuCheckbox label="订阅本地提醒" checked={newsletter} onCheckedChange={setNewsletter} />
                                <JuSwitch label="启用通知" checked={notifications} onCheckedChange={setNotifications} />
                            </div>
                            <JuRadioGroup
                                label="工作环境"
                                value={preferredMode}
                                onValueChange={setPreferredMode}
                                options={[
                                    { value: "desktop", label: "桌面" },
                                    { value: "portable", label: "便携" },
                                ]}
                            />
                            <JuInput label="警告示例" defaultValue="即将达到限制" warning="接近长度上限" />
                            <JuInput label="错误示例" defaultValue="字段有错误" error="请检查该字段" />
                        </div>
                    </section>

                    <section className="showcase-section" id="data">
                        <SectionHeading title="数据组件" description="表格、搜索、分页和空状态。" />
                        <div className="component-card">
                            <div className="table-toolbar">
                                <JuSearchInput value={searchText} onValueChange={setSearchText} />
                            </div>
                            <JuDataTable
                                columns={demoColumns}
                                data={demoRows.filter((row) => row.name.toLocaleLowerCase().includes(searchText.toLocaleLowerCase()))}
                                getRowId={(row) => row.name}
                            />
                            <JuPagination page={page} pageCount={3} onPageChange={setPage} />
                            <JuEmptyState title="没有更多工具" description="新工具会在后续开发阶段加入。" />
                        </div>
                    </section>

                    <section className="showcase-section" id="feedback">
                        <SectionHeading title="弹窗与反馈" description="统一的对话框、标签页和状态反馈。" />
                        <div className="component-card">
                            <div className="demo-row">
                                <JuDialog
                                    title="示例弹窗"
                                    description="弹窗尺寸、遮罩和焦点行为由公共组件统一管理。"
                                    trigger={<JuButton variant="outline">打开弹窗</JuButton>}
                                    footer={<JuDialogClose><JuButton>完成</JuButton></JuDialogClose>}
                                >
                                    <p className="dialog-copy">这是 JuDialog 的真实工作示例。</p>
                                </JuDialog>
                                <JuConfirmDialog
                                    title="确认清空"
                                    description="此操作会清空当前示例数据。"
                                    trigger={<JuButton variant="destructive">确认弹窗</JuButton>}
                                    confirmLabel="清空"
                                    destructive
                                    onConfirm={showNotification}
                                />
                                <JuButton variant="secondary" onClick={showNotification}>显示通知</JuButton>
                                {showToast && <JuToast>操作已完成</JuToast>}
                            </div>
                            <JuSeparator />
                            <JuTabs
                                label="示例标签页"
                                tabs={[
                                    { id: "first", label: "概览", content: <p>第一个面板内容。</p> },
                                    { id: "second", label: "详情", content: <p>第二个面板内容。</p> },
                                ]}
                            />
                            <JuToolbar>
                                <span className="toolbar-label">工具栏</span>
                                <span className="toolbar-spacer" />
                                <JuBadge variant="success">已连接</JuBadge>
                                <JuButton variant="outline">操作</JuButton>
                            </JuToolbar>
                            <JuStatusBar><span>状态栏左侧</span><span>状态栏右侧</span></JuStatusBar>
                        </div>
                    </section>
                </main>
            </div>
        </div>
    );
}

async function createToolWindowIcon(source: SVGSVGElement): Promise<Uint8Array> {
    const svg = source.cloneNode(true) as SVGSVGElement;
    svg.setAttribute("xmlns", "http://www.w3.org/2000/svg");
    svg.setAttribute("width", "64");
    svg.setAttribute("height", "64");
    svg.setAttribute("stroke", getComputedStyle(source).color);
    svg.removeAttribute("class");
    const image = new Image();
    const url = URL.createObjectURL(new Blob([new XMLSerializer().serializeToString(svg)], { type: "image/svg+xml" }));
    try {
        image.src = url;
        await image.decode();
        const canvas = document.createElement("canvas");
        canvas.width = 64;
        canvas.height = 64;
        const context = canvas.getContext("2d");
        if (!context) throw new Error("无法生成工作台图标。");
        context.drawImage(image, 0, 0, 64, 64);
        const blob = await new Promise<Blob>((resolve, reject) => {
            canvas.toBlob((result) => result ? resolve(result) : reject(new Error("无法编码工作台图标。")), "image/png");
        });
        return new Uint8Array(await blob.arrayBuffer());
    } finally {
        URL.revokeObjectURL(url);
    }
}

function Launcher({
    settings,
    notice,
    suppressAutoHide,
    onOpenSettings,
    onSelectTool,
    onDismissNotice,
}: {
    settings: AppSettings;
    notice: string;
    suppressAutoHide: boolean;
    onOpenSettings: () => void;
    onSelectTool: (toolId: string) => void;
    onDismissNotice: () => void;
}) {
    const toolRows = [
        {
            id: "json",
            title: "JSON 格式化",
            key: settings.toolKeys.json ?? "J",
            icon: <JsonToolIcon />,
            iconClass: "tool-json",
        },
        {
            id: "toto",
            title: "Toto 日程提醒",
            key: settings.toolKeys.toto ?? "T",
            icon: <Clock3 size={23} />,
            iconClass: "tool-toto",
        },
        {
            id: "echo",
            title: "EchoHttp",
            key: settings.toolKeys.echo ?? "E",
            icon: <ArrowLeftRight size={23} />,
            iconClass: "tool-echo",
        },
        {
            id: "kairos",
            title: "Kairos",
            key: settings.toolKeys.kairos ?? "K",
            icon: <Clock3 size={23} />,
            iconClass: "tool-kairos",
        },
        {
            id: "mysqlschema",
            title: "MySQL Schema",
            key: settings.toolKeys.mysqlschema ?? "M",
            icon: <Database size={23} />,
            iconClass: "tool-mysql",
        },
        {
            id: "design-system",
            title: "设计系统工作台",
            key: settings.toolKeys["design-system"] ?? "D",
            icon: <Palette size={23} />,
            iconClass: "tool-design",
        },
    ];

    const startWindowDrag = useWindowDrag();

    useEffect(() => {
        const handleKeyDown = (event: KeyboardEvent) => {
            const rows = Array.from(
                document.querySelectorAll<HTMLButtonElement>(
                    ".tool-list .ju-button-launcher",
                ),
            );
            if (event.key === "ArrowDown" || event.key === "ArrowUp") {
                if (rows.length === 0) return;
                const currentIndex = rows.indexOf(
                    document.activeElement as HTMLButtonElement,
                );
                const offset = event.key === "ArrowDown" ? 1 : -1;
                const nextIndex =
                    currentIndex < 0
                        ? 0
                        : (currentIndex + offset + rows.length) % rows.length;
                event.preventDefault();
                rows[nextIndex].focus();
                return;
            }
            if (event.key === "Escape" && isTauri()) {
                event.preventDefault();
                void hideMainWindow();
                return;
            }
            if (
                event.ctrlKey ||
                event.altKey ||
                event.metaKey ||
                event.shiftKey
            )
                return;
            const target = event.target;
            if (
                target instanceof HTMLElement &&
                ["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName)
            )
                return;
            const tool = Object.entries(settings.toolKeys).find(
                ([, key]) =>
                    key.toLocaleLowerCase() === event.key.toLocaleLowerCase(),
            )?.[0];
            if (!tool) return;
            event.preventDefault();
            onSelectTool(tool);
        };
        window.addEventListener("keydown", handleKeyDown);
        return () => window.removeEventListener("keydown", handleKeyDown);
    }, [onSelectTool, settings.toolKeys]);

    useEffect(() => {
        let dragging = false;
        let settingsSuspended = false;
        const onWindowDrag = (event: Event) => {
            dragging = (event as CustomEvent<boolean>).detail;
        };
        const endWindowDrag = () => {
            if (!dragging) return;
            dragging = false;
        };
        const onSettingsSuspended = (event: Event) => {
            settingsSuspended = (event as CustomEvent<boolean>).detail;
        };
        const handleBlur = () => {
            if (settings.autoHideLauncher && isTauri() && !suppressAutoHide && !settingsSuspended && !dragging) {
                void hideMainWindow().catch(() => undefined);
            }
        };
        window.addEventListener("juju:window-drag", onWindowDrag);
        window.addEventListener("juju:suspend-launcher-autohide", onSettingsSuspended);
        window.addEventListener("mouseup", endWindowDrag);
        window.addEventListener("focus", endWindowDrag);
        window.addEventListener("blur", handleBlur);
        return () => {
            window.removeEventListener("juju:window-drag", onWindowDrag);
            window.removeEventListener("juju:suspend-launcher-autohide", onSettingsSuspended);
            window.removeEventListener("mouseup", endWindowDrag);
            window.removeEventListener("focus", endWindowDrag);
            window.removeEventListener("blur", handleBlur);
        };
    }, [settings.autoHideLauncher, suppressAutoHide]);

    return (
        <div className="launcher-stage">
            <main className="window main-window" id="launcher">
                <header
                    className="titlebar draggable-titlebar"
                    onMouseDown={startWindowDrag}
                >
                    <div className="brand">
                        <img className="app-brand-icon" src={jujuIcon} alt="" />
                        <span>juju</span>
                    </div>
                    <JuIconButton label="设置" onClick={onOpenSettings}>
                        <SettingsIcon size={19} strokeWidth={1.8} />
                    </JuIconButton>
                </header>
                <div
                    className={`tool-list${settings.compactLauncher ? " compact" : ""}`}
                >
                    {toolRows.map((tool) => (
                        <JuButton
                            key={tool.id}
                            variant="launcher"
                            onClick={() => onSelectTool(tool.id)}
                        >
                            <span
                                className={`launcher-tool-icon ${tool.iconClass}`}
                            >
                                {tool.icon}
                            </span>
                            <span className="launcher-tool-name">
                                {tool.title}
                            </span>
                            <span
                                className="key"
                                aria-label={`快捷键 ${tool.key}`}
                            >
                                {tool.key}
                            </span>
                        </JuButton>
                    ))}
                </div>
            </main>
            {notice && (
                <div className="launcher-notice" role="status">
                    <JuToast>{notice}</JuToast>
                    <JuButton
                        size="compact"
                        variant="ghost"
                        onClick={onDismissNotice}
                    >
                        关闭
                    </JuButton>
                </div>
            )}
        </div>
    );
}

function JsonToolIcon() {
    return (
        <svg
            width="23"
            height="23"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="1.6"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
        >
            <rect x="5" y="2" width="14" height="20" rx="2" />
            <path d="M9 8c-2 0-2 2-2 4s0 4 2 4m6-8c2 0 2 2 2 4s0 4-2 4M11 9h2m-2 6h2" />
        </svg>
    );
}

export function LegacySettingsPage({
    onBack,
}: {
    onBack: () => void;
}) {
    const [settings, setSettings] = useState<AppSettings | null>(null);
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState("");
    const setThemeMode = useThemeStore((state) => state.setMode);

    useEffect(() => {
        let active = true;
        void loadAppSettings()
            .then((value) => {
                if (!active) return;
                setSettings(value);
                setThemeMode(value.theme);
            })
            .catch((reason: unknown) => {
                if (active) setError(`无法读取设置：${String(reason)}`);
            });
        return () => {
            active = false;
        };
    }, [setThemeMode]);

    const update = <K extends keyof AppSettings>(
        key: K,
        value: AppSettings[K],
    ) => {
        setSettings((current) =>
            current ? { ...current, [key]: value } : current,
        );
    };

    const save = async () => {
        if (!settings) return;
        setSaving(true);
        setError("");
        let previous: AppSettings | null = null;
        try {
            previous = await loadAppSettings();
            await setStartAtLogin(settings.startAtLogin);
            await saveAppSettings(settings);
            setThemeMode(settings.theme);
        } catch (reason) {
            if (previous && previous.startAtLogin !== settings.startAtLogin) {
                await setStartAtLogin(previous.startAtLogin).catch(
                    () => undefined,
                );
            }
            setError(`保存设置失败：${String(reason)}`);
        } finally {
            setSaving(false);
        }
    };

    const selectDataRoot = async () => {
        try {
            const selected = await chooseDataRoot();
            if (selected) update("dataRoot", selected);
            else if (!isTauri())
                setError("选择数据目录需要在 Juju 桌面应用中操作。");
        } catch (reason) {
            setError(`选择数据目录失败：${String(reason)}`);
        }
    };

    return (
        <div className="product-shell">
            <header className="app-header">
                <a
                    className="brand"
                    href="#settings"
                    onClick={(event) => {
                        event.preventDefault();
                        onBack();
                    }}
                >
                    <span className="brand-mark" aria-hidden="true">
                        J
                    </span>
                    <span>设置</span>
                </a>
                <JuButton variant="outline" onClick={onBack}>
                    返回
                </JuButton>
            </header>
            <main className="settings-content">
                <p className="eyebrow">APPLICATION SETTINGS</p>
                <h1>应用设置</h1>
                {!settings ? (
                    <JuEmptyState title={error || "正在加载设置…"} />
                ) : (
                    <>
                        <section
                            className="settings-group"
                            aria-labelledby="general-settings-title"
                        >
                            <h2 id="general-settings-title">常用设置</h2>
                            <JuSwitch
                                label="登录 Windows 后自动启动"
                                checked={settings.startAtLogin}
                                onCheckedChange={(value) =>
                                    update("startAtLogin", value)
                                }
                            />
                            <JuSwitch
                                label="启动时最小化到托盘"
                                checked={settings.startMinimized}
                                onCheckedChange={(value) =>
                                    update("startMinimized", value)
                                }
                            />
                            <JuSwitch
                                label="Launcher 失焦时自动隐藏"
                                checked={settings.autoHideLauncher}
                                onCheckedChange={(value) =>
                                    update("autoHideLauncher", value)
                                }
                            />
                            <JuCheckbox
                                label="使用紧凑工具列表"
                                checked={settings.compactLauncher}
                                onCheckedChange={(value) =>
                                    update("compactLauncher", value)
                                }
                            />
                        </section>

                        <section
                            className="settings-group"
                            aria-labelledby="display-settings-title"
                        >
                            <h2 id="display-settings-title">显示设置</h2>
                            <JuSelect
                                label="应用主题"
                                value={settings.theme}
                                onValueChange={(value) => {
                                    const theme = value as AppSettings["theme"];
                                    update("theme", theme);
                                    setThemeMode(theme);
                                }}
                                options={[
                                    { value: "system", label: "跟随系统" },
                                    { value: "light", label: "浅色" },
                                    { value: "dark", label: "深色" },
                                ]}
                            />
                        </section>

                        <section
                            className="settings-group"
                            aria-labelledby="shortcut-settings-title"
                        >
                            <h2 id="shortcut-settings-title">快捷键</h2>
                            <JuInput
                                label="显示或隐藏 Launcher"
                                value="Ctrl + Alt + Space"
                                readOnly
                            />
                        </section>

                        <section
                            className="settings-group"
                            aria-labelledby="storage-settings-title"
                        >
                            <h2 id="storage-settings-title">存储设置</h2>
                            <div className="storage-picker-row">
                                <JuInput
                                    label="数据根目录（明文文件）"
                                    value={settings.dataRoot}
                                    readOnly
                                />
                                <JuButton
                                    variant="outline"
                                    onClick={() => void selectDataRoot()}
                                >
                                    选择目录
                                </JuButton>
                            </div>
                            <p className="muted-copy">
                                切换目录只更新 Juju
                                使用的位置，不会复制旧目录数据。应用设置以 JSON
                                明文保存在用户配置目录。
                            </p>
                        </section>

                        {error && (
                            <p className="settings-error" role="alert">
                                {error}
                            </p>
                        )}
                        <div className="settings-actions">
                            <JuButton
                                onClick={() => void save()}
                                loading={saving}
                            >
                                保存设置
                            </JuButton>
                            <JuButton variant="outline" onClick={onBack}>
                                取消
                            </JuButton>
                        </div>
                    </>
                )}
            </main>
        </div>
    );
}

function DemoToolWindow() {
    const close = async () => {
        await closeCurrentToolWindow();
    };

    return (
        <div className="product-shell tool-window-shell">
            <header className="app-header">
                <a className="brand" href="#tool">
                    <span className="brand-mark" aria-hidden="true">
                        J
                    </span>
                    <span>工具窗口演示</span>
                </a>
                <JuButton variant="outline" onClick={() => void close()}>
                    关闭窗口
                </JuButton>
            </header>
            <main className="tool-window-content">
                <p className="eyebrow">WINDOW LIFECYCLE DEMO</p>
                <h1>这是一个独立工具窗口</h1>
                <p>关闭此窗口会销毁它；Juju 主进程和托盘继续运行。</p>
                <JuStatusBar>
                    <span>窗口 label：demo-tool</span>
                    <span>关闭后可从 Launcher 重建</span>
                </JuStatusBar>
            </main>
        </div>
    );
}

function PlaceholderToolWindow({ toolId }: { toolId: string }) {
    const titles: Record<string, string> = {
        json: "JSON 格式化",
        echo: "EchoHttp",
        kairos: "Kairos",
    };
    const title = titles[toolId] ?? toolId;
    const close = async () => {
        await closeCurrentToolWindow();
    };

    return (
        <div className="product-shell tool-window-shell">
            <header className="app-header">
                <a className="brand" href="#tool">
                    <span className="brand-mark" aria-hidden="true">
                        {title.slice(0, 1)}
                    </span>
                    <span>{title}</span>
                </a>
                <JuButton variant="outline" onClick={() => void close()}>
                    关闭窗口
                </JuButton>
            </header>
            <main className="tool-window-content">
                <p className="eyebrow">JUJU TOOL</p>
                <h1>{title} 正在开发中</h1>
                <p>
                    工具入口和窗口生命周期已接入，业务功能按开发计划逐步完成。
                </p>
                <JuStatusBar>
                    <span>窗口已独立创建</span>
                    <span>关闭只销毁此工具窗口</span>
                </JuStatusBar>
            </main>
        </div>
    );
}

export function LegacyTotoWindow({ settings }: { settings: AppSettings }) {
    const timeContext = useTimeContext();
    const today = (typeof timeContext.now === "number"
        ? Temporal.Instant.fromEpochMilliseconds(timeContext.now)
        : Temporal.Instant.from(timeContext.now)).toZonedDateTimeISO(timeContext.timeZone).toPlainDate();
    const [items, setItems] = useState<TotoItem[]>([]);
    const [history, setHistory] = useState<TotoHistoryItem[]>([]);
    const [showHistory, setShowHistory] = useState(false);
    const [historySearch, setHistorySearch] = useState("");
    const [historyStatus, setHistoryStatus] = useState("all");
    const [historyPage, setHistoryPage] = useState(1);
    const [historyPageSize, setHistoryPageSize] = useState(10);
    const [historyFromDate, setHistoryFromDate] = useState(() => today.with({ day: 1 }).toString());
    const [historyToDate, setHistoryToDate] = useState(() => today.toString());
    const [settingsOpen, setSettingsOpen] = useState(false);
    const [defaultRemindMinutes, setDefaultRemindMinutes] = useState(5);
    const [remindMinutesDraft, setRemindMinutesDraft] = useState("5");
    const [content, setContent] = useState("");
    const [plannedDate, setPlannedDate] = useState("");
    const [plannedTime, setPlannedTime] = useState("");
    const [notes, setNotes] = useState("");
    const [editingId, setEditingId] = useState<string | null>(null);
    const [error, setError] = useState("");
    const [notice, setNotice] = useState("");
    const quickEntry = !editingId && content.includes("@");

    const refresh = async () => {
        try {
            const [nextItems, nextHistory] = await Promise.all([
                loadTotoItems(),
                loadTotoHistory(),
            ]);
            setItems(nextItems);
            setHistory(
                nextHistory.sort((left, right) =>
                    right.ended_at.localeCompare(left.ended_at),
                ),
            );
            setError("");
        } catch (reason) {
            setError(`读取 Toto 数据失败：${String(reason)}`);
        }
    };

    useEffect(() => {
        let active = true;
        void Promise.all([
            loadTotoItems(),
            loadTotoHistory(),
            loadTotoDefaultRemindMinutes(),
        ])
            .then(([nextItems, nextHistory, nextRemindMinutes]) => {
                if (!active) return;
                setItems(nextItems);
                setDefaultRemindMinutes(nextRemindMinutes);
                setRemindMinutesDraft(String(nextRemindMinutes));
                setHistory(
                    nextHistory.sort((left, right) =>
                        right.ended_at.localeCompare(left.ended_at),
                    ),
                );
            })
            .catch((reason: unknown) => {
                if (active) setError(`读取 Toto 数据失败：${String(reason)}`);
            });
        return () => {
            active = false;
        };
    }, []);

    const resetForm = () => {
        setEditingId(null);
        setContent("");
        setPlannedDate("");
        setPlannedTime("");
        setNotes("");
    };

    const saveItem = async () => {
        try {
            if (plannedDate && plannedTime) {
                Temporal.PlainDateTime.from(`${plannedDate}T${plannedTime}`)
                    .toZonedDateTime(timeContext.timeZone, { disambiguation: "reject" });
            }
            const plannedAt =
                plannedDate && plannedTime
                    ? `${plannedDate} ${plannedTime}`
                    : null;
            if (editingId) {
                await updateTotoItem(editingId, content, plannedAt, notes);
                setNotice("事项已更新。");
            } else if (quickEntry) {
                await addTotoQuickItem(content);
                setNotice("快速事项已新增。");
            } else {
                await addTotoItem(content, plannedAt, notes);
                setNotice("事项已新增。");
            }
            resetForm();
            await refresh();
        } catch (reason) {
            setError(`保存事项失败：${String(reason)}`);
        }
    };

    const editItem = (item: TotoItem) => {
        setEditingId(item.id);
        setContent(item.content);
        if (item.planned_at) {
            setPlannedDate(item.planned_at.slice(0, 10));
            setPlannedTime(item.planned_at.slice(11, 16));
        } else {
            setPlannedDate("");
            setPlannedTime("");
        }
        setNotes(item.notes);
        setShowHistory(false);
    };

    const finishItem = async (id: string, completed: boolean) => {
        try {
            await finishTotoItem(id, completed);
            setNotice(completed ? "事项已完成。" : "事项已取消。");
            if (editingId === id) resetForm();
            await refresh();
        } catch (reason) {
            setError(`更新事项状态失败：${String(reason)}`);
        }
    };

    const saveReminderSetting = async () => {
        try {
            const minutes = Number(remindMinutesDraft);
            await saveTotoDefaultRemindMinutes(minutes);
            setDefaultRemindMinutes(minutes);
            setSettingsOpen(false);
            setError("");
        } catch (reason) {
            setError(`保存 Toto 设置失败：${String(reason)}`);
        }
    };

    const activeColumns: JuColumn<TotoItem>[] = [
        { id: "content", header: "事项", cell: (item) => item.content },
        {
            id: "planned",
            header: "计划时间",
            cell: (item) => item.planned_at ?? "—",
        },
        {
            id: "reminder",
            header: "提醒时间",
            cell: (item) => item.reminder_at ?? "—",
        },
        {
            id: "status",
            header: "提醒状态",
            cell: (item) => item.reminder_status,
        },
        { id: "created", header: "创建时间", cell: (item) => item.created_at },
        {
            id: "actions",
            header: "操作",
            cell: (item) => (
                <div className="toto-row-actions">
                    <JuButton
                        size="compact"
                        variant="ghost"
                        onClick={() => editItem(item)}
                    >
                        编辑
                    </JuButton>
                    <JuButton
                        size="compact"
                        variant="secondary"
                        onClick={() => void finishItem(item.id, true)}
                    >
                        完成
                    </JuButton>
                    <JuConfirmDialog
                        title="取消事项"
                        description={`确定取消“${item.content}”吗？`}
                        trigger={
                            <JuButton size="compact" variant="ghost">
                                取消
                            </JuButton>
                        }
                        confirmLabel="确认取消"
                        destructive
                        onConfirm={() => void finishItem(item.id, false)}
                    />
                </div>
            ),
        },
    ];

    const historyColumns: JuColumn<TotoHistoryItem>[] = [
        { id: "content", header: "事项", cell: (item) => item.content },
        { id: "status", header: "结果", cell: (item) => item.end_status },
        {
            id: "planned",
            header: "计划时间",
            cell: (item) => item.planned_at ?? "—",
        },
        { id: "ended", header: "结束时间", cell: (item) => item.ended_at },
        { id: "notes", header: "备注", cell: (item) => item.notes || "—" },
    ];

    const visibleHistory = history.filter((item) => {
        const matchesText = `${item.content} ${item.notes}`
            .toLocaleLowerCase()
            .includes(historySearch.toLocaleLowerCase());
        const matchesStatus =
            historyStatus === "all" || item.end_status === historyStatus;
        const endedDate = item.ended_at.slice(0, 10);
        const matchesFrom = !historyFromDate || endedDate >= historyFromDate;
        const matchesTo = !historyToDate || endedDate <= historyToDate;
        return matchesText && matchesStatus && matchesFrom && matchesTo;
    });
    const historyPageCount = Math.max(
        1,
        Math.ceil(visibleHistory.length / historyPageSize),
    );
    const historyRows = visibleHistory.slice(
        (historyPage - 1) * historyPageSize,
        historyPage * historyPageSize,
    );

    const close = async () => {
        if (settings.returnToLauncherOnToolClose && isTauri()) {
            await showMainWindow();
            await emitTo("main", "app:navigate", "launcher");
        }
        if (isTauri()) await getCurrentWindow().close();
        else window.close();
    };

    return (
        <div className="product-shell toto-shell">
            <header className="app-header">
                <a className="brand" href="#toto">
                    <span className="brand-mark" aria-hidden="true">
                        T
                    </span>
                    <span>Toto</span>
                    <span className="version-tag">待办事项</span>
                </a>
                <div className="header-actions">
                    <JuButton variant="outline" onClick={() => void refresh()}>
                        刷新
                    </JuButton>
                    <JuDialog
                        title="Toto 设置"
                        description="调整快速新增事项的默认提醒提前时间。"
                        open={settingsOpen}
                        onOpenChange={setSettingsOpen}
                        trigger={
                            <JuButton
                                variant="outline"
                                onClick={() => setSettingsOpen(true)}
                            >
                                设置
                            </JuButton>
                        }
                        footer={
                            <>
                                <JuDialogClose>
                                    <JuButton variant="outline">取消</JuButton>
                                </JuDialogClose>
                                <JuButton
                                    onClick={() => void saveReminderSetting()}
                                >
                                    保存
                                </JuButton>
                            </>
                        }
                    >
                        <JuInput
                            label="默认提醒提前分钟"
                            type="number"
                            min={0}
                            step={1}
                            value={remindMinutesDraft}
                            onChange={(event) =>
                                setRemindMinutesDraft(event.currentTarget.value)
                            }
                        />
                        <p className="muted-copy toto-setting-hint">
                            当前值：{defaultRemindMinutes} 分钟
                        </p>
                    </JuDialog>
                    <JuButton variant="ghost" onClick={() => void close()}>
                        关闭窗口
                    </JuButton>
                </div>
            </header>

            <main className="toto-content" id="toto">
                <section
                    className="toto-editor"
                    aria-labelledby="toto-editor-title"
                >
                    <div className="toto-editor-heading">
                        <div>
                            <p className="eyebrow">TOTO · 本地日程</p>
                            <h1 id="toto-editor-title">
                                {editingId ? "编辑事项" : "快速新增"}
                            </h1>
                        </div>
                        {editingId && (
                            <JuButton
                                size="compact"
                                variant="ghost"
                                onClick={resetForm}
                            >
                                放弃编辑
                            </JuButton>
                        )}
                    </div>
                    <div className="toto-form-grid">
                        <JuInput
                            label="事项内容"
                            value={content}
                            onChange={(event) =>
                                setContent(event.currentTarget.value)
                            }
                            placeholder="事项内容 或 内容@yyyyMMddHHmm"
                            hint="快速时间支持 HHmm、ddHHmm、MMddHHmm、yyyyMMddHHmm、+1600、W11600、H2M30、M45"
                        />
                        <JuDatePicker
                            label="计划日期"
                            {...timeContext}
                            value={plannedDate}
                            onChange={(next) => setPlannedDate(next ?? "")}
                            disabled={quickEntry}
                        />
                        <JuTimePicker
                            label="计划时间（可选）"
                            {...timeContext}
                            value={plannedTime}
                            disabled={quickEntry}
                            onChange={(next) => setPlannedTime(next ?? "")}
                        />
                        <JuInput
                            label="备注"
                            value={notes}
                            onChange={(event) =>
                                setNotes(event.currentTarget.value)
                            }
                            placeholder="可选"
                            disabled={quickEntry}
                        />
                    </div>
                    <div className="toto-form-footer">
                        <p className="muted-copy">
                            设置计划时间后，Toto 会在阶段 3.2 接入后台提醒。
                        </p>
                        <JuButton onClick={() => void saveItem()}>
                            {editingId ? "保存修改" : "新增事项"}
                        </JuButton>
                    </div>
                </section>

                <section className="toto-list-section">
                    <div className="toto-list-heading">
                        <div>
                            <h2>{showHistory ? "历史事项" : "进行中"}</h2>
                            <p>
                                {showHistory
                                    ? `${history.length} 条历史记录`
                                    : `${items.length} 条进行中事项`}
                            </p>
                        </div>
                        <div className="demo-row">
                            <JuButton
                                variant={showHistory ? "outline" : "secondary"}
                                onClick={() => {
                                    setShowHistory(false);
                                    setHistoryPage(1);
                                }}
                            >
                                进行中
                            </JuButton>
                            <JuButton
                                variant={showHistory ? "secondary" : "outline"}
                                onClick={() => {
                                    setShowHistory(true);
                                    setHistoryPage(1);
                                }}
                            >
                                历史
                            </JuButton>
                        </div>
                    </div>
                    {error && (
                        <p className="settings-error" role="alert">
                            {error}
                        </p>
                    )}
                    {notice && (
                        <p className="toto-notice" role="status">
                            {notice}
                        </p>
                    )}
                    {showHistory ? (
                        <>
                            <div className="toto-history-filters">
                                <JuSearchInput
                                    label="搜索历史事项"
                                    value={historySearch}
                                    onValueChange={(value) => {
                                        setHistorySearch(value);
                                        setHistoryPage(1);
                                    }}
                                />
                                <JuSelect
                                    label="结束状态"
                                    value={historyStatus}
                                    onValueChange={(value) => {
                                        setHistoryStatus(value);
                                        setHistoryPage(1);
                                    }}
                                    options={[
                                        { value: "all", label: "全部状态" },
                                        { value: "已完成", label: "已完成" },
                                        { value: "已取消", label: "已取消" },
                                    ]}
                                />
                                <JuDatePicker
                                    label="结束日期起"
                                    {...timeContext}
                                    value={historyFromDate}
                                    onChange={(value) => {
                                        setHistoryFromDate(value ?? "");
                                        setHistoryPage(1);
                                    }}
                                />
                                <JuDatePicker
                                    label="结束日期止"
                                    {...timeContext}
                                    value={historyToDate}
                                    onChange={(value) => {
                                        setHistoryToDate(value ?? "");
                                        setHistoryPage(1);
                                    }}
                                />
                                <JuSelect
                                    label="每页条数"
                                    value={String(historyPageSize)}
                                    onValueChange={(value) => {
                                        setHistoryPageSize(Number(value));
                                        setHistoryPage(1);
                                    }}
                                    options={[10, 20, 50, 100, 200, 500].map(
                                        (size) => ({
                                            value: String(size),
                                            label: String(size),
                                        }),
                                    )}
                                />
                            </div>
                            <JuDataTable
                                columns={historyColumns}
                                data={historyRows}
                                getRowId={(item) => item.id}
                                emptyMessage="暂无符合条件的历史事项"
                            />
                            <JuPagination
                                page={historyPage}
                                pageCount={historyPageCount}
                                onPageChange={setHistoryPage}
                            />
                        </>
                    ) : (
                        <JuDataTable
                            columns={activeColumns}
                            data={items}
                            getRowId={(item) => item.id}
                            emptyMessage="暂无事项，使用上方表单新增。"
                        />
                    )}
                </section>
                <footer className="toto-footer">
                    <JuStatusBar>
                        <span>
                            数据以 UTF-8 BOM CSV 明文保存在 Juju 数据目录
                        </span>
                        <span>按计划时间排序</span>
                    </JuStatusBar>
                </footer>
            </main>
        </div>
    );
}

function SectionHeading({
    title,
    description,
}: {
    title: string;
    description: string;
}) {
    return (
        <div className="section-heading">
            <div>
                <h2>{title}</h2>
                <p>{description}</p>
            </div>
        </div>
    );
}

function TokenSwatch({ name, variable, sample }: { name: string; variable: string; sample: string }) {
    return (
        <div className="token-card">
            <span
                aria-hidden="true"
                className={`token-preview is-${sample}`}
                style={{ "--design-token": `var(${variable})` } as CSSProperties}
            />
            <span className="token-name">{name}</span>
            <code>{variable}</code>
        </div>
    );
}
