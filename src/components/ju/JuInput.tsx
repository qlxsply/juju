import { useId, type InputHTMLAttributes } from "react";
import { InputPrimitive } from "../ui/input";
import { ValidationIndicator } from "./ValidationIndicator";
import { validationMessages } from "./validation";

interface JuInputProps extends Omit<
    InputHTMLAttributes<HTMLInputElement>,
    "className" | "style"
> {
    label: string;
    labelHidden?: boolean;
    hint?: string;
    warning?: string;
    error?: string;
    success?: string;
}

export function JuInput({
    label,
    labelHidden = false,
    hint,
    warning,
    error,
    success,
    id,
    ...props
}: JuInputProps) {
    const generatedId = useId();
    const inputId = id ?? generatedId;
    const messages = validationMessages({ error, warning, info: hint, success });
    const describedBy = [props["aria-describedby"], messages.length ? `${inputId}-validation` : null].filter(Boolean).join(" ") || undefined;

    return (
        <div className="ju-field">
            <div className="ju-field-heading">
                <label htmlFor={inputId} className={`ju-field-label${labelHidden ? " sr-only" : ""}`}>{label}</label>
                <ValidationIndicator fieldId={inputId} messages={messages} hiddenLabel={labelHidden} />
            </div>
            <InputPrimitive
                {...props}
                aria-label={label}
                aria-describedby={describedBy}
                aria-invalid={error ? true : props["aria-invalid"]}
                className="ju-input"
                id={inputId}
            />
        </div>
    );
}
