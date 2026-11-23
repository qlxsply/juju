import type { HourCycle, PickerMode } from "./types";
import { localeBundle, type LocaleBundle } from "./locale";
import { splitDateTimeFormat } from "./direct-input";

export interface PickerFormatConfig extends LocaleBundle {
    inputFormat: string;
}

export function resolvePickerFormat({
    mode,
    locale,
    hourCycle,
    format,
    dateFormat,
    timeFormat,
}: {
    mode: PickerMode;
    locale: string;
    hourCycle?: HourCycle;
    format?: string;
    dateFormat?: string;
    timeFormat?: string;
}): PickerFormatConfig {
    const resolvedHourCycle = hourCycle ?? (locale.toLowerCase() === "en-us" ? 12 : 24);
    if (mode === "date") {
        const bundle = localeBundle(locale, resolvedHourCycle, format ?? dateFormat);
        return { ...bundle, inputFormat: bundle.dateFormat };
    }
    if (mode === "time") {
        const bundle = localeBundle(locale, resolvedHourCycle, undefined, format ?? timeFormat);
        return { ...bundle, inputFormat: bundle.timeFormat };
    }

    if (format && !dateFormat && !timeFormat) {
        const split = splitDateTimeFormat(format);
        const bundle = localeBundle(locale, resolvedHourCycle, split.dateFormat, split.timeFormat);
        return { ...bundle, dateTimeSeparator: split.separator, inputFormat: format };
    }
    const bundle = localeBundle(locale, resolvedHourCycle, dateFormat, timeFormat);
    const inputFormat = `${bundle.dateFormat}${bundle.dateTimeSeparator}${bundle.timeFormat}`;
    return { ...bundle, inputFormat };
}
