import { useId, type InputHTMLAttributes } from "react";
import { InputPrimitive } from "../ui/input";

interface JuInputProps extends Omit<
    InputHTMLAttributes<HTMLInputElement>,
    "className" | "style"
> {
    label: string;
    labelHidden?: boolean;
    hint?: string;
    warning?: string;
    error?: string;
}

export function JuInput({
    label,
    labelHidden = false,
    hint,
    warning,
    error,
    id,
    ...props
}: JuInputProps) {
    const generatedId = useId();
    const inputId = id ?? generatedId;
    const messageId = `${inputId}-message`;
    const message = error ?? warning ?? hint;
    const level = error ? "error" : warning ? "warning" : "info";

    return (
        <label className="ju-field" htmlFor={inputId}>
            <span className={`ju-field-label${labelHidden ? " sr-only" : ""}`}>
                {label}
                {!labelHidden && message && (
                    <span className={`ju-field-message is-${level}`} id={messageId}>
                        {message.replace(/[。！？.!?]+$/, "")}
                    </span>
                )}
            </span>
            <InputPrimitive
                {...props}
                aria-label={label}
                aria-describedby={message ? messageId : undefined}
                aria-invalid={Boolean(error)}
                className="ju-input"
                id={inputId}
            />
            {labelHidden && message && (
                <span
                    className={`ju-field-message is-${level}`}
                    id={messageId}
                >
                    {message}
                </span>
            )}
        </label>
    );
}
