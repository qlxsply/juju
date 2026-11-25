import { useEffect, useRef, useState } from "react";
import { PopoverContent, PopoverPortal, PopoverRoot, PopoverTrigger } from "../ui/popover";
import { JuButton } from "./JuButton";
import { JuText } from "./JuText";

/** Single-line text that exposes its full value only when it is truncated. */
export function JuOverflowText({ text, tabIndex = 0 }: { text: string; tabIndex?: 0 | -1 }) {
    const textRef = useRef<HTMLSpanElement>(null);
    const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
    const pinned = useRef(false);
    const [overflow, setOverflow] = useState(false);
    const [open, setOpen] = useState(false);
    const [message, setMessage] = useState("");

    useEffect(() => {
        const element = textRef.current;
        if (!element) return;
        const measure = () => setOverflow(element.scrollWidth > element.clientWidth);
        const observer = new ResizeObserver(measure);
        observer.observe(element);
        measure();
        return () => observer.disconnect();
    }, [text, overflow]);

    useEffect(() => () => { if (timer.current) clearTimeout(timer.current); }, []);
    const cancel = () => { if (timer.current) clearTimeout(timer.current); timer.current = null; };
    const closeLater = () => {
        cancel();
        timer.current = setTimeout(() => { if (!pinned.current) setOpen(false); }, 200);
    };
    const enter = () => {
        cancel();
        if (!overflow || pinned.current) return;
        timer.current = setTimeout(() => setOpen(true), 400);
    };
    const copy = async () => {
        try {
            await navigator.clipboard.writeText(text);
            setMessage("已复制");
        } catch {
            setMessage("复制失败，请手动选择文本复制");
        }
    };

    const textElement = (
        <JuText
            className="ju-overflow-text"
            ref={textRef}
            role={overflow ? "button" : undefined}
            tabIndex={overflow ? tabIndex : undefined}
            aria-label={overflow ? `查看完整内容：${text}` : undefined}
            onMouseEnter={enter}
            onMouseLeave={closeLater}
            onClick={(event) => {
                if (!overflow) return;
                event.stopPropagation();
                if (open && !pinned.current) {
                    event.preventDefault();
                    pinned.current = true;
                }
            }}
            onDoubleClick={(event) => { if (overflow) event.stopPropagation(); }}
            onKeyDown={(event) => {
                if (!overflow) return;
                if (event.key === "Enter" || event.key === " ") {
                    event.stopPropagation();
                    event.preventDefault();
                    pinned.current = !pinned.current;
                    setOpen(pinned.current);
                } else if (tabIndex !== -1) {
                    event.stopPropagation();
                }
            }}
        >{text}</JuText>
    );
    return (
        <PopoverRoot open={open && overflow} onOpenChange={(next) => {
            cancel();
            pinned.current = next;
            setOpen(next);
            if (!next) setMessage("");
        }}>
            {overflow ? <PopoverTrigger asChild>{textElement}</PopoverTrigger> : textElement}
            <PopoverPortal>
                <PopoverContent
                    className="ju-overflow-popover"
                    side="bottom"
                    align="start"
                    sideOffset={6}
                    collisionPadding={8}
                    onOpenAutoFocus={(event) => event.preventDefault()}
                    onMouseEnter={cancel}
                    onMouseLeave={closeLater}
                    onClick={(event) => event.stopPropagation()}
                    onDoubleClick={(event) => event.stopPropagation()}
                    onKeyDown={(event) => event.stopPropagation()}
                >
                    <div className="ju-overflow-full-text">{text}</div>
                    <div className="ju-overflow-actions">
                        <JuButton size="compact" variant="outline" onClick={() => void copy()}>复制</JuButton>
                        <span role="status">{message}</span>
                    </div>
                </PopoverContent>
            </PopoverPortal>
        </PopoverRoot>
    );
}
