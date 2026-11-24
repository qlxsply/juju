import { useId } from "react";
import { Check, Minus } from "lucide-react";
import { ValidationIndicator } from "./ValidationIndicator";
import { validationMessages } from "./validation";
import { CheckboxIndicator, CheckboxRoot } from "../ui/checkbox";

export function JuCheckbox({
    label,
    ariaLabel,
    checked,
    defaultChecked,
    onCheckedChange,
    disabled = false,
    description,
    error,
    name,
    required,
    value,
}: {
    label?: string;
    ariaLabel?: string;
    checked?: boolean | "indeterminate";
    defaultChecked?: boolean | "indeterminate";
    onCheckedChange?: (checked: boolean) => void;
    disabled?: boolean;
    description?: string;
    error?: string;
    name?: string;
    required?: boolean;
    value?: string;
}) {
    const id = useId();
    const messages = validationMessages({ error, info: description });
    return (
        <div className="ju-choice-row">
            <CheckboxRoot
                aria-label={ariaLabel ?? label}
                aria-labelledby={label ? `${id}-label` : undefined}
                checked={checked}
                defaultChecked={defaultChecked}
                className="ju-checkbox"
                disabled={disabled}
                name={name}
                required={required}
                value={value}
                aria-invalid={error ? true : undefined}
                aria-describedby={messages.length ? `${id}-validation` : undefined}
                id={id}
                onCheckedChange={(next) => onCheckedChange?.(next === true)}
            >
                <CheckboxIndicator className="ju-checkbox-indicator">
                    <Check className="ju-checkbox-check" size={13} strokeWidth={2.5} aria-hidden="true" />
                    <Minus className="ju-checkbox-minus" size={13} strokeWidth={2.5} aria-hidden="true" />
                </CheckboxIndicator>
            </CheckboxRoot>
            {(label || messages.length > 0) && (
                <div>
                <div className="ju-field-heading"><label
                    className="ju-choice-label"
                    htmlFor={id}
                    id={`${id}-label`}
                >
                    {label}
                </label><ValidationIndicator fieldId={id} messages={messages} hiddenLabel={!label} /></div>
                </div>
            )}
        </div>
    );
}
