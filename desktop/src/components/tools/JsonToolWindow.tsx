import { useCallback, useEffect, useMemo, useRef, useState, type DragEvent, type KeyboardEvent as ReactKeyboardEvent } from "react";
import {
    ArrowDown,
    ArrowUp,
    Columns2,
    Copy,
    Download,
    FilePlus2,
    FileText,
    FolderInput,
    Pencil,
    RefreshCw,
    Save,
    Search,
    Settings2,
    Trash2,
    WandSparkles,
    X,
} from "lucide-react";
import { isTauri } from "@tauri-apps/api/core";
import { getCurrentWindow } from "@tauri-apps/api/window";
import Editor, { DiffEditor, loader } from "@monaco-editor/react";
import * as monaco from "monaco-editor/editor/editor.api.js";
import "monaco-editor/language/json/monaco.contribution.js";
import "../../monaco";
import { useWindowDrag } from "../../hooks/useWindowDrag";
import { JuButton, JuIconButton } from "../ju";
import type { AppSettings, JsonDocument, JsonDocumentSnapshot } from "../../services/desktop";
import {
    chooseJsonImportFiles,
    copyJsonDocumentFile,
    closeCurrentToolWindow,
    createJsonDocument,
    deleteJsonDocument,
    importJsonDocuments,
    listJsonDocuments,
    openSettingsWindow,
    openJsonDocument,
    overwriteJsonDocument,
    reorderJsonDocument,
    renameJsonDocument,
    saveJsonDocument,
} from "../../services/desktop";

loader.config({ monaco });

type Status = "已保存" | "有未保存更改" | "正在保存…" | "保存失败" | "外部修改冲突" | "已复制文件";

