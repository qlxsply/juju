import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import { ChevronDown } from "lucide-react";
import { PopoverContent, PopoverPortal, PopoverRoot, PopoverTrigger } from "../ui/popover";
import { ValidationIndicator } from "./ValidationIndicator";
import { validationMessages } from "./validation";

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
    label, ariaLabel, value, defaultValue, onValueChange, options = [], groups = [],
    placeholder = "请选择", disabled, required, name, error, description,
}: JuSelectProps) {
    const id = useId();
    const triggerRef = useRef<HTMLButtonElement>(null);
    const listRef = useRef<HTMLDivElement>(null);
    const searchRef = useRef("");
    const searchTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
    const [internalValue, setInternalValue] = useState(defaultValue ?? "");
    const [open, setOpen] = useState(false);
    const [activeValue, setActiveValue] = useState<string | null>(null);
    const selectedValue = value ?? internalValue;
    const allOptions = [...options, ...groups.flatMap((group) => group.options)];
    const enabled = allOptions.filter((option) => !option.disabled);
    const selected = allOptions.find((option) => option.value === selectedValue);
    const activeIndex = allOptions.findIndex((option) => option.value === activeValue && !option.disabled);
    const messages = validationMessages({ error, info: description });

    useEffect(() => () => clearTimeout(searchTimer.current), []);
    useEffect(() => {
        if (!open || activeIndex < 0) return;
        listRef.current?.querySelector<HTMLElement>(`[data-option-index="${activeIndex}"]`)?.scrollIntoView?.({ block: "nearest" });
    }, [open, activeIndex]);
    useEffect(() => {
        const form = triggerRef.current?.closest("form");
        if (!form || value !== undefined) return;
        const reset = () => setInternalValue(defaultValue ?? "");
        form.addEventListener("reset", reset);
        return () => form.removeEventListener("reset", reset);
    }, [value, defaultValue]);

    const openList = (edge?: "first" | "last") => {
        if (disabled) return;
        const next = edge === "last" ? enabled.at(-1) : edge === "first" ? enabled[0] : enabled.find((option) => option.value === selectedValue) ?? enabled[0];
        setActiveValue(next?.value ?? null);
        setOpen(true);
    };
    const select = (option: JuSelectOption) => {
        if (option.disabled) return;
        if (value === undefined) setInternalValue(option.value);
        if (option.value !== selectedValue) onValueChange?.(option.value);
        setOpen(false);
        triggerRef.current?.focus({ preventScroll: true });
    };
    const onKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
        if (disabled) return;
        if (event.key === "Tab") { setOpen(false); return; }
        if (event.key === "Escape") {
            if (open) { event.preventDefault(); event.stopPropagation(); setOpen(false); }
            return;
        }
        if (["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) {
            event.preventDefault();
            if (!open) { openList(event.key === "Home" ? "first" : event.key === "End" ? "last" : undefined); return; }
            if (!enabled.length) return;
            const index = enabled.findIndex((option) => option.value === activeValue);
            const next = event.key === "Home" ? 0 : event.key === "End" ? enabled.length - 1
                : event.key === "ArrowDown" ? Math.min(index + 1, enabled.length - 1) : Math.max(index - 1, 0);
            setActiveValue(enabled[next].value);
            return;
        }
        if (event.key === "Enter" || event.key === " ") {
            event.preventDefault();
            if (!open) openList();
            else {
                const option = enabled.find((item) => item.value === activeValue);
                if (option) select(option);
            }
            return;
        }
        if (event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) {
            clearTimeout(searchTimer.current);
            searchRef.current += event.key.toLocaleLowerCase();
            searchTimer.current = setTimeout(() => { searchRef.current = ""; }, 700);
            const match = enabled.find((option) => option.label.toLocaleLowerCase().startsWith(searchRef.current));
            if (match) { setActiveValue(match.value); setOpen(true); }
        }
    };
    const renderOption = (option: JuSelectOption, index: number) => (
        <div key={option.value} id={`${id}-option-${index}`} data-option-index={index}
            role="option" aria-selected={option.value === selectedValue} aria-disabled={option.disabled || undefined}
            data-highlighted={option.value === activeValue && !option.disabled ? "" : undefined}
            data-disabled={option.disabled ? "" : undefined} className="ju-select-item"
            onPointerMove={(event) => { if (event.pointerType !== "touch" && !option.disabled) setActiveValue(option.value); }}
            onPointerDown={(event) => { if (event.pointerType === "mouse") event.preventDefault(); }}
            onClick={() => select(option)}>{option.label}</div>
    );
    return <div className="ju-field">
        {(label || messages.length > 0) && <div className="ju-field-heading">
            {label && <label className="ju-field-label" id={`${id}-label`} htmlFor={id}>{label}</label>}
            <ValidationIndicator fieldId={id} messages={messages} hiddenLabel={!label} />
        </div>}
        <PopoverRoot open={open} onOpenChange={(next) => { if (next) openList(); else setOpen(false); }} modal={false}>
            <PopoverTrigger asChild>
                <button ref={triggerRef} type="button" role="combobox" aria-label={ariaLabel}
                    aria-labelledby={label ? `${id}-label` : undefined} aria-invalid={error ? true : undefined}
                    aria-describedby={messages.length ? `${id}-validation` : undefined}
                    aria-required={required || undefined} aria-expanded={open} aria-controls={open ? `${id}-listbox` : undefined}
                    aria-activedescendant={open && activeIndex >= 0 ? `${id}-option-${activeIndex}` : undefined}
                    aria-haspopup="listbox" disabled={disabled} className="ju-select-trigger" id={id} onKeyDown={onKeyDown}>
                    <span className={selected ? undefined : "ju-select-placeholder"}>{selected?.label ?? placeholder}</span>
                    <ChevronDown className="ju-select-chevron" size={15} aria-hidden="true" />
                </button>
            </PopoverTrigger>
            <PopoverPortal>
                <PopoverContent ref={listRef} id={`${id}-listbox`} role="listbox" aria-labelledby={label ? `${id}-label` : undefined}
                    aria-label={!label ? ariaLabel : undefined} className="ju-select-content"
                    sideOffset={4} align="start" onOpenAutoFocus={(event) => event.preventDefault()}
                    onCloseAutoFocus={(event) => event.preventDefault()}>
                    {options.map(renderOption)}
                    {groups.map((group, groupIndex) => {
                        const start = options.length + groups.slice(0, groupIndex).reduce((count, item) => count + item.options.length, 0);
                        return <div role="group" aria-labelledby={`${id}-group-${groupIndex}`} key={`${group.label}-${groupIndex}`}>
                            <div className="ju-select-group-label" id={`${id}-group-${groupIndex}`}>{group.label}</div>
                            {group.options.map((option, index) => renderOption(option, start + index))}
                        </div>;
                    })}
                </PopoverContent>
            </PopoverPortal>
        </PopoverRoot>
        {(name || required) && <select className="sr-only" aria-hidden="true" tabIndex={-1} name={name} required={required}
            disabled={disabled} value={selectedValue} onChange={(event) => {
                const option = allOptions.find((item) => item.value === event.target.value);
                if (option) select(option);
            }} onInvalid={(event) => { event.preventDefault(); triggerRef.current?.focus(); }}>
            <option value="" />
            {allOptions.map((option) => <option key={option.value} value={option.value} disabled={option.disabled}>{option.label}</option>)}
        </select>}
    </div>;
}
