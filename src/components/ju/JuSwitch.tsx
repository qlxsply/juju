import { useId } from "react";
import { ValidationIndicator } from "./ValidationIndicator";
import { validationMessages } from "./validation";
import { SwitchRoot, SwitchThumb } from "../ui/switch";

export function JuSwitch({
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
    checked?: boolean;
    defaultChecked?: boolean;
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
            <SwitchRoot
                aria-label={ariaLabel ?? label}
                aria-labelledby={label ? `${id}-label` : undefined}
                checked={checked}
                defaultChecked={defaultChecked}
                className="ju-switch"
                disabled={disabled}
                name={name}
                required={required}
                value={value}
                aria-invalid={error ? true : undefined}
                aria-describedby={messages.length ? `${id}-validation` : undefined}
                id={id}
                onCheckedChange={onCheckedChange}
            >
                <SwitchThumb className="ju-switch-thumb" />
            </SwitchRoot>
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
