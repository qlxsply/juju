export type DateOnly = string;
export type TimeOnly = string;
export type AbsoluteDateTime = string;
export type HourCycle = 12 | 24;
export type Disambiguation = "reject" | "earlier" | "later";
export type PickerPlacement = "bottom-start" | "bottom-end" | "top-start" | "top-end";

export interface PickerTimeContext {
    /** Absolute instant in ISO-8601 UTC/offset form, or epoch milliseconds. */
    now: string | number;
    /** IANA time-zone identifier; never inferred from the browser. */
    timeZone: string;
    /** BCP-47 language tag; never inferred from the browser. */
    locale: string;
}

export type ValueProps<T> =
    | { value: T | null; defaultValue?: never }
    | { value?: undefined; defaultValue?: T | null };

export interface PickerCommonProps extends PickerTimeContext {
    disabled?: boolean;
    readOnly?: boolean;
    required?: boolean;
    placeholder?: string;
    clearable?: boolean;
    format?: string;
    placement?: PickerPlacement;
    label?: string;
    hint?: string;
    labelHidden?: boolean;
    id?: string;
    className?: string;
    onValidationError?: (message: string | null) => void;
}

export interface PickerBase<T> extends PickerCommonProps {
    value?: T | null;
    defaultValue?: T | null;
    onChange?: (value: T | null) => void;
    onBlur?: (value: T | null) => void;
}

export type DatePickerProps = PickerBase<DateOnly> & {
    min?: DateOnly;
    max?: DateOnly;
    onChange?: (value: DateOnly | null) => void;
};

export type TimePickerProps = PickerBase<TimeOnly> & {
    hourCycle?: HourCycle;
    minuteStep?: number;
    min?: TimeOnly;
    max?: TimeOnly;
    onChange?: (value: TimeOnly | null) => void;
};

export type DateTimePickerProps = PickerBase<AbsoluteDateTime> & {
    dateFormat?: string;
    timeFormat?: string;
    hourCycle?: HourCycle;
    minuteStep?: number;
    disambiguation?: Disambiguation;
    min?: AbsoluteDateTime;
    max?: AbsoluteDateTime;
    onChange?: (value: AbsoluteDateTime | null) => void;
};

export type PickerMode = "date" | "time" | "datetime";

export type PickerValue = DateOnly | TimeOnly | AbsoluteDateTime | null;

export interface PickerValidationError {
    code:
        | "invalid-context"
        | "invalid-value"
        | "outside-range"
        | "nonexistent-local-time"
        | "ambiguous-local-time"
        | "required"
        | "min-max";
    message: string;
}