export function JsonToolWindow({ settings }: { settings: AppSettings }) {
    const startDragging = useWindowDrag();
    const jsonSettings = settings.toolSettings.json ?? {};
    const indentSize = Number(jsonSettings.indentSize ?? 2);
    const autosaveDelay = Number(jsonSettings.autosaveDelayMilliseconds ?? 800);
    const [documents, setDocuments] = useState<JsonDocument[]>([]);
    const [current, setCurrent] = useState<JsonDocumentSnapshot | null>(null);
    const [content, setContent] = useState("");
    const [status, setStatus] = useState<Status>("已保存");
    const [search, setSearch] = useState("");
    const [error, setError] = useState("");
    const [renameId, setRenameId] = useState<string | null>(null);
    const [renameValue, setRenameValue] = useState("");
    const [compareMode, setCompareMode] = useState(false);
    const [compareA, setCompareA] = useState<string | null>(null);
    const [compareB, setCompareB] = useState<string | null>(null);
    const [diff, setDiff] = useState<[string, string] | null>(null);
    const [validJson, setValidJson] = useState(true);
    const [draggedId, setDraggedId] = useState<string | null>(null);
    const [cursor, setCursor] = useState({ line: 1, column: 1 });
    const saveTimer = useRef<number | undefined>(undefined);
    const editorRef = useRef<monaco.editor.IStandaloneCodeEditor | null>(null);
    const contentRef = useRef(content);
    const currentRef = useRef(current);

    useEffect(() => { contentRef.current = content; }, [content]);
    useEffect(() => { currentRef.current = current; }, [current]);

    const refresh = useCallback(async (preferredId?: string) => {
        const rows = await listJsonDocuments();
        const documents = rows.length ? rows : [(await createJsonDocument()).document];
        setDocuments(documents);
        const selected = documents.find((item) => item.id === preferredId) ?? documents[0];
        const snapshot = await openJsonDocument(selected.id);
        setCurrent(snapshot);
        setContent(snapshot.content);
        setStatus("已保存");
        validateJson(snapshot.content);
    }, []);

    useEffect(() => {
        let active = true;
        void Promise.resolve().then(() => refresh()).catch((reason: unknown) => {
            if (active) setError(`初始化 JSON 文档失败：${String(reason)}`);
        });
        return () => {
            active = false;
            if (saveTimer.current !== undefined) window.clearTimeout(saveTimer.current);
        };
    }, [refresh]);

    useEffect(() => {
        if (!isTauri()) return;
        let unlisten: (() => void) | undefined;
        void getCurrentWindow().onDragDropEvent((event) => {
            if (event.payload.type !== "drop") return;
            const paths = event.payload.paths.filter((path) => path.toLowerCase().endsWith(".json"));
            if (!paths.length) return;
            void importJsonDocuments(paths)
                .then((rows) => refresh(rows.at(-1)?.id))
                .catch((reason: unknown) => setError(`导入失败：${String(reason)}`));
        }).then((stopListening) => { unlisten = stopListening; });
        return () => unlisten?.();
    }, [refresh]);

    const visibleDocuments = useMemo(
        () => documents.filter((item) => item.fileName.toLowerCase().includes(search.trim().toLowerCase())),
        [documents, search],
    );

    const save = useCallback(async (force = false) => {
        const snapshot = currentRef.current;
        if (!snapshot) return false;
        setStatus("正在保存…");
        try {
            const revision = force
                ? await overwriteJsonDocument(snapshot.document.id, contentRef.current)
                : await saveJsonDocument(snapshot.document.id, contentRef.current, snapshot.revision);
            const next = { ...snapshot, content: contentRef.current, revision };
            currentRef.current = next;
            setCurrent(next);
            setStatus("已保存");
            setError("");
            return true;
        } catch (reason) {
            const message = String(reason);
            if (message.includes("外部修改")) setStatus("外部修改冲突");
            else setStatus("保存失败");
            setError(message);
            return false;
        }
    }, []);

    useEffect(() => {
        if (!current || status !== "有未保存更改") return;
        if (saveTimer.current !== undefined) window.clearTimeout(saveTimer.current);
        saveTimer.current = window.setTimeout(() => void save(), Math.max(100, autosaveDelay));
        return () => {
            if (saveTimer.current !== undefined) window.clearTimeout(saveTimer.current);
        };
    }, [autosaveDelay, content, current, save, status]);

    useEffect(() => {
        if (!current || status !== "已保存") return;
        let active = true;
        const timer = window.setInterval(() => {
            void openJsonDocument(current.document.id).then((snapshot) => {
                if (!active || currentRef.current?.document.id !== snapshot.document.id
                    || currentRef.current.revision === snapshot.revision) return;
                currentRef.current = snapshot;
                setCurrent(snapshot);
                setContent(snapshot.content);
                validateJson(snapshot.content, setValidJson);
                setStatus("已保存");
                setError("");
            }).catch(() => undefined);
        }, 700);
        return () => { active = false; window.clearInterval(timer); };
    }, [current, status]);

    const selectDocument = async (document: JsonDocument) => {
        if (current && status !== "已保存" && !(await save())) {
            if (status === "外部修改冲突" && window.confirm("文件已被外部修改。确定覆盖磁盘版本并切换文档吗？")) {
                if (!(await save(true))) return;
            } else return;
        }
        if (compareMode) {
            if (compareA === document.id) setCompareA(null);
            else if (compareB === document.id) setCompareB(null);
            else if (!compareA) setCompareA(document.id);
            else if (!compareB) setCompareB(document.id);
            else {
                setCompareA(document.id);
                setCompareB(null);
            }
            return;
        }
        const snapshot = await openJsonDocument(document.id);
        setCurrent(snapshot);
        currentRef.current = snapshot;
        setContent(snapshot.content);
        setStatus("已保存");
        validateJson(snapshot.content);
        setError("");
    };

    const create = async () => {
        if (current && status !== "已保存" && !(await save())) return;
        const snapshot = await createJsonDocument();
        setDocuments(await listJsonDocuments());
        setCurrent(snapshot);
        currentRef.current = snapshot;
        setContent(snapshot.content);
        setStatus("已保存");
        validateJson(snapshot.content);
    };

    const remove = async () => {
        if (!current || !window.confirm(`将“${current.document.fileName}”移入回收目录？`)) return;
        await deleteJsonDocument(current.document.id);
        setCurrent(null);
        await refresh();
    };

    const beginRename = () => {
        if (!current) return;
        setRenameId(current.document.id);
        setRenameValue(current.document.fileName.replace(/\.json$/i, ""));
    };

    const finishRename = async () => {
        if (!renameId) return;
        try {
            await renameJsonDocument(renameId, renameValue);
            setRenameId(null);
            await refresh(renameId);
            setError("");
        } catch (reason) {
            setError(`重命名失败：${String(reason)}`);
        }
    };

    const format = () => {
        try {
            const formatted = JSON.stringify(JSON.parse(content), null, indentSize);
            setContent(formatted);
            contentRef.current = formatted;
            setStatus("有未保存更改");
            setValidJson(true);
            setError("");
        } catch (reason) {
            setError(`JSON 格式错误：${String(reason)}`);
        }
    };

    const runCommand = (command: string) => {
        switch (command) {
            case "New": void create(); break;
            case "Save": void save(); break;
            case "Format": format(); break;
            case "CopyText": void copy(contentRef.current); break;
            case "CopyMinified": void copyMinified(); break;
            case "CopyFile": void downloadCurrent(); break;
            case "FoldAll": void editorRef.current?.getAction("editor.foldAll")?.run(); break;
            case "UnfoldAll": void editorRef.current?.getAction("editor.unfoldAll")?.run(); break;
            case "UnfoldLevel": void editorRef.current?.getAction("editor.unfoldRecursively")?.run(); break;
            case "EnterDiff": setCompareMode(true); setDiff(null); break;
            case "ExitDiff": setDiff(null); setCompareMode(false); setCompareA(null); setCompareB(null); break;
            case "Rename": beginRename(); break;
            case "Delete": void remove(); break;
            default: break;
        }
    };

    const onEditorMount = (editor: monaco.editor.IStandaloneCodeEditor, api: typeof monaco) => {
        editorRef.current = editor;
        editor.onDidChangeCursorPosition(({ position }) => setCursor({
            line: position.lineNumber,
            column: position.column,
        }));
        const shortcutMap = (jsonSettings.shortcuts ?? {}) as Record<string, string>;
        for (const command of ["Save", "Format", "CopyText", "CopyMinified", "CopyFile", "FoldAll", "UnfoldAll", "UnfoldLevel", "EnterDiff"]) {
            const binding = shortcutMap[command] ? toMonacoKeybinding(shortcutMap[command], api) : undefined;
            if (binding === undefined) continue;
            editor.addAction({
                id: `juju.json.${command}`,
                label: command,
                keybindings: [binding],
                run: () => runCommand(command),
            });
        }
    };

    const onDocumentListKeyDown = (event: ReactKeyboardEvent<HTMLDivElement>) => {
        if (event.target instanceof HTMLInputElement) return;
        if (event.key === "ArrowLeft" || event.key === "ArrowRight") {
            event.preventDefault();
            return;
        }
        if ((event.key === "ArrowUp" || event.key === "ArrowDown") && visibleDocuments.length) {
            event.preventDefault();
            const currentIndex = visibleDocuments.findIndex((item) => item.id === current?.document.id);
            const offset = event.key === "ArrowDown" ? 1 : -1;
            void selectDocument(visibleDocuments[(currentIndex + offset + visibleDocuments.length) % visibleDocuments.length]);
            return;
        }
        if (event.key === "Enter" && current) {
            event.preventDefault();
            void selectDocument(current.document);
            return;
        }
        const shortcuts = (jsonSettings.shortcuts ?? {}) as Record<string, string>;
        for (const command of ["New", "Save", "Format", "CopyMinified", "CopyFile", "FoldAll", "UnfoldAll", "UnfoldLevel", "EnterDiff", "Rename", "Delete"]) {
            if (matchesShortcut(shortcuts[command] ?? "", event)) {
                event.preventDefault();
                runCommand(command);
                return;
            }
        }
    };

    const copy = async (value: string) => {
        await navigator.clipboard.writeText(value);
        setError("");
    };

    const copyMinified = async () => {
        try {
            JSON.parse(contentRef.current);
            await copy(minifyJson(contentRef.current));
        } catch (reason) {
            setError(`JSON 格式错误，无法压缩复制：${String(reason)}`);
        }
    };

    const downloadCurrent = async () => {
        if (!current) return;
        if (isTauri()) {
            if (status !== "已保存" && !(await save())) return;
            try {
                await copyJsonDocumentFile(current.document.id);
                setError("");
                setStatus("已复制文件");
            } catch (reason) {
                setError(`复制文件失败：${String(reason)}`);
            }
            return;
        }
        const url = URL.createObjectURL(new Blob([content], { type: "application/json" }));
        const anchor = document.createElement("a");
        anchor.href = url;
        anchor.download = current.document.fileName;
        anchor.click();
        URL.revokeObjectURL(url);
    };

    const startCompare = async () => {
        if (!compareA || !compareB || compareA === compareB) return;
        if (current && status !== "已保存" && !(await save())) return;
        const [left, right] = await Promise.all([openJsonDocument(compareA), openJsonDocument(compareB)]);
        setDiff([projectJsonForDiff(left.content), projectJsonForDiff(right.content)]);
    };

    const reorder = async (targetId: string) => {
        if (!draggedId || draggedId === targetId) return;
        const targetOrder = documents.findIndex((item) => item.id === targetId);
        const moved = await reorderJsonDocument(draggedId, targetOrder);
        setDocuments(moved);
        setDraggedId(null);
    };

    const handleDrop = async (event: DragEvent) => {
        event.preventDefault();
        const files = Array.from(event.dataTransfer.files).filter((file) => file.name.toLowerCase().endsWith(".json"));
        if (!files.length) return;
        const contents = await Promise.all(files.map((file) => file.text()));
        if (isTauri()) {
            setError("请使用“导入文件”按钮选择 JSON 文件。");
            return;
        }
        const imported = await importJsonDocuments(files.map((file) => file.name));
        for (let index = 0; index < files.length; index++) {
            const target = imported.find((item) => item.source.originalFileName === files[index].name);
            if (target) localStorage.setItem(`juju-json-content-${target.id}`, contents[index]);
        }
        await refresh(imported.at(-1)?.id);
    };

    useEffect(() => {
        const onKeyDown = (event: KeyboardEvent) => {
            const target = event.target;
            if (target instanceof HTMLInputElement) return;
            const shortcutMap = (jsonSettings.shortcuts ?? {}) as Record<string, string>;
            if (compareMode && matchesShortcut(shortcutMap.ExitDiff ?? "Escape", event)) {
                event.preventDefault();
                setDiff(null);
                setCompareMode(false);
                setCompareA(null);
                setCompareB(null);
            }
        };
        window.addEventListener("keydown", onKeyDown);
        return () => window.removeEventListener("keydown", onKeyDown);
    }, [compareMode, jsonSettings.shortcuts]);

    const close = async () => {
        if (status !== "已保存" && !(await save())) {
            if (!window.confirm("文档尚未保存，仍要关闭 JSON 工具吗？")) return;
        }
        await closeCurrentToolWindow();
    };

    const importNativeFiles = async () => {
        if (!isTauri()) {
            document.getElementById("json-import-input")?.click();
            return;
        }
        try {
            const paths = await chooseJsonImportFiles();
            if (!paths.length) return;
            const imported = await importJsonDocuments(paths);
            await refresh(imported.at(-1)?.id);
        } catch (reason) {
            setError(`导入失败：${String(reason)}`);
        }
    };

    return (
        <div className="json-tool-window" onDragOver={(event) => event.preventDefault()} onDrop={(event) => void handleDrop(event)}>
            <header className="json-toolbar draggable-titlebar" onMouseDown={startDragging}>
                <div className="json-brand"><FileText size={19} /> <strong>JSON 格式化</strong></div>
                <div className="json-toolbar-actions">
                    <JuButton variant="outline" onClick={() => void create()}><FilePlus2 size={15} /> 新建</JuButton>
                    <JuButton variant="outline" onClick={() => void importNativeFiles()}><FolderInput size={15} /> 导入</JuButton>
                    <input id="json-import-input" type="file" accept=".json,application/json" multiple hidden onChange={(event) => {
                        const selected = Array.from(event.currentTarget.files ?? []);
                        if (!selected.length) return;
                        void Promise.all(selected.map(async (file) => ({ file, content: await file.text() }))).then(async (imports) => {
                            const currentRows = await listJsonDocuments();
                            const rows = await importJsonDocuments(imports.map(({ file }) => file.name));
                            for (const { file, content: importedContent } of imports) {
                                const doc = rows.find((item) => item.source.originalFileName === file.name && !currentRows.some((old) => old.id === item.id));
                                if (doc) localStorage.setItem(`juju-json-content-${doc.id}`, importedContent);
                            }
                            await refresh(rows.at(-1)?.id);
                        });
                        event.currentTarget.value = "";
                    }} />
                    <JuButton variant="outline" onClick={() => void save()}><Save size={15} /> 保存</JuButton>
                    <JuButton variant="outline" onClick={format}><WandSparkles size={15} /> 格式化</JuButton>
                    <JuButton variant="outline" onClick={() => void copy(content)}><Copy size={15} /> 复制</JuButton>
                    <JuButton variant="outline" onClick={() => void copyMinified()}>压缩复制</JuButton>
                    <JuButton variant="outline" onClick={() => void downloadCurrent()}><Download size={15} /> 复制文件</JuButton>
                    <JuButton variant="outline" onClick={() => { setCompareMode(true); setDiff(null); }}><Columns2 size={15} /> 文本对比</JuButton>
                    <JuButton variant="outline" onClick={() => void openSettingsWindow("json")}><Settings2 size={15} /> 设置</JuButton>
                    <JuIconButton label="刷新文档" onClick={() => void refresh(current?.document.id)}><RefreshCw size={16} /></JuIconButton>
                    <JuIconButton label="关闭 JSON 工具" onMouseDown={(event) => event.stopPropagation()} onClick={() => void close()}><X size={17} /></JuIconButton>
                </div>
            </header>
            {compareMode && (
                <div className="json-compare-bar">
                    <span>文本对比：选择左侧列表中的 A、B 文档</span>
                    <span>{compareA ? documents.find((item) => item.id === compareA)?.fileName : "选择 A"} ↔ {compareB ? documents.find((item) => item.id === compareB)?.fileName : "选择 B"}</span>
                    <JuButton disabled={!compareA || !compareB || compareA === compareB} onClick={() => void startCompare()}>开始对比</JuButton>
                    <JuButton variant="outline" disabled={!compareA || !compareB} onClick={() => { setCompareA(compareB); setCompareB(compareA); }}>交换 A/B</JuButton>
                    <JuButton variant="outline" onClick={() => { setCompareA(null); setCompareB(null); setDiff(null); }}>重新选择</JuButton>
                    <JuButton variant="outline" onClick={() => { setCompareMode(false); setCompareA(null); setCompareB(null); setDiff(null); }}>退出对比</JuButton>
                </div>
            )}
            <div className="json-tool-body">
                <aside className="json-document-sidebar">
                    <div className="json-file-heading"><strong>文件列表</strong><span>{documents.length} 个</span></div>
                    <label className="json-filter"><Search size={15} /><input aria-label="搜索文件" value={search} onChange={(event) => setSearch(event.currentTarget.value)} placeholder="搜索文件" /></label>
                    <div className="json-document-list" tabIndex={0} onKeyDown={onDocumentListKeyDown}>
                        {visibleDocuments.map((document) => (
                            <div key={document.id} className={`json-document-row${current?.document.id === document.id ? " selected" : ""}`} draggable={!compareMode} onDragStart={() => setDraggedId(document.id)} onDragOver={(event) => event.preventDefault()} onDrop={() => void reorder(document.id)}>
                                {renameId === document.id ? (
                                    <input aria-label="重命名文档" autoFocus value={renameValue} onChange={(event) => setRenameValue(event.currentTarget.value)} onBlur={() => void finishRename()} onKeyDown={(event) => { if (event.key === "Enter") void finishRename(); if (event.key === "Escape") setRenameId(null); }} />
                                ) : (
                                    <button className="json-document-select" onClick={() => void selectDocument(document)} onDoubleClick={beginRename}>
                                        <FileText size={16} /><span>{document.fileName.replace(/\.json$/i, "")}</span>
                                    </button>
                                )}
                                {compareMode && <div className="json-compare-slots"><button aria-label={`选择 ${document.fileName} 作为 A`} onClick={() => setCompareA(document.id)} className={compareA === document.id ? "active" : ""}>A</button><button aria-label={`选择 ${document.fileName} 作为 B`} onClick={() => setCompareB(document.id)} className={compareB === document.id ? "active" : ""}>B</button></div>}
                            </div>
                        ))}
                    </div>
                    <div className="json-sidebar-actions">
                        <JuIconButton label="重命名文档" disabled={!current} onClick={beginRename}><Pencil size={15} /></JuIconButton>
                        <JuIconButton label="删除文档" disabled={!current} onClick={() => void remove()}><Trash2 size={15} /></JuIconButton>
                        <JuIconButton label="上移文档" disabled={!current || current.document.order === 0} onClick={() => void reorderJsonDocument(current!.document.id, current!.document.order - 1).then(setDocuments)}><ArrowUp size={15} /></JuIconButton>
                        <JuIconButton label="下移文档" disabled={!current || current.document.order >= documents.length - 1} onClick={() => void reorderJsonDocument(current!.document.id, current!.document.order + 1).then(setDocuments)}><ArrowDown size={15} /></JuIconButton>
                    </div>
                </aside>
                    <main className="json-editor-area">
                    {error && (
                        <div role="alert" className="json-error">
                            <span>{error}</span>
                            {status === "外部修改冲突" && current && (
                                <span className="json-conflict-actions">
                                    <JuButton size="compact" variant="outline" onClick={() => void openJsonDocument(current.document.id).then((snapshot) => { setCurrent(snapshot); currentRef.current = snapshot; setContent(snapshot.content); setStatus("已保存"); setError(""); })}>重新加载磁盘</JuButton>
                                    <JuButton size="compact" onClick={() => void save(true)}>确认覆盖</JuButton>
                                    <JuButton size="compact" variant="ghost" onClick={() => void navigator.clipboard.writeText(content).then(async () => { const snapshot = await openJsonDocument(current.document.id); setCurrent(snapshot); currentRef.current = snapshot; setContent(snapshot.content); setStatus("已保存"); setError("当前编辑文本已复制，已重新加载磁盘版本。"); })}>复制当前文本并重载</JuButton>
                                </span>
                            )}
                        </div>
                    )}
                    {diff ? (
                        <div className="json-diff-view">
                            <DiffEditor
                                height="100%"
                                original={diff[0]}
                                modified={diff[1]}
                                language="json"
                                theme={settings.theme === "dark" ? "vs-dark" : "vs"}
                                options={{ readOnly: true, automaticLayout: true, renderSideBySide: true, minimap: { enabled: false } }}
                            />
                        </div>
                    ) : (
                        <>
                            <div className="json-editor-heading"><strong>{current?.document.fileName ?? "正在加载…"}</strong><span>{validJson ? "JSON 有效" : "JSON 格式无效"}</span></div>
                            <Editor
                                height="100%"
                                language="json"
                                theme={settings.theme === "dark" ? "vs-dark" : "vs"}
                                value={content}
                                onMount={onEditorMount}
                                onChange={(next) => {
                                    const nextContent = next ?? "";
                                    setContent(nextContent);
                                    contentRef.current = nextContent;
                                    setStatus("有未保存更改");
                                    validateJson(nextContent, setValidJson);
                                }}
                                options={{
                                    automaticLayout: true,
                                    ariaLabel: "JSON 文档内容",
                                    minimap: { enabled: false },
                                    fontSize: 13,
                                    lineNumbers: "on",
                                    tabSize: indentSize,
                                    insertSpaces: true,
                                    scrollBeyondLastLine: false,
                                    wordWrap: "off",
                                }}
                            />
                        </>
                    )}
                </main>
            </div>
            <footer className="json-status-bar"><span>{status}</span><span>JSON {validJson ? "有效" : "无效"}</span><span>Ln {cursor.line}, Col {cursor.column}</span><span>{current?.document.fileName ?? "—"}</span></footer>
        </div>
    );
}

