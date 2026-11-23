import { useEffect, useState } from "react";
import { isTauri } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { CalendarClock, X } from "lucide-react";
import { JuButton, JuDataTable, JuIconButton, type JuColumn } from "../ju";
import { closeCurrentToolWindow, openTotoToolWindow } from "../../services/desktop";
import { useWindowDrag } from "../../hooks/useWindowDrag";
import { useTimeContext } from "../date-time/time-context";

interface ReminderRow {
    id: string;
    content: string;
}

export function TotoReminderWindow() {
    const startWindowDrag = useWindowDrag();
    const timeContext = useTimeContext();
    const clock = new Intl.DateTimeFormat(timeContext.locale, {
        timeZone: timeContext.timeZone,
        hour: "2-digit",
        minute: "2-digit",
        hourCycle: "h23",
    }).format(new Date(typeof timeContext.now === "number" ? timeContext.now : Date.parse(timeContext.now)));
    const [content, setContent] = useState(() =>
        new URLSearchParams(window.location.search).get("content") ?? "",
    );
    const rows: ReminderRow[] = content
        .split("\n")
        .map((item, index) => ({ id: `${index}-${item}`, content: item }))
        .filter((item) => item.content.trim().length > 0);
    const columns: JuColumn<ReminderRow>[] = [
        { id: "content", header: "提醒事项", cell: (item) => item.content },
    ];

    useEffect(() => {
        if (!isTauri()) return;
        let unlisten: (() => void) | undefined;
        void listen<string>("toto:reminder-update", (event) =>
            setContent((previous) => [...new Set([...previous.split("\n"), ...event.payload.split("\n")])].filter(Boolean).join("\n")),
        ).then((stopListening) => { unlisten = stopListening; });
        return () => unlisten?.();
    }, []);

    const close = async () => {
        await closeCurrentToolWindow();
    };

    return (
        <div className="toto-reminder-window">
            <header className="toto-tool-header draggable-titlebar" onMouseDown={startWindowDrag}>
                <div className="toto-tool-brand"><CalendarClock size={18} /><strong>Toto 提醒</strong></div>
                <div className="toto-tool-actions">
                    <span>{clock}</span>
                    <JuIconButton label="关闭提醒窗口" onMouseDown={(event) => event.stopPropagation()} onClick={() => void close()}><X size={17} /></JuIconButton>
                </div>
            </header>
            <main className="toto-reminder-body">
                <JuDataTable
                    caption="到期的 Toto 提醒"
                    columns={columns}
                    data={rows}
                    getRowId={(item) => item.id}
                    emptyMessage="没有待显示的提醒。"
                />
            </main>
            <footer className="toto-reminder-actions">
                <JuButton variant="outline" onClick={() => void openTotoToolWindow()}>打开 Toto</JuButton>
                <JuButton onClick={() => void close()}>知道了</JuButton>
            </footer>
        </div>
    );
}
