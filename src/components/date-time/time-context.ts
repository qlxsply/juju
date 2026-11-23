import { createContext, useContext } from "react";
import type { TimeContext } from "../../services/desktop";

export const DEMO_TIME_CONTEXT: TimeContext = {
    now: "2026-11-23T12:30:00Z",
    timeZone: "Asia/Shanghai",
    locale: "zh-CN",
};

export const TimeContextStore = createContext<TimeContext | null>(null);

export function useTimeContext(): TimeContext {
    const context = useContext(TimeContextStore);
    if (!context) throw new Error("日期时间组件必须在 TimeContextProvider 中使用。");
    return context;
}
