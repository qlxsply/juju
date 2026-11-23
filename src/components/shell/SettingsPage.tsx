import { useEffect, useState, type KeyboardEvent, type ReactNode } from "react";
import { listen } from "@tauri-apps/api/event";
import { isTauri } from "@tauri-apps/api/core";
import {
    BriefcaseBusiness,
    ChevronDown,
    Clock3,
    Copy,
    FileJson,
    FileText,
    Keyboard,
    List,
    Monitor,
    Pencil,
    RefreshCw,
    Settings as SettingsIcon,
    Puzzle,
    X,
    type LucideIcon,
} from "lucide-react";
import {
    JuButton,
    JuDialog,
    JuDialogClose,
    JuIconButton,
    JuInput,
    JuSelect,
    JuSwitch,
    JuTemporalInput,
} from "../ju";
import type { AppSettings } from "../../services/desktop";
import jujuIcon from "../../../src-tauri/icons/icon.ico";
import packageJson from "../../../package.json";
import {
    chooseDataRoot,
    loadTotoDefaultRemindMinutes,
    saveAppSettings,
    saveTotoDefaultRemindMinutes,
    setStartAtLogin,
    type TotoToolSettings,
    defaultTotoToolSettings,
    loadTotoSettings,
    saveTotoSettings,
} from "../../services/desktop";
import { useThemeStore } from "../../design-system/theme-store";
import { useWindowDrag } from "../../hooks/useWindowDrag";

type PageId =
    | "general"
    | "shortcuts"
    | "storage"
    | "display"
    | "updates"
    | "json"
    | "toto"
    | "echo"
    | "kairos";

const appNavigation: { id: PageId; label: string; icon: LucideIcon }[] = [
    { id: "general", label: "常用设置", icon: List },
    { id: "shortcuts", label: "快捷键", icon: Keyboard },
    { id: "storage", label: "存储设置", icon: FileText },
    { id: "display", label: "显示设置", icon: Monitor },
    { id: "updates", label: "更新与版本", icon: RefreshCw },
];

const toolNavigation: { id: "json" | "toto" | "echo" | "kairos"; label: string; color: string; icon?: LucideIcon }[] = [
    { id: "json" as const, label: "JSON 格式化", color: "var(--tool-json)", icon: FileJson },
    { id: "toto" as const, label: "Toto 日程提醒", color: "var(--tool-toto)", icon: Clock3 },
    { id: "echo" as const, label: "EchoHttp", color: "var(--tool-echo)", icon: Puzzle },
    { id: "kairos" as const, label: "Kairos", color: "var(--tool-kairos)", icon: Puzzle },
];

const jsonShortcutContexts = [
    ["Save", "Format", "FoldAll", "UnfoldAll", "New", "UnfoldLevel", "CopyMinified", "CopyFile", "EnterDiff"],
    ["Save", "Format", "FoldAll", "UnfoldAll", "New", "UnfoldLevel", "CopyMinified", "CopyFile", "EnterDiff", "CopyText"],
    ["Save", "Format", "FoldAll", "UnfoldAll", "New", "UnfoldLevel", "CopyMinified", "CopyFile", "EnterDiff", "Rename", "Delete"],
    ["ExitDiff"],
];

function canonicalShortcut(value: string) {
    return value.replace(/\s+/g, "").toLowerCase();
}

function duplicateShortcutMessage() {
    return "快捷键已存在，请更换其他组合。";
}