function validateJson(content: string, setValid: (value: boolean) => void = () => undefined) {
    try {
        JSON.parse(content);
        setValid(true);
    } catch {
        setValid(false);
    }
}

function minifyJson(source: string): string {
    let output = "";
    let inString = false;
    let escaped = false;
    for (const character of source) {
        if (inString) {
            output += character;
            if (escaped) escaped = false;
            else if (character === "\\") escaped = true;
            else if (character === '"') inString = false;
        } else if (character === '"') {
            inString = true;
            output += character;
        } else if (!/\s/.test(character)) output += character;
    }
    return output;
}

function projectJsonForDiff(source: string): string {
    try {
        return JSON.stringify(JSON.parse(source), null, 2);
    } catch {
        return source;
    }
}

function toMonacoKeybinding(shortcut: string, api: typeof monaco): number | undefined {
    const keyNames: Record<string, string> = {
        escape: "Escape",
        delete: "Delete",
        backspace: "Backspace",
        enter: "Enter",
        space: "Space",
        tab: "Tab",
        up: "UpArrow",
        down: "DownArrow",
        left: "LeftArrow",
        right: "RightArrow",
        home: "Home",
        end: "End",
        pageup: "PageUp",
        pagedown: "PageDown",
        insert: "Insert",
    };
    const normalized = shortcut.trim().replace(/\s*\+\s*/g, "+");
    const strokes = normalized.split(/\s+/).filter(Boolean).map((stroke) => {
        const parts = stroke.split("+").map((part) => part.trim());
        const key = parts.pop()?.toLowerCase() ?? "";
        const keyName = keyNames[key] ?? (key.length === 1 && /[a-z]/.test(key) ? `Key${key.toUpperCase()}` : key.length === 1 && /\d/.test(key) ? `Digit${key}` : key);
        const keyCode = (api.KeyCode as unknown as Record<string, number>)[keyName];
        if (keyCode === undefined) return undefined;
        let modifiers = 0;
        if (parts.some((part) => part.toLowerCase() === "ctrl" || part.toLowerCase() === "control")) modifiers |= api.KeyMod.CtrlCmd;
        if (parts.some((part) => part.toLowerCase() === "alt")) modifiers |= api.KeyMod.Alt;
        if (parts.some((part) => part.toLowerCase() === "shift")) modifiers |= api.KeyMod.Shift;
        return modifiers | keyCode;
    });
    if (!strokes.length || strokes.some((stroke) => stroke === undefined) || strokes.length > 2) return undefined;
    return strokes.length === 1 ? strokes[0] : api.KeyMod.chord(strokes[0]!, strokes[1]!);
}

function matchesShortcut(
    shortcut: string,
    event: Pick<KeyboardEvent, "key" | "ctrlKey" | "metaKey" | "altKey" | "shiftKey">,
): boolean {
    if (!shortcut) return false;
    const parts = shortcut.replace(/\s*\+\s*/g, "+").split("+").map((part) => part.trim().toLowerCase());
    const key = parts.at(-1);
    if (!key || event.key.toLowerCase() !== key.toLowerCase()) return false;
    return parts.includes("ctrl") === (event.ctrlKey || event.metaKey)
        && parts.includes("alt") === event.altKey
        && parts.includes("shift") === event.shiftKey;
}
