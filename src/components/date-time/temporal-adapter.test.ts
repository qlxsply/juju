import { describe, expect, it } from "vitest";
import {
    contextInstant,
    changeWallDate,
    fieldsFromZoned,
    normalizeValue,
    validateTimeContext,
    wallDateTimeToInstant,
    type WallFields,
} from "./temporal-adapter";
import { parsePickerInput } from "./direct-input";

describe("explicit date-time zones", () => {
    it("constrains year and month changes to the last valid day", () => {
        const base: WallFields = { year: 2026, month: 3, day: 31, hour: 10, minute: 30, second: 0 };
        const leap = changeWallDate(changeWallDate(base, { year: 2024 }), { month: 2 });
        expect(leap).toEqual({ ...base, year: 2024, month: 2, day: 29 });
        expect(changeWallDate(leap, { year: 2025 }).day).toBe(28);
    });
    it("projects one absolute now into its requested business zones", () => {
        const now = "2026-11-23T12:30:00Z";
        const newYork = contextInstant({ now, timeZone: "America/New_York", locale: "en-US" })
            .toZonedDateTimeISO("America/New_York");
        const shanghai = contextInstant({ now, timeZone: "Asia/Shanghai", locale: "zh-CN" })
            .toZonedDateTimeISO("Asia/Shanghai");

        expect(fieldsFromZoned(newYork)).toMatchObject({ year: 2026, month: 11, day: 23, hour: 7, minute: 30 });
        expect(fieldsFromZoned(shanghai)).toMatchObject({ year: 2026, month: 11, day: 23, hour: 20, minute: 30 });
    });

    it("keeps plain dates and times free of timezone conversion", () => {
        const context = { now: "2026-11-23T12:30:00Z", timeZone: "America/New_York", locale: "en-US" };
        expect(normalizeValue("2026-11-23", "date", context)).toBe("2026-11-23");
        expect(normalizeValue("20:30", "time", context)).toBe("20:30");
    });

    it("rejects out-of-range time components, including leap-second notation", () => {
        const context = { now: "2026-11-23T12:30:00Z", timeZone: "UTC", locale: "zh-CN" };
        expect(normalizeValue("23:59", "time", context)).toBe("23:59");
        for (const value of ["24:00", "12:60", "12:59:60", "12:59:99"]) {
            expect(() => normalizeValue(value, "time", context)).toThrow();
        }
    });

    it("rejects nonexistent DST wall times instead of moving them forward", () => {
        const gap: WallFields = { year: 2026, month: 3, day: 8, hour: 2, minute: 30, second: 0 };
        expect(() => wallDateTimeToInstant(gap, "America/New_York", "earlier"))
            .toThrow(/不存在/);
    });

    it("requires an explicit earlier/later strategy for repeated DST wall times", () => {
        const repeated: WallFields = { year: 2026, month: 11, day: 1, hour: 1, minute: 30, second: 0 };
        expect(() => wallDateTimeToInstant(repeated, "America/New_York", "reject"))
            .toThrow(/出现两次/);
        const earlier = wallDateTimeToInstant(repeated, "America/New_York", "earlier");
        const later = wallDateTimeToInstant(repeated, "America/New_York", "later");
        expect(Date.parse(later) - Date.parse(earlier)).toBe(60 * 60 * 1000);
    });

    it("requires valid explicit timezone and locale context", () => {
        expect(() => validateTimeContext({ now: "2026-11-23T12:30:00Z", timeZone: "Not/AZone", locale: "zh-CN" }))
            .toThrow(/时区或 locale/);
        expect(() => contextInstant({ now: "2026-11-23T12:30", timeZone: "UTC", locale: "en-US" }))
            .toThrow(/绝对时间/);
    });
});

describe("localized direct input parsing", () => {
    it("parses locale-formatted dates/times into wall fields, independent of zone", () => {
        expect(parsePickerInput("11/23/2026 07:30 AM", "MM/dd/yyyy hh:mm aa", "datetime", "en-US"))
            .toMatchObject({ year: 2026, month: 11, day: 23, hour: 7, minute: 30 });
        expect(parsePickerInput("2026-11-23 20:30", "yyyy-MM-dd HH:mm", "datetime", "zh-CN"))
            .toMatchObject({ year: 2026, month: 11, day: 23, hour: 20, minute: 30 });
    });

    it("rejects malformed and impossible input", () => {
        expect(() => parsePickerInput("2026-02-30", "yyyy-MM-dd", "date", "zh-CN"))
            .toThrow(/无效/);
        expect(() => parsePickerInput("25:00", "HH:mm", "time", "zh-CN"))
            .toThrow(/无效/);
    });
});
