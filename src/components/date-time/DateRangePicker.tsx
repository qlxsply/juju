import { useEffect, useId, useRef, useState } from "react";
import AirDatepicker from "air-datepicker";
import { CalendarDays } from "lucide-react";
import { dateToCarrier, carrierToWallFields, fieldsToValue, normalizeValue, validateTimeContext } from "./temporal-adapter";
import { parsePickerInput } from "./direct-input";
import { resolvePickerFormat } from "./picker-format";
import { createPopupPositionController } from "./popup-animation";
import type { PickerCommonProps } from "./types";
import { ValidationIndicator } from "../ju/ValidationIndicator";
import { validationMessages } from "../ju/validation";

export interface DateRange { start: string | null; end: string | null }
export interface DateRangePickerProps extends PickerCommonProps {
    value?: DateRange;
    onChange?: (value: DateRange) => void;
    disabledDates?: string[];
    min?: string;
    max?: string;
}

function validateDateRange(value: DateRange, props: DateRangePickerProps): string | null {
    try {
        validateTimeContext(props);
        const start = value.start ? normalizeValue(value.start, "date", props) : null;
        const end = value.end ? normalizeValue(value.end, "date", props) : null;
        if (end && !start) return "请选择开始日期。";
        if (start && end && start > end) return "开始日期不能晚于结束日期。";
        if (props.min && props.max && props.min > props.max) return "最小日期不能晚于最大日期。";
        if ([start, end].some((date) => date && ((props.min && date < props.min) || (props.max && date > props.max)))) return "所选日期不在允许范围内。";
        if (props.disabledDates?.some((date) => (start && date === start) || (end && date === end) || (start && end && date >= start && date <= end))) return "所选范围包含不可选择的日期。";
        return null;
    } catch {
        return "日期或时间格式无效。";
    }
}

