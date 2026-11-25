import { describe, expect, it } from "vitest";
import { totoPickerValueToWallTime, totoWallTimeToPickerValue } from "./toto-date-time";

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
});
