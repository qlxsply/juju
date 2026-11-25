import { useEffect, useState } from "react";
import { PopoverContent, PopoverPortal, PopoverRoot, PopoverTrigger } from "../ui/popover";
import { validationPriority, type ValidationMessage } from "./validation";
import { validationIndicatorDefinitions } from "./validation-indicator-definitions";

export function ValidationIndicator({ fieldId, messages, hiddenLabel = false }: {
    fieldId: string;
    messages: ValidationMessage[];
    hiddenLabel?: boolean;
}) {
    const [open, setOpen] = useState(false);
    const level = validationPriority.find((item) => messages.some((message) => message.level === item));
    useEffect(() => {
        const handleOpen = (event: Event) => {
            if ((event as CustomEvent<string>).detail === fieldId) setOpen(true);
        };
        document.addEventListener("juju:show-validation", handleOpen);
        return () => document.removeEventListener("juju:show-validation", handleOpen);
    }, [fieldId]);
    if (!level) return null;
    const { icon: Icon, label } = validationIndicatorDefinitions[level];
    return <>
        <span id={`${fieldId}-validation`} className="sr-only">{messages.map((message) => `${validationIndicatorDefinitions[message.level].label}：${message.text}`).join("。")}</span>
        <PopoverRoot open={open} onOpenChange={setOpen}>
            <PopoverTrigger asChild>
                <button type="button" className={`ju-validation-indicator is-${level}${hiddenLabel ? " is-hidden-label" : ""}`} aria-label={`查看${label}信息`} aria-describedby={`${fieldId}-validation`}>
                    <Icon size={15} aria-hidden="true" />
                </button>
            </PopoverTrigger>
            <PopoverPortal>
                <PopoverContent className="ju-validation-popover" sideOffset={6} align="start" onOpenAutoFocus={(event) => event.preventDefault()}>
                    {messages.length === 1 ? <p>{messages[0].text}</p> : <ul>{messages.map((message, index) => <li key={`${message.level}-${index}`}>{message.text}</li>)}</ul>}
                </PopoverContent>
            </PopoverPortal>
        </PopoverRoot>
    </>;
}
