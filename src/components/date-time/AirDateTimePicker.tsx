import { useEffect, useId, useMemo, useRef, useState, type FocusEvent } from "react";
import AirDatepicker, { type AirDatepickerOptions } from "air-datepicker";
import { CalendarDays, Clock3 } from "lucide-react";
import {
    carrierToWallFields,
    compareModeValues,
    contextInstant,
    dateTimeCarrierToInstant,
    fieldsToValue,
    instantToCarrier,
    normalizeValue,
    validateTimeContext,
    valueToCarrier,
    wallFieldsToCarrier,
    DateTimeValueError,
    type WallFields,
} from "./temporal-adapter";
import { parsePickerInput } from "./direct-input";
import { localeText } from "./locale";
import { resolvePickerFormat } from "./picker-format";
import { createPopupPositionController } from "./popup-animation";
import type {
    DatePickerProps,
    DateTimePickerProps,
    Disambiguation,
    HourCycle,
    PickerCommonProps,
    PickerMode,
    TimePickerProps,
} from "./types";

export interface AirPickerProps extends PickerCommonProps {
    mode: PickerMode;
    value?: string | null;
    defaultValue?: string | null;
    min?: string;
    max?: string;
    onChange?: (value: string | null) => void;
    onBlur?: (value: string | null) => void;
    dateFormat?: string;
    timeFormat?: string;
    hourCycle?: HourCycle;
    minuteStep?: number;
    disambiguation?: Disambiguation;
}

const defaultDisambiguation: Disambiguation = "reject";

function nowDefault(mode: PickerMode, props: AirPickerProps): string | null {
    if (mode === "date") return null;
    const instant = contextInstant(props);
    if (mode === "datetime") return instant.toString();
    const zoned = instant.toZonedDateTimeISO(props.timeZone);
    return `${String(zoned.hour).padStart(2, "0")}:${String(zoned.minute).padStart(2, "0")}`;
}

function toWallValue(fields: WallFields, props: AirPickerProps): string {
    if (props.mode !== "datetime") return fieldsToValue(fields, props.mode);
    return dateTimeCarrierToInstant(fieldsToCarrier(fields), props.timeZone, props.disambiguation ?? defaultDisambiguation);
}

function fieldsToCarrier(fields: WallFields) {
    // This Date is only a wall-field carrier for Air Datepicker, never a business instant.
    return wallFieldsToCarrier(fields);
}

function getBounds(props: AirPickerProps) {
    const min = props.min ? normalizeValue(props.min, props.mode, props) : null;
    const max = props.max ? normalizeValue(props.max, props.mode, props) : null;
    if (min && max && compareModeValues(min, max, props.mode) > 0) {
        throw new DateTimeValueError("min-max", localeText(props.locale).minMax);
    }
    return { min, max };
}

function isInBounds(value: string, props: AirPickerProps, bounds: ReturnType<typeof getBounds>) {
    if (bounds.min && compareModeValues(value, bounds.min, props.mode) < 0) return false;
    if (bounds.max && compareModeValues(value, bounds.max, props.mode) > 0) return false;
    return true;
}

function toCarrier(value: string | null, props: AirPickerProps): Date | undefined {
    if (!value) return undefined;
    return valueToCarrier(value, props.mode, props);
}

function toNowCarrier(props: AirPickerProps): Date {
    return instantToCarrier(contextInstant(props).toString(), props.timeZone);
}

