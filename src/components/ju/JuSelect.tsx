import { useId } from "react";
import { ChevronDown } from "lucide-react";
import {
    SelectContent,
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

interface JuSelectProps {
    label?: string;
    ariaLabel?: string;
    value: string;
    onValueChange: (value: string) => void;
    options: JuSelectOption[];
    placeholder?: string;
    disabled?: boolean;
}

export function JuSelect({
    label,
    ariaLabel,
    value,
    onValueChange,
    options,
    placeholder = "请选择",
    disabled,
}: JuSelectProps) {
    const id = useId();
    const select = (
        <SelectRoot
            value={value}
            onValueChange={onValueChange}
            disabled={disabled}
        >
            <SelectTrigger
                aria-label={ariaLabel}
                aria-labelledby={label ? `${id}-label` : undefined}
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
                    </SelectViewport>
                </SelectContent>
            </SelectPortal>
        </SelectRoot>
    );

    if (!label) return select;

    return (
        <label className="ju-field" htmlFor={id}>
            <span className="ju-field-label" id={`${id}-label`}>
                {label}
            </span>
            {select}
        </label>
    );
}
