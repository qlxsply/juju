import { JuTemporalInput } from "./JuTemporalInput";

export function JuDatePicker({
    label,
    value,
    onChange,
    disabled,
    labelHidden = false,
}: {
    label: string;
    value: string;
    onChange: (value: string) => void;
    disabled?: boolean;
    labelHidden?: boolean;
}) {
    return (
        <JuTemporalInput
            label={label}
            labelHidden={labelHidden}
            type="date"
            value={value}
            onChange={onChange}
            disabled={disabled}
        />
    );
}
