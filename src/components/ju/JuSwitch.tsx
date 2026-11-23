import { useId } from "react";
import { SwitchRoot, SwitchThumb } from "../ui/switch";

export function JuSwitch({
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
            <SwitchRoot
                aria-label={ariaLabel ?? label}
                aria-labelledby={label ? `${id}-label` : undefined}
                checked={checked}
                className="ju-switch"
                disabled={disabled}
                id={id}
                onCheckedChange={onCheckedChange}
            >
                <SwitchThumb className="ju-switch-thumb" />
            </SwitchRoot>
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
