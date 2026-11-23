import { Temporal } from "@js-temporal/polyfill";
import type { PickerMode } from "./types";
import type { WallFields } from "./temporal-adapter";

type Token = "yyyy" | "yy" | "MMMM" | "MMM" | "MM" | "M" | "dd" | "d" | "HH" | "H" | "hh" | "h" | "mm" | "m" | "aa" | "AA";
type PatternPart = { token: Token } | { literal: string };
const TOKENS: Token[] = ["yyyy", "MMMM", "MMM", "yy", "MM", "dd", "HH", "hh", "M", "d", "H", "h", "mm", "m", "aa", "AA"];

export function parsePickerInput(text: string, pattern: string, mode: PickerMode, locale: string): WallFields {
    const tokens = tokenize(pattern);
    const groups: { token: Token; index: number }[] = [];
    let source = "^";
    let groupIndex = 1;
    for (const part of tokens) {
        if ("literal" in part) {
            source += escapeRegExp(part.literal);
            continue;
        }
        groups.push({ token: part.token, index: groupIndex++ });
        source += `(${tokenPattern(part.token, locale)})`;
    }
    source += "$";
    const match = new RegExp(source, "iu").exec(text.trim());
    if (!match) throw new Error(`请输入符合格式 ${pattern} 的值。`);

    const result: Partial<WallFields> = { year: 2000, month: 1, day: 1, hour: 0, minute: 0, second: 0 };
    let hourCycle12 = false;
    let period: string | undefined;
    for (const group of groups) {
        const raw = match[group.index];
        switch (group.token) {
            case "yyyy": result.year = number(raw); break;
            case "yy": result.year = 2000 + number(raw); break;
            case "MMMM":
            case "MMM": result.month = monthIndex(raw, locale, group.token === "MMMM"); break;
            case "MM":
            case "M": result.month = number(raw); break;
            case "dd":
            case "d": result.day = number(raw); break;
            case "HH":
            case "H": result.hour = number(raw); break;
            case "hh":
            case "h": result.hour = number(raw); hourCycle12 = true; break;
            case "mm":
            case "m": result.minute = number(raw); break;
            case "aa":
            case "AA": period = raw.toLocaleLowerCase("en-US"); break;
        }
    }

    if (mode !== "time" && !groups.some(({ token }) => token === "yyyy" || token === "yy")) {
        throw new Error("日期格式必须包含年份。");
    }
    if (mode !== "time" && !groups.some(({ token }) => ["M", "MM", "MMM", "MMMM"].includes(token))) {
        throw new Error("日期格式必须包含月份。");
    }
    if (mode !== "time" && !groups.some(({ token }) => token === "d" || token === "dd")) {
        throw new Error("日期格式必须包含日。");
    }
    if (mode !== "date" && !groups.some(({ token }) => ["H", "HH", "h", "hh"].includes(token))) {
        throw new Error("时间格式必须包含小时。");
    }
    if (mode !== "date" && !groups.some(({ token }) => token === "m" || token === "mm")) {
        throw new Error("时间格式必须包含分钟。");
    }

    if (hourCycle12) {
        if (!period) throw new Error("12 小时制时间必须包含 AM/PM。");
        if (result.hour! < 1 || result.hour! > 12) throw new Error("12 小时制小时必须在 1 到 12 之间。");
        result.hour = (result.hour! % 12) + (period.startsWith("p") ? 12 : 0);
    }

    try {
        if (mode !== "time") Temporal.PlainDate.from({ year: result.year!, month: result.month!, day: result.day! }, { overflow: "reject" });
        if (mode !== "date") Temporal.PlainTime.from({ hour: result.hour!, minute: result.minute! }, { overflow: "reject" });
    } catch {
        throw new Error(`无效的${mode === "date" ? "日历日期" : mode === "time" ? "时间" : "日期时间"}。`);
    }
    return result as WallFields;
}

export function splitDateTimeFormat(pattern: string): { dateFormat: string; timeFormat: string; separator: string } {
    const separatorMatch = /\s+(?=(?:HH|H|hh|h)\b)/.exec(pattern);
    if (separatorMatch?.index !== undefined) {
        const separator = separatorMatch[0];
        return {
            dateFormat: pattern.slice(0, separatorMatch.index),
            timeFormat: pattern.slice(separatorMatch.index + separator.length),
            separator,
        };
    }
    return { dateFormat: "yyyy-MM-dd", timeFormat: pattern || "HH:mm", separator: " " };
}

function tokenize(pattern: string): PatternPart[] {
    const result: PatternPart[] = [];
    for (let index = 0; index < pattern.length;) {
        const token = TOKENS.find((candidate) => pattern.startsWith(candidate, index));
        if (token) {
            result.push({ token });
            index += token.length;
        } else if (pattern[index] === "'") {
            // UTS-35 quoted literals are copied verbatim for input parsing.
            const end = pattern.indexOf("'", index + 1);
            if (end < 0) throw new Error("日期格式中的引号未闭合。");
            result.push({ literal: pattern.slice(index + 1, end) });
            index = end + 1;
        } else {
            result.push({ literal: pattern[index] });
            index += 1;
        }
    }
    return result;
}

function tokenPattern(token: Token, locale: string): string {
    if (token === "MMMM" || token === "MMM") {
        const names = monthNames(locale, token === "MMMM");
        return names.sort((left, right) => right.length - left.length).map(escapeRegExp).join("|");
    }
    if (token === "aa" || token === "AA") return "(?:AM|PM|a\\.m\\.|p\\.m\\.)";
    return token === "yyyy" ? "\\d{4}" : token === "yy" ? "\\d{2}" : "\\d{1,2}";
}

function monthNames(locale: string, full: boolean) {
    return Array.from({ length: 12 }, (_, month) =>
        new Intl.DateTimeFormat(locale, { month: full ? "long" : "short", timeZone: "UTC" })
            .format(new Date(Date.UTC(2020, month, 1))),
    );
}

function monthIndex(value: string, locale: string, full: boolean) {
    const found = monthNames(locale, full).findIndex((month) => month.toLocaleLowerCase(locale) === value.toLocaleLowerCase(locale));
    if (found < 0) throw new Error(`无法识别月份：${value}`);
    return found + 1;
}

function number(value: string) {
    const parsed = Number(value);
    if (!Number.isInteger(parsed)) throw new Error("日期或时间字段无效。");
    return parsed;
}

function escapeRegExp(value: string) {
    return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
