import { useId, type TextareaHTMLAttributes } from "react";
import { ValidationIndicator } from "./ValidationIndicator";
import { validationMessages } from "./validation";

interface JuTextareaProps extends Omit<
    TextareaHTMLAttributes<HTMLTextAreaElement>,
    "className" | "style"
> {
    label: string;
    hint?: string;
    warning?: string;
    error?: string;
    success?: string;
}

export function JuTextarea({
    label,
    hint,
    warning,
    error,
    success,
    id,
    ...props
}: JuTextareaProps) {
    const generatedId = useId();
    const fieldId = id ?? generatedId;
    const messages = validationMessages({ error, warning, info: hint, success });
    const describedBy = [props["aria-describedby"], messages.length ? `${fieldId}-validation` : null].filter(Boolean).join(" ") || undefined;

    return (
        <div className="ju-field">
            <div className="ju-field-heading">
                <label className="ju-field-label" htmlFor={fieldId}>{label}</label>
                <ValidationIndicator fieldId={fieldId} messages={messages} />
            </div>
            <textarea
                {...props}
                aria-label={label}
                aria-describedby={describedBy}
                aria-invalid={error ? true : props["aria-invalid"]}
                className="ju-textarea"
                id={fieldId}
            />
        </div>
    );
}
