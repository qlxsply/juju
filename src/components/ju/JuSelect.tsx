import { useId } from "react";
import { ChevronDown } from "lucide-react";
import { ValidationIndicator } from "./ValidationIndicator";
import { validationMessages } from "./validation";
import {
    SelectContent,
    SelectGroup,
    SelectLabel,
    SelectItem,
    SelectItemText,
    SelectPortal,
    SelectRoot,
    SelectTrigger,
    SelectValue,
    SelectViewport,
} from "../ui/select";

export interface JuSelectOption {
    value: string;
    label: string;
    disabled?: boolean;
}

export interface JuSelectProps {
    label?: string;
    ariaLabel?: string;
    value?: string;
    defaultValue?: string;
    onValueChange?: (value: string) => void;
    options?: JuSelectOption[];
    groups?: { label: string; options: JuSelectOption[] }[];
    placeholder?: string;
    disabled?: boolean;
    required?: boolean;
    name?: string;
    error?: string;
    description?: string;
}

export function JuSelect({
    label,
    ariaLabel,
    value,
    defaultValue,
    onValueChange,
    options = [],
    groups = [],
    placeholder = "请选择",
    disabled,
    required,
    name,
    error,
    description,
}: JuSelectProps) {
    const id = useId();
    const messages = validationMessages({ error, info: description });
    const select = (
        <SelectRoot
            value={value}
            defaultValue={defaultValue}
            onValueChange={onValueChange}
            disabled={disabled}
            required={required}
            name={name}
        >
            <SelectTrigger
                aria-label={ariaLabel}
                aria-labelledby={label ? `${id}-label` : undefined}
                aria-invalid={error ? true : undefined}
                aria-describedby={messages.length ? `${id}-validation` : undefined}
                className="ju-select-trigger"
                id={id}
            >
                <SelectValue placeholder={placeholder} />
                <ChevronDown className="ju-select-chevron" size={15} aria-hidden="true" />
            </SelectTrigger>
            <SelectPortal>
                <SelectContent
                    className="ju-select-content"
                    position="popper"
                    sideOffset={4}
                >
                    <SelectViewport>
                        {options.map((option) => (
                            <SelectItem
                                className="ju-select-item"
                                disabled={option.disabled}
                                key={option.value}
                                value={option.value}
                            >
                                <SelectItemText>{option.label}</SelectItemText>
                            </SelectItem>
                        ))}
                        {groups.map((group) => (
                            <SelectGroup key={group.label}>
                                <SelectLabel className="ju-select-group-label">{group.label}</SelectLabel>
                                {group.options.map((option) => (
                                    <SelectItem className="ju-select-item" disabled={option.disabled} key={option.value} value={option.value}>
                                        <SelectItemText>{option.label}</SelectItemText>
                                    </SelectItem>
                                ))}
                            </SelectGroup>
                        ))}
                    </SelectViewport>
                </SelectContent>
            </SelectPortal>
        </SelectRoot>
    );

    return (
        <div className="ju-field">
            {(label || messages.length > 0) && <div className="ju-field-heading">
                {label && <label className="ju-field-label" id={`${id}-label`} htmlFor={id}>{label}</label>}
                <ValidationIndicator fieldId={id} messages={messages} hiddenLabel={!label} />
            </div>}
            {select}
        </div>
    );
}
