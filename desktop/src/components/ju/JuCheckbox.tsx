import { useId } from "react";
import { CheckboxIndicator, CheckboxRoot } from "../ui/checkbox";

export function JuCheckbox({
    label,
    ariaLabel,
    checked,
    onCheckedChange,
    disabled = false,
}: {
    label?: string;
    ariaLabel?: string;
    checked: boolean;
    onCheckedChange: (checked: boolean) => void;
    disabled?: boolean;
}) {
    const id = useId();
    return (
        <div className="ju-choice-row">
            <CheckboxRoot
                aria-label={ariaLabel ?? label}
                aria-labelledby={label ? `${id}-label` : undefined}
                checked={checked}
                className="ju-checkbox"
                disabled={disabled}
                id={id}
                onCheckedChange={(value) => onCheckedChange(value === true)}
            >
                <CheckboxIndicator className="ju-checkbox-indicator">
                    ✓
                </CheckboxIndicator>
            </CheckboxRoot>
            {label && (
                <label
                    className="ju-choice-label"
                    htmlFor={id}
                    id={`${id}-label`}
                >
                    {label}
                </label>
            )}
        </div>
    );
}
