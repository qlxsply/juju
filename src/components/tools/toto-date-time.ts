import { Temporal } from "@js-temporal/polyfill";

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