export function SettingsPage({
    settings: initialSettings,
    onSettingsChanged,
    onBack,
    onMessage,
}: {
    settings: AppSettings;
    onSettingsChanged: (settings: AppSettings) => void;
    onBack: () => void;
    onMessage: (message: string) => void;
}) {
    const settings = initialSettings;
    const [page, setPage] = useState<PageId>(() => {
        const requested = new URLSearchParams(window.location.search).get("section");
        const valid: PageId[] = ["general", "shortcuts", "storage", "display", "updates", "json", "toto", "echo", "kairos"];
        return valid.includes(requested as PageId) ? requested as PageId : "general";
    });
    const [appGroupOpen, setAppGroupOpen] = useState(true);
    const [toolGroupOpen, setToolGroupOpen] = useState(true);
    const [error, setError] = useState("");
    const [totoSettings, setTotoSettings] =
        useState<TotoToolSettings>(defaultTotoToolSettings);
    const [dailyStartDraft, setDailyStartDraft] = useState("");
    const [dailyEndDraft, setDailyEndDraft] = useState("");
    const setThemeMode = useThemeStore((state) => state.setMode);
    const startWindowDrag = useWindowDrag();

    useEffect(() => {
        void loadTotoSettings()
            .then((loaded) => {
                setTotoSettings(loaded);
                setDailyStartDraft(loaded.daily_review_start_time ?? "");
                setDailyEndDraft(loaded.daily_review_end_time ?? "");
            })
            .catch((reason: unknown) => setError(String(reason)));
    }, []);

    useEffect(() => {
        if (!isTauri()) return;
        let unlisten: (() => void) | undefined;
        let active = true;
        void listen<string>("app:navigate-settings", (event) => {
            const valid: PageId[] = ["general", "shortcuts", "storage", "display", "updates", "json", "toto", "echo", "kairos"];
            if (valid.includes(event.payload as PageId)) setPage(event.payload as PageId);
        }).then((stopListening) => {
            if (active) unlisten = stopListening;
            else stopListening();
        });
        return () => { active = false; unlisten?.(); };
    }, []);

    const commit = async (next: AppSettings): Promise<boolean> => {
        const previous = settings;
        onSettingsChanged(next);
        setError("");
        try {
            if (next.startAtLogin !== previous.startAtLogin) {
                await setStartAtLogin(next.startAtLogin);
            }
            await saveAppSettings(next);
            setThemeMode(next.theme);
            return true;
        } catch (reason) {
            onSettingsChanged(previous);
            if (next.startAtLogin !== previous.startAtLogin) {
                await setStartAtLogin(previous.startAtLogin).catch(
                    () => undefined,
                );
            }
            setError(String(reason));
            return false;
        }
    };

    const update = <K extends keyof AppSettings>(
        key: K,
        value: AppSettings[K],
    ) => commit({ ...settings, [key]: value });

    const updateToolSetting = (
        toolId: string,
        key: string,
        value: unknown,
    ) => {
        const current = settings.toolSettings[toolId] ?? {};
        update("toolSettings", {
            ...settings.toolSettings,
            [toolId]: { ...current, [key]: value },
        });
    };

    const updateToolKey = (toolId: string, value: string) =>
        update("toolKeys", { ...settings.toolKeys, [toolId]: value });

    const updateGlobalShortcut = (toolId: string, value: string) =>
        update("toolGlobalShortcuts", {
            ...settings.toolGlobalShortcuts,
            [toolId]: value,
        });

    const validateGlobalShortcut = (candidate: string, toolId?: string) => {
        const canonical = canonicalShortcut(candidate);
        if (!canonical || canonical === "none") return null;
        const owner = toolId ?? "launcher";
        const registered = [
            ...Object.entries(settings.toolGlobalShortcuts),
            ["launcher", settings.leaderShortcut] as [string, string],
        ];
        return registered.some(
            ([id, value]) => id !== owner && canonicalShortcut(value) === canonical,
        )
            ? duplicateShortcutMessage()
            : null;
    };

    const validateToolKey = (candidate: string, toolId: string) => {
        const canonical = canonicalShortcut(candidate);
        return Object.entries(settings.toolKeys).some(
            ([id, value]) => id !== toolId && canonicalShortcut(value) === canonical,
        )
            ? duplicateShortcutMessage()
            : null;
    };

    const renderGeneral = () => (
        <>
            <SettingsSection title="启动与关闭">
                <SettingRow kind="toggle" label="启动时仅显示托盘图标">
                    <ToolSwitch
                        label="启动时仅显示托盘图标"
                        checked={settings.startMinimized}
                        onChange={(value) => update("startMinimized", value)}
                    />
                </SettingRow>
                <SettingRow kind="toggle" label="开机自动启动">
                    <ToolSwitch
                        label="开机自动启动"
                        checked={settings.startAtLogin}
                        onChange={(value) => update("startAtLogin", value)}
                    />
                </SettingRow>
                <SettingRow kind="toggle" label="显示工具状态提示">
                    <ToolSwitch
                        label="显示工具状态提示"
                        checked={settings.showToolNotifications}
                        onChange={(value) =>
                            update("showToolNotifications", value)
                        }
                    />
                </SettingRow>
                <SettingRow kind="toggle" label="点击空白区域时自动隐藏应用">
                    <ToolSwitch
                        label="点击空白区域时自动隐藏应用"
                        checked={settings.autoHideLauncher}
                        onChange={(value) => update("autoHideLauncher", value)}
                    />
                </SettingRow>
            </SettingsSection>
            <SettingsSection title="窗口行为">
                <SettingRow kind="toggle" label="工具关闭后重新显示入口">
                    <ToolSwitch
                        label="工具关闭后重新显示入口"
                        checked={settings.returnToLauncherOnToolClose}
                        onChange={(value) =>
                            update("returnToLauncherOnToolClose", value)
                        }
                    />
                </SettingRow>
                <SettingRow kind="numeric" label="主界面组合键超时（毫秒）">
                    <ValidatedNumberSetting
                        key={settings.launcherTimeoutMilliseconds}
                        label="主界面组合键超时（毫秒）"
                        value={settings.launcherTimeoutMilliseconds}
                        min={250}
                        max={30000}
                        onSave={(value) => update("launcherTimeoutMilliseconds", value)}
                    />
                </SettingRow>
            </SettingsSection>
        </>
    );

    const renderShortcuts = () => (
        <>
            <SettingsSection title="全局快捷键">
                <ShortcutRow
                    label="显示 / 隐藏主界面"
                    value={settings.leaderShortcut}
                    onSave={(value) => update("leaderShortcut", value)}
                    validateCandidate={(candidate) => validateGlobalShortcut(candidate)}
                    allowModifiers
                    requireModifier
                />
            </SettingsSection>
        </>
    );

    const renderJsonSettings = () => {
        const value = settings.toolSettings.json ?? {};
        const shortcutValues: Record<string, string> = {
            Save: "Alt+S",
            Format: "Alt+F",
            FoldAll: "Alt+Q",
            UnfoldAll: "Alt+W",
            New: "Alt+A",
            UnfoldLevel: "Alt+E",
            CopyText: "Ctrl+C",
            CopyMinified: "Ctrl+Shift+C",
            CopyFile: "Ctrl+Alt+Shift+C",
            EnterDiff: "Alt+D",
            ExitDiff: "Escape",
            Rename: "Alt+R",
            Delete: "Delete",
            ...((value.shortcuts ?? {}) as Record<string, string>),
        };
        const updateJson = (key: string, next: string | number | boolean) =>
            updateToolSetting("json", key, next);
        const updateJsonShortcut = (key: string, next: string) =>
            update("toolSettings", {
                ...settings.toolSettings,
                json: {
                    ...value,
                    shortcuts: { ...shortcutValues, [key]: next },
                },
            });
        const updateJsonGlobalShortcut = (next: string) =>
            commit({
                ...settings,
                toolGlobalShortcuts: {
                    ...settings.toolGlobalShortcuts,
                    json: next,
                },
                toolSettings: {
                    ...settings.toolSettings,
                    json: { ...value, globalShortcut: next },
                },
            });
        const validateJsonShortcut = (command: string, candidate: string) => {
            const canonical = canonicalShortcut(candidate);
            if (!canonical || canonical === "none") return null;
            return jsonShortcutContexts
                .filter((context) => context.includes(command))
                .some((context) => context.some((other) =>
                    other !== command && canonicalShortcut(shortcutValues[other] ?? "") === canonical,
                ))
                ? duplicateShortcutMessage()
                : null;
        };
        const windowShortcuts: [string, string, string][] = [
            ["保存", "Save", "Alt+S"],
            ["格式化", "Format", "Alt+F"],
            ["折叠全部", "FoldAll", "Alt+Q"],
            ["展开全部", "UnfoldAll", "Alt+W"],
            ["新建", "New", "Alt+A"],
            ["逐层展开", "UnfoldLevel", "Alt+E"],
            ["复制压缩文本", "CopyMinified", "Ctrl+Shift+C"],
            ["复制文件", "CopyFile", "Ctrl+Alt+Shift+C"],
            ["进入对比", "EnterDiff", "Alt+D"],
        ];
        const renderShortcutGroup = (
            title: string,
            rows: [string, string, string][],
        ) => (
            <SettingsSection title={title}>
                {rows.map(([label, key, fallback]) => (
                    <ShortcutRow
                        key={key}
                        label={label}
                        value={String(shortcutValues[key] ?? fallback)}
                        onSave={(next) => updateJsonShortcut(key, next)}
                        validateCandidate={(candidate) => validateJsonShortcut(key, candidate)}
                        allowModifiers
                    />
                ))}
            </SettingsSection>
        );
        return (
            <>
                <SettingsSection title="工具入口">
                    <ShortcutRow
                        label="主界面快捷键"
                        value={settings.toolKeys.json ?? "J"}
                        onSave={(key) => updateToolKey("json", key)}
                        validateCandidate={(candidate) => validateToolKey(candidate, "json")}
                    />
                    <ShortcutRow
                        label="全局快捷键"
                        value={String(value.globalShortcut ?? "None")}
                        onSave={updateJsonGlobalShortcut}
                        validateCandidate={(candidate) => validateGlobalShortcut(candidate, "json")}
                        allowModifiers
                        requireModifier
                        allowNone
                    />
                </SettingsSection>
                <SettingsSection title="窗口与进程">
                    <SettingRow kind="toggle" label="关闭窗口后保留会话">
                        <ToolSwitch label="关闭窗口后保留会话" checked={Boolean(value.keepWindowOnClose ?? true)} onChange={(next) => updateJson("keepWindowOnClose", next)} />
                    </SettingRow>
                    <SettingRow kind="toggle" label="默认最大化打开">
                        <ToolSwitch label="默认最大化打开" checked={Boolean(value.openMaximized ?? false)} onChange={(next) => updateJson("openMaximized", next)} />
                    </SettingRow>
                    <SettingRow kind="toggle" label="记住窗口大小与位置">
                        <ToolSwitch label="记住窗口大小与位置" checked={Boolean(value.rememberWindowPlacement ?? true)} onChange={(next) => updateJson("rememberWindowPlacement", next)} />
                    </SettingRow>
                </SettingsSection>
                <SettingsSection title="编辑设置">
                    <SettingRow kind="numeric" label="自动保存延迟（毫秒）">
                        <ValidatedNumberSetting
                            key={Number(value.autosaveDelayMilliseconds ?? 800)}
                            label="自动保存延迟（毫秒）"
                            min={100}
                            max={10000}
                            value={Number(value.autosaveDelayMilliseconds ?? 800)}
                            onSave={(delay) => updateJson("autosaveDelayMilliseconds", delay)}
                        />
                    </SettingRow>
                    <SettingRow kind="numeric" label="缩进空格数">
                        <ValidatedNumberSetting
                            key={Number(value.indentSize ?? 2)}
                            label="缩进空格数"
                            min={1}
                            max={8}
                            value={Number(value.indentSize ?? 2)}
                            onSave={(indent) => updateJson("indentSize", indent)}
                        />
                    </SettingRow>
                </SettingsSection>
                <SettingsSection title="存储目录">
                    <PathRow path={`${settings.dataRoot}\\tools\\json`} />
                </SettingsSection>
                {renderShortcutGroup("编辑模式窗口快捷键", windowShortcuts)}
                {renderShortcutGroup("编辑器快捷键", [
                    ["复制文本", "CopyText", "Ctrl+C"],
                ])}
                {renderShortcutGroup("文件列表快捷键", [
                    ["重命名", "Rename", "Alt+R"],
                    ["删除", "Delete", "Delete"],
                ])}
                {renderShortcutGroup("对比模式快捷键", [
                    ["退出对比", "ExitDiff", "Escape"],
                ])}
            </>
        );
    };

    const renderTotoSettings = () => {
        const value = totoSettings;
        const localShortcutKeys = [
            "shortcut_add",
            "shortcut_history",
            "shortcut_refresh",
            "shortcut_detail",
            "shortcut_edit",
            "shortcut_complete",
            "shortcut_cancel",
        ];
        const updateToto = (key: string, nextValue: string | number | boolean) => {
            const next = { ...value, [key]: String(nextValue) };
            setTotoSettings(next);
            return (async () => {
                try {
                    if (key === "hotkey") {
                        const shortcuts = {
                            ...settings.toolGlobalShortcuts,
                            toto: String(nextValue),
                        };
                        const committed = await commit({
                            ...settings,
                            toolGlobalShortcuts: shortcuts,
                        });
                        if (!committed) {
                            setTotoSettings(value);
                            return false;
                        }
                    }
                    await saveTotoSettings(next);
                    return true;
                } catch (reason) {
                    setTotoSettings(value);
                    if (key === "daily_review_start_time") {
                        setDailyStartDraft(value.daily_review_start_time ?? "");
                    }
                    if (key === "daily_review_end_time") {
                        setDailyEndDraft(value.daily_review_end_time ?? "");
                    }
                    if (key === "hotkey") {
                        await commit({
                            ...settings,
                            toolGlobalShortcuts: {
                                ...settings.toolGlobalShortcuts,
                                toto: value.hotkey ?? "None",
                            },
                        });
                    }
                    setError(String(reason));
                    return false;
                }
            })();
        };
        return (
            <>
                <SettingsSection title="工具入口">
                    <ShortcutRow
                        label="主界面快捷键"
                        value={settings.toolKeys.toto ?? "T"}
                        onSave={(key) => updateToolKey("toto", key)}
                        validateCandidate={(candidate) => validateToolKey(candidate, "toto")}
                    />
                    <ShortcutRow
                        label="全局快捷键"
                        value={value.hotkey ?? "None"}
                        onSave={(key) => updateToto("hotkey", key)}
                        validateCandidate={(candidate) => validateGlobalShortcut(candidate, "toto")}
                        allowModifiers
                        requireModifier
                        allowNone
                    />
                </SettingsSection>
                <SettingsSection title="提醒设置">
                    <SettingRow kind="numeric" label="默认提前提醒分钟">
                        <TotoReminderMinutes
                            onSaved={(minutes) =>
                                setTotoSettings((previous) => ({
                                    ...previous,
                                    default_remind_minutes: String(minutes),
                                }))
                            }
                        />
                    </SettingRow>
                </SettingsSection>
                <SettingsSection title="每日事项盘点">
                    <SettingRow kind="time" label="开始盘点">
                        <JuTemporalInput
                            label="开始盘点"
                            labelHidden
                            type="time"
                            value={dailyStartDraft}
                            onBlur={(time) =>
                                updateToto(
                                    "daily_review_start_time",
                                    time,
                                )
                            }
                            onChange={setDailyStartDraft}
                        />
                    </SettingRow>
                    <SettingRow kind="time" label="结束盘点">
                        <JuTemporalInput
                            label="结束盘点"
                            labelHidden
                            type="time"
                            value={dailyEndDraft}
                            onBlur={(time) =>
                                updateToto(
                                    "daily_review_end_time",
                                    time,
                                )
                            }
                            onChange={setDailyEndDraft}
                        />
                    </SettingRow>
                    <SettingRow kind="toggle" label="仅工作日提醒">
                        <ToolSwitch
                            label="仅工作日提醒"
                            checked={String(value.daily_review_workdays ?? "0") === "1"}
                            onChange={(next) => updateToto("daily_review_workdays", next ? "1" : "0")}
                        />
                    </SettingRow>
                </SettingsSection>
                <SettingsSection title="快捷键">
                    {([
                        ["新增", "shortcut_add", "Alt+A"],
                        ["历史", "shortcut_history", "Alt+Q"],
                        ["刷新", "shortcut_refresh", "Alt+R"],
                        ["详情", "shortcut_detail", "Alt+D"],
                        ["编辑", "shortcut_edit", "Alt+E"],
                        ["完成", "shortcut_complete", "Alt+F"],
                        ["取消", "shortcut_cancel", "Alt+C"],
                    ] as const).map(([label, key, fallback]) => (
                        <ShortcutRow
                            key={key}
                            label={label}
                            value={String(value[key] ?? fallback)}
                            onSave={(next) => updateToto(key, next)}
                            validateCandidate={(candidate) => localShortcutKeys.some((other) =>
                                other !== key && canonicalShortcut(value[other] ?? "") === canonicalShortcut(candidate),
                            ) ? duplicateShortcutMessage() : null}
                            allowModifiers
                        />
                    ))}
                </SettingsSection>
                <SettingsSection title="存储目录">
                    <PathRow path={`${settings.dataRoot}\\tools\\toto`} />
                </SettingsSection>
            </>
        );
    };

    const renderPlaceholderToolSettings = (tool: string) => {
        return (
            <>
            <SettingsSection title="工具入口">
                <ShortcutRow
                    label="主界面快捷键"
                    value={settings.toolKeys[tool] ?? ""}
                    onSave={(key) => updateToolKey(tool, key)}
                    validateCandidate={(candidate) => validateToolKey(candidate, tool)}
                />
                <ShortcutRow
                    label="全局快捷键"
                    value={settings.toolGlobalShortcuts[tool] ?? "None"}
                    onSave={(key) => updateGlobalShortcut(tool, key)}
                    validateCandidate={(candidate) => validateGlobalShortcut(candidate, tool)}
                    allowModifiers
                    requireModifier
                    allowNone
                />
            </SettingsSection>
        </>
        );
    };

    const renderPage = () => {
        if (page === "general") return renderGeneral();
        if (page === "shortcuts") return renderShortcuts();
        if (page === "storage") {
            return (
                <SettingsSection title="存储设置">
                    <PathRow
                        path={settings.pendingDataRoot ?? settings.dataRoot}
                        editable
                        onChange={(path) => {
                            const pending = path === settings.dataRoot ? null : path;
                            update("pendingDataRoot", pending);
                            if (pending) {
                                onMessage("新数据目录将在 Juju 退出后生效；不会复制旧目录数据。");
                            }
                        }}
                    />
                    {settings.pendingDataRoot && (
                        <p className="settings-description">
                            新目录将在退出 Juju 后应用，不会复制当前目录中的文件。
                        </p>
                    )}
                </SettingsSection>
            );
        }
        if (page === "display") {
            return (
                <SettingsSection title="外观与布局">
                    <SettingRow kind="select" label="应用主题">
                        <JuSelect
                            ariaLabel="应用主题"
                            value={settings.theme}
                            onValueChange={(value) =>
                                update("theme", value as AppSettings["theme"])
                            }
                            options={[
                                { value: "system", label: "跟随系统" },
                                { value: "light", label: "浅色" },
                                { value: "dark", label: "深色" },
                            ]}
                        />
                    </SettingRow>
                    <SettingRow kind="select" label="工具列表密度">
                        <JuSelect
                            ariaLabel="工具列表密度"
                            value={
                                settings.compactLauncher
                                    ? "compact"
                                    : "comfortable"
                            }
                            onValueChange={(value) =>
                                update("compactLauncher", value === "compact")
                            }
                            options={[
                                { value: "comfortable", label: "舒适" },
                                { value: "compact", label: "紧凑" },
                            ]}
                        />
                    </SettingRow>
                </SettingsSection>
            );
        }
        if (page === "updates") {
            return (
                <>
                    <SettingsSection title="当前版本">
                        <SettingRow kind="info" label="juju">
                            <span className="muted-copy">
                                Windows x64 · 版本 {packageJson.version}
                            </span>
                        </SettingRow>
                    </SettingsSection>
                    <SettingsSection title="软件更新">
                        <SettingRow kind="info" label="更新与版本">
                            <span className="muted-copy">
                                自动更新不在当前实现范围内
                            </span>
                        </SettingRow>
                    </SettingsSection>
                </>
            );
        }
        if (page === "json") return renderJsonSettings();
        if (page === "toto") return renderTotoSettings();
        return renderPlaceholderToolSettings(page);
    };

    return (
        <div className="settings-stage">
            <div className="window settings-window">
                <header
                    className="titlebar settings-titlebar draggable-titlebar"
                    onMouseDown={startWindowDrag}
                >
                    <div className="brand">
                        <img className="app-brand-icon" src={jujuIcon} alt="" />
                        juju 设置
                    </div>
                    <JuIconButton label="关闭设置" onMouseDown={(event) => event.stopPropagation()} onClick={onBack}>
                        <X size={19} />
                    </JuIconButton>
                </header>
                <div className="settings-body">
                    <aside className="settings-sidebar">
                        <div className="app-nav-group">
                            <JuButton
                                variant="navHead"
                                onClick={() => setAppGroupOpen(!appGroupOpen)}
                            >
                                <span className="nav-head-inner">
                                    <SettingsIcon size={17} />
                                    应用设置
                                </span>
                                <ChevronDown
                                    className="nav-chevron"
                                    size={15}
                                />
                            </JuButton>
                            {appGroupOpen && (
                                <div className="settings-subnav settings-subnav-app">
                                    {appNavigation.map(
                                        ({ id, label, icon: Icon }) => (
                                            <JuButton
                                                key={id}
                                                variant={
                                                    page === id
                                                        ? "navigationActive"
                                                        : "navigation"
                                                }
                                                onClick={() => setPage(id)}
                                            >
                                                <Icon size={17} />
                                                {label}
                                            </JuButton>
                                        ),
                                    )}
                                </div>
                            )}
                        </div>
                        <div className="tools-nav-group">
                            <JuButton
                                variant="navHead"
                                onClick={() => setToolGroupOpen(!toolGroupOpen)}
                            >
                                <span className="nav-head-inner">
                                    <BriefcaseBusiness size={17} />
                                    工具设置
                                </span>
                                <ChevronDown
                                    className="nav-chevron"
                                    size={15}
                                />
                            </JuButton>
                            {toolGroupOpen && (
                                <div className="settings-subnav settings-subnav-tools">
                                    {toolNavigation.map((tool) => {
                                        const Icon = tool.icon ?? Puzzle;
                                        return <JuButton
                                            key={tool.id}
                                            variant={
                                                page === tool.id
                                                    ? "subnavActive"
                                                    : "subnav"
                                            }
                                            onClick={() => setPage(tool.id)}
                                        >
                                            <Icon
                                                className="settings-tool-icon"
                                                size={17}
                                                strokeWidth={1.8}
                                                style={{ color: tool.color }}
                                                aria-hidden="true"
                                            />
                                            {tool.label}
                                        </JuButton>
                                    })}
                                </div>
                            )}
                        </div>
                    </aside>
                    <main className="settings-content-main" key={page}>
                        {error && (
                            <p className="settings-error" role="alert">
                                {error}
                            </p>
                        )}
                        {renderPage()}
                    </main>
                </div>
            </div>
        </div>
    );
}