function makePickerOptions(
    props: AirPickerProps,
    formats: ReturnType<typeof resolvePickerFormat>,
    selectedValue: string | null,
    pickerClass: string,
    position: ReturnType<typeof createPopupPositionController> | null,
    onSelect: (date: Date | Date[]) => void,
): AirDatepickerOptions<HTMLInputElement> {
    const bounds = getBounds(props);
    const selected = toCarrier(selectedValue, props);
    const todayCarrier = toNowCarrier(props);
    return {
        classes: pickerClass,
        container: document.body,
        locale: formats.air,
        startDate: selected ?? todayCarrier,
        selectedDates: selected ? [selected] : false,
        dateFormat: formats.dateFormat,
        timepicker: props.mode !== "date",
        onlyTimepicker: props.mode === "time",
        timeFormat: formats.timeFormat,
        dateTimeSeparator: formats.dateTimeSeparator,
        minDate: bounds.min ? toCarrier(bounds.min, props) : false,
        maxDate: bounds.max ? toCarrier(bounds.max, props) : false,
        minHours: 0,
        maxHours: 23,
        minMinutes: 0,
        maxMinutes: 59,
        minutesStep: props.minuteStep ?? 1,
        showEvent: "focus",
        keyboardNav: true,
        toggleSelected: props.clearable !== false && !props.required,
        position: position?.position,
        onRenderCell: ({ date, cellType }) => {
            if (cellType !== "day") return;
            const today = validateTimeContext(props);
            if (date.getFullYear() === today.year && date.getMonth() + 1 === today.month && date.getDate() === today.day) {
                return { classes: "-juju-business-today-" };
            }
        },
        onSelect: ({ date }) => onSelect(date),
    };
}

