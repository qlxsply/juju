import { describe, expect, it } from "vitest";
import { totoCurrentWallTime, totoPickerValueToWallTime, totoReminderFromPlan, totoWallTimeToPickerValue } from "./toto-date-time";

describe("Toto date-time picker conversion", () => {
    it("round-trips the configured time zone without browser-local time", () => {
        expect(totoWallTimeToPickerValue("2026-12-01", "16:00", "Asia/Shanghai")).toBe("2026-12-01T08:00:00Z");
        expect(totoPickerValueToWallTime("2026-12-01T08:00:59.999Z", "Asia/Shanghai")).toEqual({ date: "2026-12-01", time: "16:00" });
        expect(totoPickerValueToWallTime(null, "UTC")).toEqual({ date: "", time: "" });
    });

    it("does not silently resolve DST gaps or repeated wall times", () => {
        expect(totoWallTimeToPickerValue("2026-03-08", "02:30", "America/New_York")).toBe("2026-03-08T02:30");
        expect(totoWallTimeToPickerValue("2026-11-01", "01:30", "America/New_York")).toBe("2026-11-01T01:30");
        expect(totoWallTimeToPickerValue("", "", "UTC")).toBeNull();
    });
    it("calculates a reminder across dates and rejects invalid DST wall times", () => {
        expect(totoReminderFromPlan("2026-01-01", "00:10", 30, "Asia/Shanghai")).toEqual({ date: "2025-12-31", time: "23:40" });
        expect(totoReminderFromPlan("2026-12-01", "16:00", 0, "UTC")).toEqual({ date: "2026-12-01", time: "16:00" });
        expect(totoReminderFromPlan("", "", 5, "UTC")).toEqual({ date: "", time: "" });
        expect(() => totoReminderFromPlan("2026-03-08", "03:10", 30, "America/New_York")).toThrow();
        expect(() => totoReminderFromPlan("2026-11-01", "02:10", 30, "America/New_York")).toThrow();
        expect(() => totoReminderFromPlan("2026-12-01", "16:00", -1, "UTC")).toThrow();
    });
    it("initializes from the explicit system instant and zone at minute precision", () => {
        expect(totoCurrentWallTime({ now: "2026-12-31T15:59:59.900Z", timeZone: "Asia/Shanghai", locale: "zh-CN" }, 200))
            .toEqual({ date: "2027-01-01", time: "00:00" });
    });
});
