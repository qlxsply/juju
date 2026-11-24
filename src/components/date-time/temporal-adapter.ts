import { Temporal } from "@js-temporal/polyfill";
import type { Disambiguation, PickerMode, PickerTimeContext, PickerValidationError } from "./types";

export interface WallFields {
    year: number;
    month: number;
    day: number;
    hour: number;
    minute: number;
    second: number;
}

export function changeWallDate(fields: WallFields, change: { year?: number; month?: number }): WallFields {
    const date = Temporal.PlainDate.from({ year: fields.year, month: fields.month, day: fields.day })
        .with(change, { overflow: "constrain" });
    return { ...fields, year: date.year, month: date.month, day: date.day };
}

export class DateTimeValueError extends Error {
    constructor(readonly code: PickerValidationError["code"], message: string) {
        super(message);
        this.name = "DateTimeValueError";
    }
}

export function contextInstant(context: PickerTimeContext): Temporal.Instant {
    try {
        if (typeof context.now === "number") {
            if (!Number.isFinite(context.now)) throw new RangeError("now must be finite epoch milliseconds");
            return Temporal.Instant.fromEpochMilliseconds(context.now);
        }
        return Temporal.Instant.from(context.now);
    } catch (error) {
        throw new DateTimeValueError("invalid-context", `now 必须是带时区的绝对时间：${String(error)}`);
    }
}

export function validateTimeContext(context: PickerTimeContext): WallFields {
    if (!context.timeZone.trim() || !context.locale.trim()) {
        throw new DateTimeValueError("invalid-context", "必须显式提供 IANA 时区和 locale。");
    }
    try {
        const zoned = contextInstant(context).toZonedDateTimeISO(context.timeZone);
        // Validate the language tag without relying on the host's default locale.
        new Intl.DateTimeFormat(context.locale, { timeZone: "UTC" }).format(0);
        return fieldsFromZoned(zoned);
    } catch (error) {
        if (error instanceof DateTimeValueError) throw error;
        throw new DateTimeValueError("invalid-context", `无效的时区或 locale：${String(error)}`);
    }
}

export function fieldsFromZoned(zoned: Temporal.ZonedDateTime): WallFields {
    return {
        year: zoned.year,
        month: zoned.month,
        day: zoned.day,
        hour: zoned.hour,
        minute: zoned.minute,
        second: zoned.second,
    };
}

/**
 * Date is used only as Air Datepicker's local calendar-field carrier. Its epoch
 * value is never interpreted or returned as a business timestamp.
 */
export function wallFieldsToCarrier(fields: WallFields): Date {
    const carrier = new Date(0);
    carrier.setFullYear(fields.year, fields.month - 1, fields.day);
    carrier.setHours(fields.hour, fields.minute, fields.second, 0);
    return carrier;
}

export function carrierToWallFields(carrier: Date): WallFields {
    return {
        year: carrier.getFullYear(),
        month: carrier.getMonth() + 1,
        day: carrier.getDate(),
        hour: carrier.getHours(),
        minute: carrier.getMinutes(),
        second: carrier.getSeconds(),
    };
}

export function instantToCarrier(value: string, timeZone: string): Date {
    try {
        return wallFieldsToCarrier(fieldsFromZoned(Temporal.Instant.from(value).toZonedDateTimeISO(timeZone)));
    } catch (error) {
        throw new DateTimeValueError("invalid-value", `无效的绝对日期时间：${String(error)}`);
    }
}

export function dateToCarrier(value: string): Date {
    try {
        const date = Temporal.PlainDate.from(value, { overflow: "reject" });
        return wallFieldsToCarrier({
            year: date.year,
            month: date.month,
            day: date.day,
            hour: 12,
            minute: 0,
            second: 0,
        });
    } catch (error) {
        if (error instanceof DateTimeValueError) throw error;
        throw new DateTimeValueError("invalid-value", `无效的日历日期：${value}`);
    }
}

export function timeToCarrier(value: string, context: PickerTimeContext): Date {
    const fields = validateTimeContext(context);
    const match = /^(\d{2}):(\d{2})(?::(\d{2}))?$/.exec(value);
    if (!match) throw new DateTimeValueError("invalid-value", "时间必须使用 HH:mm 格式。");
    const hour = Number(match[1]);
    const minute = Number(match[2]);
    const second = Number(match[3] ?? 0);
    try {
        Temporal.PlainTime.from({ hour, minute, second }, { overflow: "reject" });
    } catch {
        throw new DateTimeValueError("invalid-value", `无效的时间：${value}`);
    }
    return wallFieldsToCarrier({ ...fields, year: 2000, month: 1, day: 1, hour, minute, second });
}

