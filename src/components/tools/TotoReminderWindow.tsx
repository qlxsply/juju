import { useCallback, useEffect, useRef, useState } from "react";
import { invoke, isTauri } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { X } from "lucide-react";
import { JuButton, JuIconButton, JuText } from "../ju";
import { closeCurrentToolWindow, loadTotoItems, type TotoItem } from "../../services/desktop";
import { useWindowDrag } from "../../hooks/useWindowDrag";

export function TotoReminderWindow() {
    const id = new URLSearchParams(window.location.search).get("id") ?? "";
    const [item, setItem] = useState<TotoItem | null>(null);
    const [error, setError] = useState("");
    const [busy, setBusy] = useState(false);
    const busyRef = useRef(false);
    const resolvedRef = useRef(false);
    const startWindowDrag = useWindowDrag();

    const refresh = useCallback(async () => {
        try {
            const items = await loadTotoItems();
            setItem(items.find((entry) => entry.id === id) ?? null);
        } catch (reason) {
            setError(String(reason));
        }
    }, [id]);

    useEffect(() => {
        let disposed = false;
        if (!isTauri()) {
            void loadTotoItems().then((items) => {
                if (!disposed) setItem(items.find((entry) => entry.id === id) ?? null);
            }).catch((reason: unknown) => { if (!disposed) setError(String(reason)); });
            return () => { disposed = true; };
        }
        void listen("toto:reminder-changed", () => void refresh()).then((unlisten) => {
            if (disposed) unlisten();
            else {
                stop = unlisten;
                void refresh();
            }
        });
        let stop: (() => void) | undefined;
        return () => { disposed = true; stop?.(); };
    }, [id, refresh]);

    const resolve = useCallback(async (snooze: boolean) => {
        if (busyRef.current || (!resolvedRef.current && !item?.reminder_at)) return;
        busyRef.current = true;
        setBusy(true);
        try {
            if (!resolvedRef.current) {
                await invoke("resolve_toto_reminder", { id, expected: item?.reminder_at, snooze });
                resolvedRef.current = true;
            }
            await closeCurrentToolWindow();
        } catch (reason) {
            setError(String(reason));
            setBusy(false);
            busyRef.current = false;
        }
    }, [id, item]);

    useEffect(() => {
        if (!isTauri()) return;
        let disposed = false;
        let stop: (() => void) | undefined;
        void getCurrentWindow().onCloseRequested((event) => {
            event.preventDefault();
            void resolve(false);
        }).then((unlisten) => {
            if (disposed) unlisten();
            else stop = unlisten;
        });
        return () => { disposed = true; stop?.(); };
    }, [resolve]);

    useEffect(() => {
        const onKeyDown = (event: KeyboardEvent) => {
            if (event.key === "Escape") {
                event.preventDefault();
                void resolve(false);
            }
        };
        window.addEventListener("keydown", onKeyDown);
        return () => window.removeEventListener("keydown", onKeyDown);
    }, [resolve]);

    return (
        <div className="toto-reminder-window">
            <header className="ju-dialog-heading toto-reminder-heading draggable-titlebar" onMouseDown={startWindowDrag}>
                <div className="ju-dialog-title">Toto 提醒</div>
                <JuIconButton label="关闭提醒窗口" onMouseDown={(event) => event.stopPropagation()} onClick={() => void resolve(false)}><X size={17} /></JuIconButton>
            </header>
            <main className="ju-dialog-body toto-reminder-body">
                {item ? <dl className="toto-item-detail">{[
                    ["事项内容", item.content],
                    ["计划时间", item.planned_at ?? "无"],
                    ["备注", item.notes || "无"],
                ].map(([label, value]) => <div key={label}><dt><JuText>{label}</JuText></dt><dd><JuText>{value}</JuText></dd></div>)}</dl> : <p>{error || "正在加载提醒…"}</p>}
                {error && <p role="alert">{error}</p>}
            </main>
            <footer className="ju-dialog-footer toto-reminder-actions">
                <JuButton variant="outline" disabled={busy || !item} onClick={() => void resolve(true)}>稍等</JuButton>
                <JuButton disabled={busy || !item} onClick={() => void resolve(false)}>收到</JuButton>
            </footer>
        </div>
    );
}