export function AirDateTimePicker(props: AirPickerProps) {
    const inputRef = useRef<HTMLInputElement>(null);
    const pickerRef = useRef<AirDatepicker<HTMLInputElement> | null>(null);
    const positionRef = useRef<ReturnType<typeof createPopupPositionController> | null>(null);
    const skipInitialSync = useRef(false);
    const latestProps = useRef(props);
    const directInputRef = useRef(false);
    const userCommitRef = useRef<{ value: string | null } | null>(null);
    const [internalValue, setInternalValue] = useState<string | null | undefined>(undefined);
    const controlled = props.value !== undefined;
    const defaultValue = props.defaultValue !== undefined
        ? props.defaultValue
        : (() => {
            try {
                return nowDefault(props.mode, props);
            } catch {
                return null;
            }
        })();
    const value = controlled ? props.value ?? null : internalValue !== undefined ? internalValue : defaultValue;
    const [validationMessage, setValidationMessage] = useState("");
    const lastCommittedValue = useRef<string | null>(value ?? null);
    const generatedInputId = useId();
    const inputId = props.id ?? generatedInputId;
    const errorId = useId();
    const pickerClass = `juju-air-date-picker juju-air-picker-${inputId.replace(/[^a-zA-Z0-9_-]/g, "")}`;

    useEffect(() => {
        latestProps.current = props;
    }, [props]);

    const contextState = useMemo(() => {
        try {
            validateTimeContext(props);
            const formats = resolvePickerFormat({
                mode: props.mode,
                locale: props.locale,
                hourCycle: props.hourCycle,
                format: props.format,
                dateFormat: props.dateFormat,
                timeFormat: props.timeFormat,
            });
            getBounds(props);
            return { formats, error: "" };
        } catch (error) {
            return { formats: null, error: error instanceof Error ? error.message : String(error) };
        }
    }, [props]);

    const selectionState = useMemo(() => {
        try {
            if (value === null || value === "") return { value: null, error: "" };
            const normalized = normalizeValue(value, props.mode, props);
            if (!isInBounds(normalized, props, getBounds(props))) {
                throw new DateTimeValueError("outside-range", localeText(props.locale).range);
            }
            return { value: normalized, error: "" };
        } catch (error) {
            return { value: null, error: error instanceof Error ? error.message : String(error) };
        }
    }, [value, props]);

    const reportError = (message: string) => {
        setValidationMessage(message);
        latestProps.current.onValidationError?.(message || null);
    };

    const commit = (rawValue: string | null) => {
        const current = latestProps.current;
        try {
            const next = rawValue === null || rawValue === "" ? null : normalizeValue(rawValue, current.mode, current);
            if (next && !isInBounds(next, current, getBounds(current))) {
                throw new DateTimeValueError("outside-range", localeText(current.locale).range);
            }
            reportError("");
            if (!controlled) setInternalValue(next);
            lastCommittedValue.current = next;
            userCommitRef.current = { value: next };
            if (next !== value) current.onChange?.(next);
            return next;
        } catch (error) {
            reportError(error instanceof Error ? error.message : localeText(current.locale).invalid);
            return null;
        }
    };

    const onAirSelect = (date: Date | Date[]) => {
        if (directInputRef.current) return;
        const selected = Array.isArray(date) ? date[0] : date;
        if (!selected) {
            if (latestProps.current.clearable !== false && !latestProps.current.required) {
                commit(null);
                latestProps.current.onBlur?.(null);
            }
            return;
        }
        const current = latestProps.current;
        try {
            const output = toWallValue(carrierToWallFields(selected), current);
            commit(output);
        } catch (error) {
            reportError(error instanceof Error ? error.message : localeText(current.locale).invalid);
        }
    };

    const processTypedValue = (input: HTMLInputElement, current: AirPickerProps, inputFormat: string): string | null | undefined => {
        const text = input.value.trim();
        if (!text) {
            if (current.required) {
                reportError(localeText(current.locale).required);
                return undefined;
            }
            return commit(null);
        }
        try {
            const fields = parsePickerInput(text, inputFormat, current.mode, current.locale);
            const result = toWallValue(fields, current);
            if (!isInBounds(result, current, getBounds(current))) {
                throw new DateTimeValueError("outside-range", localeText(current.locale).range);
            }
            reportError("");
            const carrier = toCarrier(result, current);
            if (carrier) void pickerRef.current?.selectDate(carrier, { silent: true, updateTime: true });
            return commit(result);
        } catch (error) {
            reportError(error instanceof Error ? error.message : localeText(current.locale).invalid);
            return undefined;
        }
    };

    const getOptions = (current: AirPickerProps, selectedValue: string | null) => {
        const formats = resolvePickerFormat({
            mode: current.mode,
            locale: current.locale,
            hourCycle: current.hourCycle,
            format: current.format,
            dateFormat: current.dateFormat,
            timeFormat: current.timeFormat,
        });
        return makePickerOptions(current, formats, selectedValue, pickerClass, positionRef.current, onAirSelect);
    };

    useEffect(() => {
        const input = inputRef.current;
        const current = latestProps.current;
        if (!input || contextState.error) return;
        positionRef.current = createPopupPositionController(current.placement ?? "bottom-start");
        const picker = new AirDatepicker(input, getOptions(current, selectionState.value));
        pickerRef.current = picker;
        skipInitialSync.current = true;
        if (selectionState.error && value) input.value = value;

        const keepInputFocusedWhilePointerSelecting = (event: MouseEvent) => {
            const target = event.target;
            if (!(target instanceof Node) || !picker.$datepicker.contains(target)) return;
            // Set Air Datepicker's focus guard before the browser's default mousedown focus transfer.
            (picker as unknown as { inFocus: boolean }).inFocus = true;
            if (!(target instanceof Element) || !target.closest("input[type=range]")) event.preventDefault();
        };
        document.addEventListener("mousedown", keepInputFocusedWhilePointerSelecting, true);

        return () => {
            document.removeEventListener("mousedown", keepInputFocusedWhilePointerSelecting, true);
            positionRef.current?.dispose();
            positionRef.current = null;
            picker.destroy();
            pickerRef.current = null;
        };
        // One Air Datepicker instance per input; callbacks read latestProps.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [props.mode, props.placement, contextState.error]);

    useEffect(() => {
        const picker = pickerRef.current;
        const current = latestProps.current;
        if (!picker || contextState.error) return;
        if (skipInitialSync.current) {
            skipInitialSync.current = false;
            return;
        }
        if (userCommitRef.current && userCommitRef.current.value === selectionState.value) {
            userCommitRef.current = null;
            lastCommittedValue.current = selectionState.value;
            return;
        }
        picker.update(getOptions(current, selectionState.value), { silent: true });
        if (selectionState.value) {
            const carrier = toCarrier(selectionState.value, current);
            if (carrier) void picker.selectDate(carrier, { silent: true, updateTime: true });
        } else {
            picker.clear({ silent: true });
        }
        if (selectionState.error && value) picker.$el.value = value;
        lastCommittedValue.current = selectionState.value;
        if (props.disabled && picker.visible) picker.hide();
        // External value/now/zone/locale/limit updates are silent.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [contextState.error, selectionState.value, selectionState.error, value, props.now, props.timeZone, props.locale, props.min, props.max, props.format, props.dateFormat, props.timeFormat, props.hourCycle, props.minuteStep, props.clearable, props.required, props.disabled, props.disambiguation]);

    if (contextState.error) {
        return (
            <div className="ju-field">
                {props.label && <span className={`ju-field-label${props.labelHidden ? " sr-only" : ""}`}>{props.label}</span>}
                <span className="ju-field-message is-error" role="alert">{contextState.error}</span>
            </div>
        );
    }

    const formats = resolvePickerFormat({
        mode: props.mode,
        locale: props.locale,
        hourCycle: props.hourCycle,
        format: props.format,
        dateFormat: props.dateFormat,
        timeFormat: props.timeFormat,
    });
    const actualValue = value ?? "";
    const currentError = validationMessage || selectionState.error;
    const nativeFormat = props.mode === "date"
        ? formats.dateFormat
        : props.mode === "time"
          ? formats.timeFormat
          : `${formats.dateFormat}${formats.dateTimeSeparator}${formats.timeFormat}`;

    return (
        <label className={`ju-field ju-air-picker-field${props.className ? ` ${props.className}` : ""}`} htmlFor={inputId}>
            {props.label && <span className={`ju-field-label${props.labelHidden ? " sr-only" : ""}`}>{props.label}</span>}
            <span className="ju-air-picker-control">
                <input
                    aria-describedby={currentError ? errorId : undefined}
                    aria-invalid={currentError ? "true" : undefined}
                    aria-label={props.label}
                    autoComplete="off"
                    className={`ju-input ju-air-picker-input${currentError ? " ju-air-picker-error-input" : ""}`}
                    defaultValue={selectionState.error ? actualValue : ""}
                    disabled={props.disabled}
                    id={inputId}
                    onFocus={() => {
                        const picker = pickerRef.current;
                        // Explicitly show on focus: Air Datepicker's native focus handler can
                        // be skipped after its animated hide callback removes the popup node.
                        if (picker?.visible && !picker.$datepicker.isConnected) {
                            (picker as unknown as { visible: boolean }).visible = false;
                        }
                        picker?.show();
                        picker?.$datepicker.classList.add("-juju-visible-");
                    }}
                    onBlur={(event: FocusEvent<HTMLInputElement>) => {
                        const current = latestProps.current;
                        if (directInputRef.current && !current.readOnly) {
                            const typedValue = processTypedValue(event.currentTarget, current, nativeFormat);
                            directInputRef.current = false;
                            if (typedValue !== undefined) current.onBlur?.(typedValue);
                            return;
                        }
                        const blurredValue = lastCommittedValue.current;
                        directInputRef.current = false;
                        window.setTimeout(() => {
                            const picker = pickerRef.current;
                            if (picker?.visible && picker.$datepicker.contains(document.activeElement)) return;
                            latestProps.current.onBlur?.(blurredValue);
                        }, 0);
                    }}
                    onInput={() => {
                        if (props.readOnly || props.disabled) return;
                        directInputRef.current = true;
                        if (validationMessage) reportError("");
                    }}
                    placeholder={props.placeholder ?? nativeFormat}
                    readOnly={props.readOnly}
                    ref={inputRef}
                    required={props.required}
                    type="text"
                />
                <span aria-hidden="true" className="ju-air-picker-icon">
                    {props.mode === "time" ? <Clock3 size={16} /> : <CalendarDays size={16} />}
                </span>
            </span>
            {currentError && <span className="ju-field-message is-error" id={errorId} role="alert">{currentError}</span>}
            {!currentError && props.required && !actualValue && <span className="ju-air-picker-required sr-only">{localeText(props.locale).required}</span>}
        </label>
    );
}

export function DatePicker(props: DatePickerProps) {
    return <AirDateTimePicker {...props} mode="date" />;
}

export function TimePicker(props: TimePickerProps) {
    return <AirDateTimePicker {...props} mode="time" />;
}

export function DateTimePicker(props: DateTimePickerProps) {
    return <AirDateTimePicker {...props} mode="datetime" />;
}