export function fieldsToValue(fields: WallFields, mode: PickerMode, timeZone?: string, disambiguation: Disambiguation = "reject"): string {
    if (mode === "date") {
        return `${pad(fields.year, 4)}-${pad(fields.month)}-${pad(fields.day)}`;
    }
    if (mode === "time") {
        // Preserve Toto's established HH:mm storage/API format.
        return `${pad(fields.hour)}:${pad(fields.minute)}`;
    }
    if (!timeZone) throw new DateTimeValueError("invalid-context", "DateTimePicker 必须指定时区。");
    return resolveWallTime(fields, timeZone, disambiguation).toInstant().toString({ smallestUnit: "second" });
}

export function wallDateTimeToInstant(
    fields: WallFields,
    timeZone: string,
    disambiguation: Disambiguation,
): string {
    return resolveWallTime(fields, timeZone, disambiguation).toInstant().toString({ smallestUnit: "second" });
}

export function dateTimeCarrierToInstant(
    carrier: Date,
    timeZone: string,
    disambiguation: Disambiguation,
): string {
    return wallDateTimeToInstant(carrierToWallFields(carrier), timeZone, disambiguation);
}

export function instantToWallFields(value: string, timeZone: string): WallFields {
    try {
        return fieldsFromZoned(Temporal.Instant.from(value).toZonedDateTimeISO(timeZone));
    } catch (error) {
        throw new DateTimeValueError("invalid-value", `无效的绝对日期时间：${String(error)}`);
    }
}

export function normalizeValue(value: string, mode: PickerMode, context: PickerTimeContext): string {
    if (value === "") return "";
    if (mode === "date") {
        const date = Temporal.PlainDate.from(value, { overflow: "reject" });
        return `${pad(date.year, 4)}-${pad(date.month)}-${pad(date.day)}`;
    }
    if (mode === "time") {
        const seconds = /^(\d{2}):(\d{2}):(\d{2})(?:[.,]\d+)?$/.exec(value)?.[3];
        // Temporal accepts ISO leap-second notation, but this control has no
        // leap-second semantics and must not silently normalize :60 to :59.
        if (seconds !== undefined && Number(seconds) > 59) {
            throw new DateTimeValueError("invalid-value", `无效的时间：${value}`);
        }
        const time = Temporal.PlainTime.from(value, { overflow: "reject" });
        return `${pad(time.hour)}:${pad(time.minute)}`;
    }
    // Parse as an instant first. Time-zone changes can then only alter its display.
    try {
        Temporal.Instant.from(value);
        Temporal.Instant.from(value).toZonedDateTimeISO(context.timeZone);
        return Temporal.Instant.from(value).toString();
    } catch (error) {
        throw new DateTimeValueError("invalid-value", `DateTimePicker 必须接收绝对 ISO 时间：${String(error)}`);
    }
}

export function compareModeValues(left: string, right: string, mode: PickerMode): number {
    if (mode === "datetime") {
        const a = Temporal.Instant.from(left).epochNanoseconds;
        const b = Temporal.Instant.from(right).epochNanoseconds;
        return a < b ? -1 : a > b ? 1 : 0;
    }
    if (mode === "date") return Temporal.PlainDate.compare(left, right);
    return Temporal.PlainTime.compare(left, right);
}

export function valueToCarrier(
    value: string | null,
    mode: PickerMode,
    context: PickerTimeContext,
): Date | undefined {
    if (value === null || value === "") return undefined;
    if (mode === "date") return dateToCarrier(value);
    if (mode === "time") return timeToCarrier(value, context);
    return instantToCarrier(value, context.timeZone);
}

function resolveWallTime(fields: WallFields, timeZone: string, disambiguation: Disambiguation) {
    let plain: Temporal.PlainDateTime;
    try {
        plain = Temporal.PlainDateTime.from({ ...fields, millisecond: 0, microsecond: 0, nanosecond: 0 }, { overflow: "reject" });
    } catch {
        throw new DateTimeValueError("invalid-value", "日期或时间字段无效。");
    }

    try {
        const earlier = plain.toZonedDateTime(timeZone, { disambiguation: "earlier" });
        const later = plain.toZonedDateTime(timeZone, { disambiguation: "later" });
        const earlierMatches = earlier.toPlainDateTime().equals(plain);
        const laterMatches = later.toPlainDateTime().equals(plain);
        if (!earlierMatches && !laterMatches) {
            throw new DateTimeValueError("nonexistent-local-time", `该本地时间在 ${timeZone} 不存在。`);
        }
        if (earlierMatches && laterMatches && earlier.epochNanoseconds !== later.epochNanoseconds) {
            if (disambiguation === "reject") {
                throw new DateTimeValueError("ambiguous-local-time", `该本地时间在 ${timeZone} 出现两次，请选择 earlier 或 later。`);
            }
            return disambiguation === "earlier" ? earlier : later;
        }
        return earlierMatches ? earlier : later;
    } catch (error) {
        if (error instanceof DateTimeValueError) throw error;
        throw new DateTimeValueError("invalid-value", `无法在时区 ${timeZone} 解析该日期时间：${String(error)}`);
    }
}

function pad(value: number, length = 2) {
    return String(value).padStart(length, "0");
}