function SettingsSection({
    title,
    children,
}: {
    title: string;
    children: ReactNode;
}) {
    return (
        <section className="settings-section">
            <h3 className="settings-section-title">{title}</h3>
            <div className="settings-group">{children}</div>
        </section>
    );
}

function SettingRow({
    label,
    children,
    description,
    kind = "control",
}: {
    label: string;
    children: ReactNode;
    description?: string;
    kind?: "control" | "toggle" | "numeric" | "time" | "select" | "shortcut" | "status" | "info";
}) {
    return (
        <div className={`setting-row setting-row--${kind}`}>
            <div className="setting-label">
                {label}
                {description && (
                    <div className="setting-description">{description}</div>
                )}
            </div>
            <div className="setting-control">{children}</div>
        </div>
    );
}
function ValidatedNumberSetting({
    label,
    value,
    min,
    max,
    onSave,
}: {
    label: string;
    value: number;
    min: number;
    max: number;
    onSave: (value: number) => unknown;
}) {
    const [draft, setDraft] = useState(String(value));
    const [error, setError] = useState("");
    const commit = () => {
        const parsed = Number(draft);
        if (!Number.isInteger(parsed) || parsed < min || parsed > max) {
            setDraft(String(value));
            setError(`请输入 ${min} 到 ${max} 之间的整数。`);
            return;
        }
        setError("");
        onSave(parsed);
    };
    return (
        <JuInput
            label={label}
            labelHidden
            type="number"
            min={min}
            max={max}
            step={1}
            value={draft}
            error={error}
            onChange={(event) => setDraft(event.currentTarget.value)}
            onBlur={commit}
        />
    );
}

