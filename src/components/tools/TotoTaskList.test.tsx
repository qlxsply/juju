import { useState } from "react";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { TotoItem } from "../../services/desktop";
import { TotoTaskList } from "./TotoTaskList";

const items: TotoItem[] = ["第一条事项", "第二条事项"].map((content, index) => ({
    id: String(index), content, planned_at: null, reminder_at: null,
    created_at: "2026-11-25 12:00", created_order: index,
    reminder_status: "未提醒", reminded_at: null, notes: "较长的备注内容",
}));

describe("TotoTaskList", () => {
    beforeEach(() => {
        vi.stubGlobal("ResizeObserver", class { observe() {} disconnect() {} });
    });
    afterEach(() => vi.unstubAllGlobals());
    it("keeps only the selected row in the tab order and navigates rows with the keyboard", async () => {
        const user = userEvent.setup();
        const onOpen = vi.fn();
        function Example() {
            const [selectedId, setSelectedId] = useState<string | null>(items[0].id);
            return <><button>工具栏</button><main><TotoTaskList items={items} selectedId={selectedId}
                onSelect={(item) => setSelectedId(item.id)} onOpen={onOpen} /></main><button>后续操作</button></>;
        }
        render(<Example />);
        const grid = screen.getByRole("grid", { name: "Toto 进行中事项" });
        const rows = within(grid).getAllByRole("row").slice(1);
        expect(grid).not.toHaveAttribute("tabindex");
        expect(grid.parentElement).not.toHaveAttribute("tabindex");
        for (const header of within(grid).getAllByRole("columnheader")) expect(header).not.toHaveAttribute("tabindex");
        expect(within(grid).queryAllByRole("button")).toHaveLength(0);
        expect(rows[0]).toHaveAttribute("tabindex", "0");
        expect(rows[1]).toHaveAttribute("tabindex", "-1");
        await user.tab();
        expect(screen.getByRole("button", { name: "工具栏" })).toHaveFocus();
        await user.tab();
        expect(rows[0]).toHaveFocus();
        await user.keyboard("{ArrowDown}");
        expect(rows[1]).toHaveFocus();
        expect(rows[1]).toHaveAttribute("aria-selected", "true");
        expect(rows[0]).toHaveAttribute("tabindex", "-1");
        await user.keyboard("{Enter}");
        expect(onOpen).toHaveBeenCalledWith(items[1]);
        await user.keyboard("{ArrowDown}");
        expect(rows[0]).toHaveFocus();
        await user.tab();
        expect(screen.getByRole("button", { name: "后续操作" })).toHaveFocus();
        await user.click(rows[1]);
        expect(rows[1]).toHaveFocus();
        expect(rows[1]).toHaveAttribute("aria-selected", "true");
        await user.dblClick(rows[1]);
        expect(onOpen).toHaveBeenLastCalledWith(items[1]);
    });

    it("does not add a tab stop when the list is empty", () => {
        render(<TotoTaskList items={[]} selectedId={null} onSelect={() => {}} onOpen={() => {}} />);
        const grid = screen.getByRole("grid", { name: "Toto 进行中事项" });
        expect(within(grid).getByText(/暂无进行中事项/)).toBeInTheDocument();
        expect(grid.querySelectorAll("[tabindex]")).toHaveLength(0);
    });

    it("opens and copies truncated text without adding tab stops to the row", async () => {
        vi.spyOn(HTMLElement.prototype, "scrollWidth", "get").mockImplementation(function (this: HTMLElement) {
            return this.classList.contains("ju-overflow-text") ? 200 : 0;
        });
        vi.spyOn(HTMLElement.prototype, "clientWidth", "get").mockImplementation(function (this: HTMLElement) {
            return this.classList.contains("ju-overflow-text") ? 80 : 0;
        });
        const user = userEvent.setup();
        function Example() {
            const [selectedId, setSelectedId] = useState<string | null>(items[0].id);
            return <TotoTaskList items={items} selectedId={selectedId} onSelect={(item) => setSelectedId(item.id)} onOpen={() => {}} />;
        }
        render(<Example />);
        const grid = screen.getByRole("grid", { name: "Toto 进行中事项" });
        const rows = within(grid).getAllByRole("row").slice(1);
        const textButtons = within(rows[1]).getAllByRole("button", { name: /查看完整内容/ });
        expect(textButtons).toHaveLength(2);
        for (const button of textButtons) expect(button).toHaveAttribute("tabindex", "-1");
        await user.click(textButtons[0]);
        expect(rows[1]).toHaveAttribute("aria-selected", "true");
        expect(screen.getByText(items[1].content, { selector: ".ju-overflow-full-text" })).toBeVisible();
        await user.click(screen.getByRole("button", { name: "复制" }));
        expect(screen.getByRole("status")).toHaveTextContent("已复制");
        expect(await navigator.clipboard.readText()).toBe(items[1].content);
        await user.keyboard("{Escape}");
        await user.click(rows[1]);
        await user.keyboard("{F2}");
        expect(rows[1]).toHaveFocus();
    });
});
