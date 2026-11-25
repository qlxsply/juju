import { Temporal } from "@js-temporal/polyfill";
import type { DateRange } from "./DateRangePicker";

export const dateRangePresets = ["本周", "上一周", "本月", "上一月", "本年", "上一年"] as const;
export function matchingDateRangePreset(value: DateRange, now: string | number, timeZone: string): typeof dateRangePresets[number] | null {
    if (!value.start || !value.end) return null;
    return dateRangePresets.find((preset) => {
        const range = presetDateRange(preset, now, timeZone);
        return range?.start === value.start && range.end === value.end;
    }) ?? null;
}
export function presetDateRange(preset: string, now: string | number, timeZone: string): DateRange | null {
    const instant = typeof now === "number" ? Temporal.Instant.fromEpochMilliseconds(now) : Temporal.Instant.from(now);
    const today = instant.toZonedDateTimeISO(timeZone).toPlainDate();
    const week = today.subtract({ days: today.dayOfWeek - 1 });
    const month = today.with({ day: 1 });
    const year = today.with({ month: 1, day: 1 });
    const ranges: Record<string, [Temporal.PlainDate, Temporal.PlainDate]> = {
        本周: [week, week.add({ days: 6 })],
        上一周: [week.subtract({ days: 7 }), week.subtract({ days: 1 })],
        本月: [month, month.add({ months: 1 }).subtract({ days: 1 })],
        上一月: [month.subtract({ months: 1 }), month.subtract({ days: 1 })],
        本年: [year, year.add({ years: 1 }).subtract({ days: 1 })],
        上一年: [year.subtract({ years: 1 }), year.subtract({ days: 1 })],
    };
    const range = ranges[preset];
    return range ? { start: range[0].toString(), end: range[1].toString() } : null;
}
