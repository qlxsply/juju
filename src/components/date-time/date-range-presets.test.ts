import { describe, expect, it } from "vitest";
import { presetDateRange } from "./date-range-presets";

describe("date range presets", () => {
    it("uses the requested time zone and handles month and year boundaries", () => {
        const now = "2025-01-01T00:30:00Z";
        expect(presetDateRange("本周", now, "America/New_York")).toEqual({ start: "2024-12-30", end: "2025-01-05" });
        expect(presetDateRange("上一月", now, "UTC")).toEqual({ start: "2024-12-01", end: "2024-12-31" });
        expect(presetDateRange("上一年", now, "UTC")).toEqual({ start: "2024-01-01", end: "2024-12-31" });
        expect(presetDateRange("本月", "2024-02-12T00:00:00Z", "UTC")).toEqual({ start: "2024-02-01", end: "2024-02-29" });
    });
});