function ToolSwitch({
    label,
    checked,
    onChange,
}: {
    label: string;
    checked: boolean;
    onChange: (value: boolean) => void;
}) {
    return (
        <JuSwitch
            ariaLabel={label}
            checked={checked}
            onCheckedChange={onChange}
        />
    );
}

function PathRow({
    path,
    editable = false,
    onChange,
}: {
    path: string;
    editable?: boolean;
    onChange?: (path: string) => void;
}) {
    return (
        <div className="setting-row setting-row--path">
            <div className="setting-label path-label">
                <span className="path-name">{editable ? "根目录" : "数据目录"}</span>
                <span className="path-value" title={path}>{path}</span>
            </div>
            <div className="setting-control">
                <JuIconButton
                    label="复制路径"
                    onClick={() => void navigator.clipboard?.writeText(path)}
                >
                    <Copy size={19} />
                </JuIconButton>
                {editable && (
                    <JuIconButton
                        label="修改路径"
                        onClick={() =>
                            void chooseDataRoot().then(
                                (selected) => selected && onChange?.(selected),
                            )
                        }
                    >
                        <Pencil size={19} />
                    </JuIconButton>
                )}
            </div>
        </div>
    );
}

function ShortcutRow({
    label,
    value,
    onSave,
    allowModifiers = false,
    requireModifier = false,
    allowNone = false,
    validateCandidate,
}: {
    label: string;
    value: string;
    onSave: (value: string) => unknown | Promise<unknown>;
    allowModifiers?: boolean;
    requireModifier?: boolean;
    allowNone?: boolean;
    validateCandidate?: (value: string) => string | null;
}) {
    const [open, setOpen] = useState(false);
    const [draft, setDraft] = useState(value);
    const [error, setError] = useState("");
    const capture = (event: KeyboardEvent<HTMLInputElement>) => {
        event.preventDefault();
        event.stopPropagation();
        if (["Control", "Alt", "Shift", "Meta"].includes(event.key)) return;
        const modifiers = [
            event.ctrlKey ? "Ctrl" : "",
            event.altKey ? "Alt" : "",
            event.shiftKey ? "Shift" : "",
            event.metaKey ? "Win" : "",
        ].filter(Boolean);
        const key =
            event.key === " "
                ? "Space"
                : event.key.length === 1
                  ? event.key.toUpperCase()
                  : event.key;
        if (!allowModifiers && (modifiers.length > 0 || key.length !== 1)) {
            setError("此快捷键只能使用单个字母或数字。");
            return;
        }
        if (requireModifier && modifiers.length === 0) {
            setError("主界面快捷键必须包含修饰键。");
            return;
        }
        const next = [...modifiers, key].join(" + ");
        setDraft(next);
        setError(validateCandidate?.(next) ?? "");
    };

    const submit = async () => {
        if (!draft && !allowNone) {
            setError("此快捷键不能清空。");
            return;
        }
        const validationError = validateCandidate?.(draft);
        if (validationError) {
            setError(validationError);
            return;
        }
        const result = await onSave(draft || "None");
        if (result === false) {
            setError("快捷键已存在或无法注册，请更换其他组合。");
            return;
        }
        if (typeof result === "string" && result) {
            setError(result);
            return;
        }
        setOpen(false);
    };

    return (
        <SettingRow kind="shortcut" label={label}>
            <span className="shortcut-value">
                {(value || "None").split("+").map((key, index) => (
                    <kbd key={`${key}-${index}`}>{key.trim()}</kbd>
                ))}
                <JuDialog
                    title="设置快捷键"
                    description="点击输入框后按下需要使用的快捷键组合。"
                    trigger={
                        <JuIconButton
                            label={`修改${label}`}
                            onClick={() => {
                                setDraft(value);
                                setError("");
                                setOpen(true);
                            }}
                        >
                            <Pencil size={19} />
                        </JuIconButton>
                    }
                    open={open}
                    onOpenChange={setOpen}
                    footer={
                        <>
                            <JuDialogClose>
                                <JuButton variant="outline">取消</JuButton>
                            </JuDialogClose>
                            {allowNone && (
                                <JuButton
                                    variant="ghost"
                                    onClick={() => {
                                        setDraft("");
                                        void Promise.resolve(onSave("None"))
                                            .then((result) => {
                                                if (result !== false) setOpen(false);
                                                else setError("快捷键已存在或无法注册，请更换其他组合。");
                                            })
                                            .catch((reason: unknown) =>
                                                setError(String(reason)),
                                            );
                                    }}
                                >
                                    清除
                                </JuButton>
                            )}
                            <JuButton onClick={() => void submit()}>
                                保存
                            </JuButton>
                        </>
                    }
                >
                    <JuInput
                        label="新的快捷键"
                        labelHidden
                        value={draft}
                        readOnly
                        error={error}
                        onKeyDown={capture}
                        placeholder="按下快捷键"
                    />
                </JuDialog>
            </span>
        </SettingRow>
    );
}

function TotoReminderMinutes({
    onSaved,
}: {
    onSaved: (minutes: number) => void;
}) {
    const [value, setValue] = useState("5");
    const [error, setError] = useState("");
    useEffect(() => {
        let active = true;
        void loadTotoDefaultRemindMinutes()
            .then((minutes) => {
                if (active) setValue(String(minutes));
            })
            .catch((reason: unknown) => {
                if (active) setError(String(reason));
            });
        return () => {
            active = false;
        };
    }, []);
    const save = async () => {
        try {
            await saveTotoDefaultRemindMinutes(Number(value));
            onSaved(Number(value));
            setError("");
        } catch (reason) {
            setError(String(reason));
        }
    };
    return (
        <div>
            <JuInput
                label="默认提醒提前分钟"
                labelHidden
                type="number"
                min={0}
                step={1}
                value={value}
                onChange={(event) => setValue(event.currentTarget.value)}
                onBlur={() => void save()}
            />
            {error && <p className="settings-error">{error}</p>}
        </div>
    );
}
