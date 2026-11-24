import { JuDateRangePicker, type DateRange, type DateRangePickerProps } from "./JuDateRangePicker";
import { JuSelect } from "./JuSelect";
import { dateRangePresets, presetDateRange } from "../date-time/date-range-presets";

export function JuPresetDateRangePicker(props: DateRangePickerProps & { value: DateRange; onChange: (value: DateRange) => void; presetLabel?: string }) {
    const selected = dateRangePresets.find((preset) => {
        const range = presetDateRange(preset, props.now, props.timeZone);
        return range?.start === props.value.start && range.end === props.value.end;
    }) ?? (!props.value.start && !props.value.end ? "不限" : "自定义");
    return (
        <div className="ju-preset-date-range">
            <JuSelect ariaLabel={props.presetLabel ?? "日期范围预设"} value={selected} onValueChange={(value) => {
                if (value === "不限") props.onChange({ start: null, end: null });
                else {
                    const range = presetDateRange(value, props.now, props.timeZone);
                    if (range) props.onChange(range);
                }
            }} options={["不限", ...dateRangePresets, "自定义"].map((label) => ({ value: label, label }))} />
            <JuDateRangePicker {...props} />
        </div>
    );
}
