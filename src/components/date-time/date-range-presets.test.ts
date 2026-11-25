import { describe, expect, it } from "vitest";
import { matchingDateRangePreset, presetDateRange } from "./date-range-presets";

describe("date range presets", () => {
    it("uses the requested time zone and handles month and year boundaries", () => {
        const now = "2025-01-01T00:30:00Z";
        expect(presetDateRange("本周", now, "America/New_York")).toEqual({ start: "2024-12-30", end: "2025-01-05" });
        expect(presetDateRange("上一月", now, "UTC")).toEqual({ start: "2024-12-01", end: "2024-12-31" });
        expect(presetDateRange("上一年", now, "UTC")).toEqual({ start: "2024-01-01", end: "2024-12-31" });
        expect(presetDateRange("本月", "2024-02-12T00:00:00Z", "UTC")).toEqual({ start: "2024-02-01", end: "2024-02-29" });
    });
    it("matches only complete exact ranges and respects the preset order", () => {
        const now = "2024-01-15T12:00:00Z";
        expect(matchingDateRangePreset({ start: null, end: null }, now, "UTC")).toBeNull();
        expect(matchingDateRangePreset({ start: "2024-01-01", end: null }, now, "UTC")).toBeNull();
        expect(matchingDateRangePreset({ start: "2024-01-01", end: "2024-01-30" }, now, "UTC")).toBeNull();
        expect(matchingDateRangePreset({ start: "2024-01-01", end: "2024-01-31" }, now, "UTC")).toBe("本月");
        expect(matchingDateRangePreset({ start: "2024-01-01", end: "2024-01-31" }, "2024-01-01T00:30:00Z", "America/New_York")).toBeNull();
    });
});
