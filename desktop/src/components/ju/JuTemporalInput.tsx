import { useEffect, useId, useRef, useState } from "react";
import { CalendarDays, Clock3 } from "lucide-react";

type Kind = "year" | "month" | "day" | "hour" | "minute";
const config: Record<Kind, { label: string; placeholder: string; max: number; width: number }> = {
    year: { label: "年", placeholder: "yyyy", max: 9999, width: 4 },
    month: { label: "月", placeholder: "mm", max: 12, width: 2 },
    day: { label: "日", placeholder: "dd", max: 31, width: 2 },
    hour: { label: "时", placeholder: "hh", max: 23, width: 2 },
    minute: { label: "分", placeholder: "mm", max: 59, width: 2 },
};

function parse(value: string, type: "date" | "time" | "datetime-local"): string[] {
    if (!value) return Array(type === "datetime-local" ? 5 : type === "date" ? 3 : 2).fill("");
    return value.split(/[-T:]/).slice(0, type === "datetime-local" ? 5 : type === "date" ? 3 : 2);
}

function serialize(parts: string[], type: "date" | "time" | "datetime-local"): string | null {
    if (parts.every((part) => !part)) return "";
    if (parts.some((part) => !part) || parts.some((part, index) => part.length !== (type === "time" || index > 0 ? 2 : 4))) return null;
    const date = type === "time" ? "" : `${parts[0]}-${parts[1]}-${parts[2]}`;
    if (date) {
        const [year, month, day] = parts.map(Number);
        const maxDay = new Date(Date.UTC(year, month, 0)).getUTCDate();
        if (year < 1 || month < 1 || month > 12 || day < 1 || day > maxDay) return null;
    }
    const time = type === "date" ? "" : parts.slice(type === "time" ? 0 : 3).join(":");
    return type === "datetime-local" ? `${date}T${time}` : date || time;
}

export function JuTemporalInput({ label, labelHidden = false, type, value, onChange, onBlur, disabled = false }: {
    label: string; labelHidden?: boolean; type: "date" | "time" | "datetime-local";
    value: string; onChange: (value: string) => void; onBlur?: (value: string) => void; disabled?: boolean;
}) {
    const kinds: Kind[] = type === "date" ? ["year", "month", "day"] : type === "time" ? ["hour", "minute"] : ["year", "month", "day", "hour", "minute"];
    const [parts, setParts] = useState(() => parse(value, type));
    const inputs = useRef<(HTMLInputElement | null)[]>([]);
    const picker = useRef<HTMLInputElement>(null);
    const editing = useRef(false);
    const labelId = useId();
    useEffect(() => { if (!editing.current) setParts(parse(value, type)); }, [value, type]);

    const clear = () => { setParts(kinds.map(() => "")); onChange(""); onBlur?.(""); };
    const update = (index: number, raw: string) => {
        const kind = kinds[index];
        const spec = config[kind];
        let next = raw.replace(/\D/g, "").slice(-spec.width);
        if (spec.width === 2 && next.length === 1 && Number(next) > Math.floor(spec.max / 10)) next = `0${next}`;
        if (next.length === spec.width && (Number(next) > spec.max || (kind !== "hour" && kind !== "minute" && Number(next) === 0))) return;
        const updated = [...parts]; updated[index] = next; setParts(updated);
        const result = serialize(updated, type);
        if (result !== null) onChange(result);
        if (next.length === spec.width && index < kinds.length - 1) inputs.current[index + 1]?.focus();
    };
    return (
        <div className="ju-field ju-temporal-input" onFocus={() => { editing.current = true; }} onBlur={(event) => {
            if (event.currentTarget.contains(event.relatedTarget as Node | null)) return;
            editing.current = false;
            const result = serialize(parts, type);
            if (result === null) { setParts(parse(value, type)); onBlur?.(value); }
            else onBlur?.(result);
        }}>
            <span id={labelId} className={`ju-field-label${labelHidden ? " sr-only" : ""}`}>{label}</span>
            <div className="ju-temporal-control" role="group" aria-labelledby={labelId}>
                {kinds.map((kind, index) => <span className="ju-temporal-segment-wrap" key={kind}>
                    {index > 0 && <span className="ju-temporal-separator" aria-hidden="true">{kind === "hour" ? " " : kind === "minute" ? ":" : " / "}</span>}
                    <input ref={(node) => { inputs.current[index] = node; }} className={`ju-temporal-segment is-${kind}`} type="text" inputMode="numeric" autoComplete="off" aria-label={`${label} ${config[kind].label}`} placeholder={config[kind].placeholder} value={parts[index] ?? ""} disabled={disabled}
                        onFocus={(event) => event.currentTarget.select()}
                        onChange={(event) => update(index, event.currentTarget.value)}
                        onKeyDown={(event) => { if (event.key === "Backspace" || event.key === "Delete") { event.preventDefault(); clear(); } }} />
                </span>)}
                <button type="button" className="ju-temporal-picker-button" aria-label={`选择${label}`} disabled={disabled} onClick={() => { try { picker.current?.showPicker(); } catch { picker.current?.focus(); } }}>{type === "time" ? <Clock3 size={15} /> : <CalendarDays size={15} />}</button>
                <input ref={picker} className="ju-temporal-native" tabIndex={-1} aria-hidden="true" type={type} value={value} disabled={disabled} onChange={(event) => { const next = event.currentTarget.value; setParts(parse(next, type)); onChange(next); onBlur?.(next); }} />
            </div>
        </div>
    );
}
