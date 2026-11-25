import { Temporal } from "@js-temporal/polyfill";
import { contextInstant } from "../date-time/temporal-adapter";
import type { PickerTimeContext } from "../date-time/types";

/**
 * Toto stores wall times; JuDateTimePicker accepts absolute instants.
 * Keep invalid or ambiguous legacy values visible as invalid instead of
 * silently choosing a different instant.
 */
export function totoWallTimeToPickerValue(date: string, time: string, timeZone: string): string | null {
    if (!date || !time) return null;
    const wallTime = `${date}T${time}`;
    try {
        return Temporal.PlainDateTime.from(wallTime)
            .toZonedDateTime(timeZone, { disambiguation: "reject" })
            .toInstant().toString();
    } catch {
        return wallTime;
    }
}

export function totoPickerValueToWallTime(value: string | null, timeZone: string): { date: string; time: string } {
    if (!value) return { date: "", time: "" };
    const local = Temporal.Instant.from(value).toZonedDateTimeISO(timeZone).toPlainDateTime()
        .with({ second: 0, millisecond: 0, microsecond: 0, nanosecond: 0 });
    const [date, time] = local.toString({ smallestUnit: "minute" }).split("T");
    return { date, time };
}

/** Advance the Rust-provided instant by monotonic elapsed time, retaining its time zone. */
export function totoCurrentWallTime(context: PickerTimeContext, elapsedMilliseconds: number): { date: string; time: string } {
    const instant = contextInstant(context).add({ milliseconds: Math.max(0, Math.floor(elapsedMilliseconds)) });
    return totoPickerValueToWallTime(instant.toString(), context.timeZone);
}

/** Preserve Toto's existing wall-time subtraction and reject ambiguous DST results. */
export function totoReminderFromPlan(date: string, time: string, minutes: number, timeZone: string): { date: string; time: string } {
    if (!date || !time) return { date: "", time: "" };
    if (!Number.isSafeInteger(minutes) || minutes < 0) throw new RangeError("默认提前提醒分钟必须是非负整数。");
    const planned = Temporal.PlainDateTime.from(`${date}T${time}`);
    planned.toZonedDateTime(timeZone, { disambiguation: "reject" });
    const reminder = planned.subtract({ minutes });
    reminder.toZonedDateTime(timeZone, { disambiguation: "reject" });
    const [reminderDate, reminderTime] = reminder.toString({ smallestUnit: "minute" }).split("T");
    return { date: reminderDate, time: reminderTime };
}
