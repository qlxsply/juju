import { useId } from "react";
import {
    RadioGroupItem,
    RadioGroupRoot,
    RadioIndicator,
} from "../ui/radio-group";

export interface JuRadioOption {
    value: string;
    label: string;
    disabled?: boolean;
}

export function JuRadioGroup({
    label,
    value,
    options,
    onValueChange,
}: {
    label: string;
    value: string;
    options: JuRadioOption[];
    onValueChange: (value: string) => void;
}) {
    const id = useId();
    return (
        <fieldset className="ju-radio-fieldset">
            <legend className="ju-field-label">{label}</legend>
            <RadioGroupRoot
                className="ju-radio-group"
                value={value}
                onValueChange={onValueChange}
            >
                {options.map((option) => {
                    const optionId = `${id}-${option.value}`;
                    return (
                        <div className="ju-choice-row" key={option.value}>
                            <RadioGroupItem
                                aria-labelledby={`${optionId}-label`}
                                className="ju-radio"
                                disabled={option.disabled}
                                id={optionId}
                                value={option.value}
                            >
                                <RadioIndicator className="ju-radio-indicator" />
                            </RadioGroupItem>
                            <label
                                className="ju-choice-label"
                                htmlFor={optionId}
                                id={`${optionId}-label`}
                            >
                                {option.label}
                            </label>
                        </div>
                    );
                })}
            </RadioGroupRoot>
        </fieldset>
    );
}
