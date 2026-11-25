import { describe, expect, it } from "vitest";
import { parseTotoQuickEntry } from "./toto-quick-entry";

const context = { now: "2026-11-21T01:00:00Z", timeZone: "Asia/Shanghai", locale: "zh-CN" };

describe("Toto browser quick entry", () => {
    it("parses bracketed content with @ and an optional reminder", () => {
        expect(parseTotoQuickEntry("[确认邮箱test@gamil.com] @1600@", context, 5)).toEqual({
            content: "确认邮箱test@gamil.com", plannedAt: "2026-11-21 16:00:00", reminderAt: "2026-11-21 15:55:00",
        });
        expect(parseTotoQuickEntry("[[这是事项内容] @1700", context, 5).content).toBe("[这是事项内容");
        expect(parseTotoQuickEntry("[a]b@c] @1600@0", context, 5).content).toBe("a]b@c");
        expect(parseTotoQuickEntry("普通事项", context, 5)).toEqual({ content: "普通事项", plannedAt: null, reminderAt: null });
    });

    it("supports complete absolute and case-insensitive relative syntax", () => {
        expect(parseTotoQuickEntry("事项@++1600", context, 5).plannedAt).toBe("2026-11-23 16:00:00");
        expect(parseTotoQuickEntry("事项@w11600", context, 5).plannedAt).toBe("2026-11-23 16:00:00");
        expect(parseTotoQuickEntry("事项@ww11600", context, 5).plannedAt).toBe("2026-11-30 16:00:00");
        expect(parseTotoQuickEntry("事项@w71600", context, 5).plannedAt).toBe("2026-11-29 16:00:00");
        expect(parseTotoQuickEntry("事项@h2m30", context, 5).plannedAt).toBe("2026-11-21 11:30:00");
        expect(parseTotoQuickEntry("事项@m45", context, 5).plannedAt).toBe("2026-11-21 09:45:00");
        expect(parseTotoQuickEntry("事项@202612011600@10", context, 5).reminderAt).toBe("2026-12-01 15:50:00");
    });

    it("rejects incomplete brackets, malformed separators and invalid times", () => {
        for (const input of ["[这是事项内容 @1700", "[内容] 多余@1600", "事项@1600@5@更多", "事项@1600@-1", "事项@", "事项@W周1600", "事项@202602301200"]) {
            expect(() => parseTotoQuickEntry(input, context, 5), input).toThrow();
        }
    });
});