export function DateRangePicker(props: DateRangePickerProps) {
    const inputRef = useRef<HTMLInputElement>(null);
    const pickerRef = useRef<AirDatepicker<HTMLInputElement> | null>(null);
    const latest = useRef(props);
    useEffect(() => { latest.current = props; }, [props]);
    const userCommit = useRef(false);
    const [internal, setInternal] = useState<DateRange>({ start: null, end: null });
    const [error, setError] = useState("");
    const id = useId();
    const value = props.value ?? internal;
    const formats = resolvePickerFormat({ mode: "date", locale: props.locale, format: props.format });
    const valueError = validateDateRange(value, props);
    const format = formats.dateFormat;
    const commit = (next: DateRange) => {
        const message = validateDateRange(next, latest.current);
        setError(message ?? "");
        latest.current.onValidationError?.(message);
        if (message) return false;
        userCommit.current = true;
        if (!latest.current.value) setInternal(next);
        latest.current.onChange?.(next);
        return true;
    };

    useEffect(() => {
        const input = inputRef.current;
        if (!input) return;
        const position = createPopupPositionController(latest.current.placement ?? "bottom-start");
        const getDate = (date: Date) => fieldsToValue(carrierToWallFields(date), "date");
        const picker = new AirDatepicker(input, {
            classes: `juju-air-date-picker juju-air-picker-${id.replace(/[^a-zA-Z0-9_-]/g, "")}`,
            container: document.body,
            locale: formats.air,
            dateFormat: format,
            range: true,
            fixedHeight: true,
            multipleDatesSeparator: " - ",
            selectedDates: valueError ? false : [value.start, value.end].filter((date): date is string => !!date).map(dateToCarrier),
            startDate: !valueError && value.start ? dateToCarrier(value.start) : dateToCarrier(fieldsToValue(validateTimeContext(props), "date")),
            minDate: props.min ? dateToCarrier(props.min) : false,
            maxDate: props.max ? dateToCarrier(props.max) : false,
            position: position.position,
            onRenderCell: ({ date, cellType }) => cellType === "day" && latest.current.disabledDates?.includes(getDate(date)) ? { disabled: true } : undefined,
            onBeforeSelect: ({ date, datepicker }) => {
                if (datepicker.selectedDates.length !== 1) return true;
                const dates = [getDate(datepicker.selectedDates[0]), getDate(date)].sort();
                return !validateDateRange({ start: dates[0], end: dates[1] }, latest.current);
            },
            onFocus: ({ date, datepicker }) => {
                const dates = [datepicker.selectedDates[0] && getDate(datepicker.selectedDates[0]), getDate(date)].filter(Boolean).sort() as string[];
                datepicker.$datepicker.classList.toggle("-disabled-range-", dates.length === 2 && !!validateDateRange({ start: dates[0], end: dates[1] }, latest.current));
            },
            onSelect: ({ date }) => {
                const dates = (Array.isArray(date) ? date : date ? [date] : []).map(getDate).sort();
                commit({ start: dates[0] ?? null, end: dates[1] ?? null });
            },
        });
        pickerRef.current = picker;
        const keepFocus = (event: MouseEvent) => {
            if (event.target instanceof Node && picker.$datepicker.contains(event.target)) {
                (picker as unknown as { inFocus: boolean }).inFocus = true;
                event.preventDefault();
            }
        };
        document.addEventListener("mousedown", keepFocus, true);
        return () => {
            document.removeEventListener("mousedown", keepFocus, true);
            position.dispose();
            picker.destroy();
            pickerRef.current = null;
        };
        // Recreate only when presentation/constraints or externally controlled value changes.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [id, props.locale, props.timeZone, props.now, props.format, props.min, props.max, props.placement, JSON.stringify(props.disabledDates)]);

    useEffect(() => {
        if (userCommit.current) { userCommit.current = false; return; }
        const picker = pickerRef.current;
        if (!picker || valueError) return;
        picker.clear({ silent: true });
        const dates = [value.start, value.end].filter((date): date is string => !!date).map(dateToCarrier);
        if (dates.length) void picker.selectDate(dates, { silent: true });
    }, [value.start, value.end, valueError]);

    return <div className="ju-field ju-air-picker-field">
        {(props.label || error || valueError) && <div className="ju-field-heading">
            {props.label && <label htmlFor={id} className={`ju-field-label${props.labelHidden ? " sr-only" : ""}`}>{props.label}</label>}
            <ValidationIndicator fieldId={id} messages={validationMessages({ error: error || valueError || undefined })} hiddenLabel={props.labelHidden || !props.label} />
        </div>}
        <span className="ju-air-picker-control">
            <input ref={inputRef} id={id} type="text" className="ju-input ju-air-picker-input" aria-label={props.label}
                aria-invalid={!!(error || valueError)} aria-describedby={error || valueError ? `${id}-validation` : undefined} disabled={props.disabled} readOnly={props.readOnly}
                placeholder={`${format} - ${format}`} autoComplete="off"
                onFocus={() => { const picker = pickerRef.current; if (picker?.visible && !picker.$datepicker.isConnected) (picker as unknown as { visible: boolean }).visible = false; picker?.show(); picker?.$datepicker.classList.add("-juju-visible-"); }}
                onKeyDown={(event) => {
                    if (event.key !== "Delete" && event.key !== "Backspace") return;
                    event.preventDefault();
                    if (event.repeat || props.disabled || props.readOnly || props.required || props.clearable === false) return;
                    pickerRef.current?.clear({ silent: true });
                    event.currentTarget.value = "";
                    commit({ start: null, end: null });
                }}
                onBlur={(event) => {
                    const text = event.currentTarget.value.trim();
                    if (!text) { if (!props.required && props.clearable !== false) commit({ start: null, end: null }); return; }
                    try {
                        const parts = text.split(" - ");
                        if (parts.length > 2) throw new Error();
                        const dates = parts.map((part) => fieldsToValue(parsePickerInput(part, format, "date", props.locale), "date"));
                        commit({ start: dates[0], end: dates[1] ?? null });
                    } catch { setError("日期或时间格式无效。"); }
                }}
            />
            <span aria-hidden="true" className="ju-air-picker-icon"><CalendarDays size={16} /></span>
        </span>
    </div>;
}
