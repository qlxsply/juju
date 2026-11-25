import { JuDateRangePicker, type DateRange, type DateRangePickerProps } from "./JuDateRangePicker";

export function JuPresetDateRangePicker(props: DateRangePickerProps & { value: DateRange; onChange: (value: DateRange) => void; presetLabel?: string }) {
    return <JuDateRangePicker {...props} presetPanelLabel={props.presetLabel ?? "日期范围预设"} />;
}
