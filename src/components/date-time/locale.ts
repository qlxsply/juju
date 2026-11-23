import english from "air-datepicker/locale/en";
import chinese from "air-datepicker/locale/zh";
import type { AirDatepickerLocale } from "air-datepicker";
import type { HourCycle } from "./types";

export interface LocaleBundle {
    air: AirDatepickerLocale;
    dateFormat: string;
    timeFormat: string;
    dateTimeSeparator: string;
}

export function localeBundle(
    locale: string,
    hourCycle: HourCycle,
    dateFormatOverride?: string,
    timeFormatOverride?: string,
): LocaleBundle {
    if (locale !== "zh-CN" && locale !== "en-US") throw new Error(`当前支持 zh-CN / en-US，收到：${locale}`);
    const isChinese = locale.toLowerCase().startsWith("zh");
    const imported = (isChinese ? chinese : english) as AirDatepickerLocale & { default?: AirDatepickerLocale };
    const base = imported.default ?? imported;
    const dateFormat = dateFormatOverride ?? (isChinese ? "yyyy-MM-dd" : "MM/dd/yyyy");
    const timeFormat = timeFormatOverride ?? (hourCycle === 12 ? "hh:mm AA" : "HH:mm");
    return {
        air: { ...base, dateFormat, timeFormat },
        dateFormat,
        timeFormat,
        dateTimeSeparator: " ",
    };
}

export function localeText(locale: string) {
    return locale.toLowerCase().startsWith("zh")
        ? {
            today: "今天",
            clear: "清除",
            error: "请输入有效的日期或时间。",
            invalid: "日期或时间格式无效。",
            range: "所选值不在允许范围内。",
            minMax: "最小值不能晚于最大值。",
            required: "此项为必填项。",
        }
        : {
            today: "Today",
            clear: "Clear",
            error: "Enter a valid date or time.",
            invalid: "The date or time is invalid.",
            range: "The value is outside the allowed range.",
            minMax: "The minimum value must not be after the maximum value.",
            required: "This field is required.",
        };
}
