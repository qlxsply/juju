import { Temporal } from "@js-temporal/polyfill";
import type { TimeContext, TotoQuickPreview } from "./desktop";

/** Browser-mode equivalent of Toto's Rust quick-entry parser. */
export function parseTotoQuickEntry(input: string, context: TimeContext, defaultMinutes: number): TotoQuickPreview {
    const trimmed = input.trimStart();
    let content: string;
    let suffix: string;
    if (trimmed.startsWith("[")) {
        const end = trimmed.lastIndexOf("]");
        if (end < 1) throw new Error("以 [ 开头的事项内容必须以 ] 结束。");
        content = trimmed.slice(1, end);
        suffix = trimmed.slice(end + 1).trimStart();
        if (suffix && !suffix.startsWith("@")) throw new Error("事项内容后的计划时间必须以 @ 开头。");
    } else {
        const end = trimmed.indexOf("@");
        content = end < 0 ? trimmed : trimmed.slice(0, end);
        suffix = end < 0 ? "" : trimmed.slice(end);
    }
    content = content.trim();
    if (!content) throw new Error("事项内容不能为空。");
    if (/[\r\n]/.test(content)) throw new Error("事项内容不能包含换行。");
    const parts = suffix ? suffix.slice(1).split("@") : [];
    if (parts.length > 2) throw new Error("快速新增最多只能包含计划时间和提前提醒分钟两个分段。");
    const reminderText = parts[1]?.trim() ?? "";
    if (reminderText && !/^\+?\d+$/.test(reminderText)) throw new Error("提醒提前分钟数必须是非负整数。");
    const reminderMinutes = reminderText ? Number(reminderText) : defaultMinutes;
    if (!Number.isSafeInteger(reminderMinutes) || reminderMinutes < 0) throw new Error("提醒提前分钟数必须是非负整数。");
    if (!parts.length) return { content, plannedAt: null, reminderAt: null };
    const inputTime = parts[0].trim().toUpperCase();
    if (!inputTime) throw new Error("计划时间不能为空。");
    const instant = typeof context.now === "number" ? Temporal.Instant.fromEpochMilliseconds(context.now) : Temporal.Instant.from(context.now);
    const now = instant.toZonedDateTimeISO(context.timeZone).toPlainDateTime()
        .with({ millisecond: 0, microsecond: 0, nanosecond: 0 });
    const parseClock = (clock: string) => {
        if (!/^\d{4}$/.test(clock)) throw new Error("时间格式应为 HHmm。");
        return Temporal.PlainTime.from(`${clock.slice(0, 2)}:${clock.slice(2)}`);
    };
    let planned: Temporal.PlainDateTime;
    try {
        if (inputTime.startsWith("+")) {
            const prefix = inputTime.match(/^\++/)![0];
            planned = now.toPlainDate().add({ days: prefix.length }).toPlainDateTime(parseClock(inputTime.slice(prefix.length)));
        } else if (inputTime.startsWith("W")) {
            const prefix = inputTime.match(/^W+/)![0];
            const suffix = inputTime.slice(prefix.length);
            if (!/^\d{4,5}$/.test(suffix)) throw new Error("周计划时间格式无效。");
            const weekday = suffix.length === 5 ? Number(suffix[0]) : now.dayOfWeek;
            if (weekday < 1 || weekday > 7) throw new Error("星期必须为 1 到 7。");
            const monday = now.toPlainDate().subtract({ days: now.dayOfWeek - 1 });
            planned = monday.add({ weeks: prefix.length, days: weekday - 1 }).toPlainDateTime(parseClock(suffix.slice(-4)));
        } else if (inputTime.startsWith("H") || inputTime.startsWith("M")) {
            const match = /^(?:H(\+?\d+)(?:M(\+?\d+))?|M(\+?\d+))$/.exec(inputTime);
            if (!match) throw new Error("相对时间格式无效。");
            const hours = Number(match[1] ?? 0);
            const minutes = Number(match[2] ?? match[3] ?? 0);
            if (!Number.isSafeInteger(hours) || !Number.isSafeInteger(minutes) || hours * 60 + minutes <= 0) throw new Error("相对时间必须大于零。");
            planned = now.add({ hours, minutes });
        } else {
            if (!/^(?:\d{4}|\d{6}|\d{8}|\d{12})$/.test(inputTime)) throw new Error("计划时间格式无效。");
            const clock = parseClock(inputTime.slice(-4));
            const datePart = inputTime.slice(0, -4);
            let date = now.toPlainDate();
            if (datePart.length === 2) date = date.with({ day: Number(datePart) }, { overflow: "reject" });
            if (datePart.length === 4) date = Temporal.PlainDate.from({ year: now.year, month: Number(datePart.slice(0, 2)), day: Number(datePart.slice(2)) }, { overflow: "reject" });
            if (datePart.length === 8) date = Temporal.PlainDate.from({ year: Number(datePart.slice(0, 4)), month: Number(datePart.slice(4, 6)), day: Number(datePart.slice(6)) }, { overflow: "reject" });
            planned = date.toPlainDateTime(clock);
        }
        const format = (value: Temporal.PlainDateTime) => value.toString({ smallestUnit: "second" }).replace("T", " ");
        return { content, plannedAt: format(planned), reminderAt: format(planned.subtract({ minutes: reminderMinutes })) };
    } catch (error) {
        throw new Error(error instanceof RangeError ? "计划日期或时间无效。" : error instanceof Error ? error.message : "计划时间格式无效。", { cause: error });
    }
}
