import { useEffect, useState, type ReactNode } from "react";
import { isTauri } from "@tauri-apps/api/core";
import { getTimeContext, type TimeContext } from "../../services/desktop";
import { DEMO_TIME_CONTEXT, TimeContextStore } from "./time-context";

export function TimeContextProvider({ children }: { children: ReactNode }) {
    const [context, setContext] = useState<TimeContext | null>(() => isTauri() ? null : DEMO_TIME_CONTEXT);
    const [error, setError] = useState("");

    useEffect(() => {
        if (!isTauri()) return;
        let active = true;
        const refresh = async () => {
            try {
                const next = await getTimeContext();
                if (active) {
                    setContext(next);
                    setError("");
                }
            } catch (reason) {
                if (active) setError(String(reason));
            }
        };
        void refresh();
        const timer = window.setInterval(() => void refresh(), 60_000);
        return () => {
            active = false;
            window.clearInterval(timer);
        };
    }, []);

    if (!context) {
        return (
            <div className="time-context-loading" role={error ? "alert" : "status"}>
                {error ? `无法初始化系统时间上下文：${error}` : "正在初始化时间上下文…"}
            </div>
        );
    }

    return <TimeContextStore.Provider value={context}>{children}</TimeContextStore.Provider>;
}
