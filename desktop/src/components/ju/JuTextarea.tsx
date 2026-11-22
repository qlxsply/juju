import { useId, type TextareaHTMLAttributes } from "react";

interface JuTextareaProps extends Omit<
    TextareaHTMLAttributes<HTMLTextAreaElement>,
    "className" | "style"
> {
    label: string;
    hint?: string;
    warning?: string;
    error?: string;
}

export function JuTextarea({
    label,
    hint,
    warning,
    error,
    id,
    ...props
}: JuTextareaProps) {
    const generatedId = useId();
    const fieldId = id ?? generatedId;
    const messageId = `${fieldId}-message`;
    const message = error ?? warning ?? hint;
    const level = error ? "error" : warning ? "warning" : "info";

    return (
        <label className="ju-field" htmlFor={fieldId}>
            <span className="ju-field-label">
                {label}
                {message && <span className={`ju-field-message is-${level}`} id={messageId}>{message.replace(/[。！？.!?]+$/, "")}</span>}
            </span>
            <textarea
                {...props}
                aria-label={label}
                aria-describedby={message ? messageId : undefined}
                aria-invalid={Boolean(error)}
                className="ju-textarea"
                id={fieldId}
            />
        </label>
    );
}
