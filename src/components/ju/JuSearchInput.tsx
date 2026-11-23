import { JuButton } from "./JuButton";
import { JuInput } from "./JuInput";

export function JuSearchInput({
    label = "搜索",
    value,
    onValueChange,
    placeholder = "搜索",
}: {
    label?: string;
    value: string;
    onValueChange: (value: string) => void;
    placeholder?: string;
}) {
    return (
        <div className="ju-search-input">
            <JuInput
                label={label}
                type="search"
                value={value}
                onChange={(event) => onValueChange(event.currentTarget.value)}
                placeholder={placeholder}
            />
            {value && (
                <JuButton
                    aria-label="清除搜索"
                    onClick={() => onValueChange("")}
                    size="compact"
                    variant="ghost"
                >
                    清除
                </JuButton>
            )}
        </div>
    );
}
