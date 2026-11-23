import { addMonths, format } from "date-fns";
import { zhCN } from "date-fns/locale";
import { ChevronDown, ChevronLeft, ChevronRight, ChevronsLeft, ChevronsRight } from "lucide-react";
import type { ReactNode } from "react";
import { DayPicker } from "react-day-picker";
import {
    SelectContent,
    SelectItem,
    SelectItemText,
    SelectPortal,
    SelectRoot,
    SelectTrigger,
    SelectValue,
    SelectViewport,
} from "./select";

function PeriodSelect({
    label,
    value,
    options,
    onValueChange,
}: {
    label: string;
    value: string;
    options: { value: string; label: string }[];
    onValueChange: (value: string) => void;
}) {
    return (
        <SelectRoot value={value} onValueChange={onValueChange}>
            <SelectTrigger aria-label={label} className="ju-calendar-period-trigger">
                <SelectValue />
                <ChevronDown aria-hidden="true" className="ju-calendar-period-chevron" size={12} />
            </SelectTrigger>
            <SelectPortal>
                <SelectContent className="ju-select-content ju-calendar-period-list" position="popper" sideOffset={4}>
                    <SelectViewport>
                        {options.map((option) => (
                            <SelectItem className="ju-select-item" key={option.value} value={option.value}>
                                <SelectItemText>{option.label}</SelectItemText>
                            </SelectItem>
                        ))}
                    </SelectViewport>
                </SelectContent>
            </SelectPortal>
        </SelectRoot>
    );
}

function MonthButton({
    label,
    onClick,
    children,
}: {
    label: string;
    onClick: () => void;
    children: ReactNode;
}) {
    return (
        <button aria-label={label} className="ju-calendar-nav-button" onClick={onClick} type="button">
            {children}
        </button>
    );
}

export function Calendar({
    selected,
    onSelect,
    month,
    onMonthChange,
    startYear = 1900,
    endYear = 2100,
}: {
    selected?: Date;
    onSelect: (date: Date | undefined) => void;
    month: Date;
    onMonthChange: (date: Date) => void;
    startYear?: number;
    endYear?: number;
}) {
    const currentYear = month.getFullYear();
    const currentMonth = month.getMonth();
    const years = Array.from({ length: endYear - startYear + 1 }, (_, index) => {
        const year = startYear + index;
        return { value: String(year), label: `${year}年` };
    });
    const months = Array.from({ length: 12 }, (_, index) => ({
        value: String(index),
        label: `${index + 1}月`,
    }));

    const changeYear = (year: string) => onMonthChange(new Date(Number(year), currentMonth, 1));
    const changeMonth = (monthIndex: string) => onMonthChange(new Date(currentYear, Number(monthIndex), 1));

    return (
        <div className="ju-calendar-shell">
            <div className="ju-calendar-toolbar">
                <MonthButton label="上一年" onClick={() => onMonthChange(new Date(currentYear - 1, currentMonth, 1))}>
                    <ChevronsLeft aria-hidden="true" size={14} />
                </MonthButton>
                <MonthButton label="上个月" onClick={() => onMonthChange(addMonths(month, -1))}>
                    <ChevronLeft aria-hidden="true" size={16} />
                </MonthButton>
                <PeriodSelect label="选择年份" onValueChange={changeYear} options={years} value={String(currentYear)} />
                <PeriodSelect label="选择月份" onValueChange={changeMonth} options={months} value={String(currentMonth)} />
                <MonthButton label="下个月" onClick={() => onMonthChange(addMonths(month, 1))}>
                    <ChevronRight aria-hidden="true" size={16} />
                </MonthButton>
                <MonthButton label="下一年" onClick={() => onMonthChange(new Date(currentYear + 1, currentMonth, 1))}>
                    <ChevronsRight aria-hidden="true" size={14} />
                </MonthButton>
            </div>
            <DayPicker
                mode="single"
                required
                selected={selected}
                onSelect={onSelect}
                month={month}
                onMonthChange={onMonthChange}
                startMonth={new Date(startYear, 0)}
                endMonth={new Date(endYear, 11)}
                locale={zhCN}
                showOutsideDays
                fixedWeeks
                hideNavigation
                className="ju-calendar"
            />
            <span className="sr-only" aria-live="polite">{format(month, "yyyy年M月", { locale: zhCN })}</span>
        </div>
    );
}
