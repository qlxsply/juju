import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { JuButton } from "./JuButton";
import { JuCheckbox } from "./JuCheckbox";
import { JuDataTable } from "./JuDataTable";
import { JuDialog } from "./JuDialog";
import { JuInput } from "./JuInput";
import { JuPagination } from "./JuPagination";
import { JuSelect } from "./JuSelect";
import { JuSwitch } from "./JuSwitch";

describe("Ju components", () => {
    it("provides consistent disabled and loading button behavior", () => {
        render(<JuButton loading>Saving</JuButton>);

        expect(screen.getByRole("button", { name: "Saving" })).toBeDisabled();
        expect(screen.getByRole("button")).toHaveAttribute("aria-busy", "true");
    });

    it("connects an input label and validation message accessibly", () => {
        render(<JuInput label="名称" error="名称不能为空" />);

        expect(screen.getByRole("textbox", { name: "名称" })).toHaveAttribute(
            "aria-invalid",
            "true",
        );
        expect(screen.getByText("名称不能为空")).toBeInTheDocument();
    });

    it("opens a dialog from its trigger", async () => {
        const user = userEvent.setup();
        render(
            <JuDialog title="确认操作" trigger={<JuButton>打开</JuButton>}>
                <p>确认内容</p>
            </JuDialog>,
        );

        await user.click(screen.getByRole("button", { name: "打开" }));
        expect(
            screen.getByRole("dialog", { name: "确认操作" }),
        ).toBeInTheDocument();
        expect(screen.getByText("确认内容")).toBeInTheDocument();
    });

    it("lets users choose an option with keyboard interaction", async () => {
        const user = userEvent.setup();
        const onValueChange = vi.fn();
        render(
            <JuSelect
                ariaLabel="主题"
                value="light"
                onValueChange={onValueChange}
                options={[
                    { value: "light", label: "浅色" },
                    { value: "dark", label: "深色" },
                ]}
            />,
        );

        screen.getByRole("combobox", { name: "主题" }).focus();
        await user.keyboard("{ArrowDown}{ArrowDown}{Enter}");
        expect(onValueChange).toHaveBeenCalledWith("dark");
    });

    it("renders configured table columns and rows", () => {
        render(
            <JuDataTable
                columns={[
                    {
                        id: "name",
                        header: "名称",
                        cell: (row: { name: string }) => row.name,
                    },
                ]}
                data={[{ name: "Toto" }]}
                getRowId={(row) => row.name}
                caption="工具清单"
            />,
        );

        expect(
            screen.getByRole("columnheader", { name: "名称" }),
        ).toBeInTheDocument();
        expect(screen.getByRole("cell", { name: "Toto" })).toBeInTheDocument();
    });

    it("exposes checkbox and switch controls with labels", async () => {
        const user = userEvent.setup();
        const onCheckboxChange = vi.fn();
        const onSwitchChange = vi.fn();
        render(
            <>
                <JuCheckbox
                    label="启用选项"
                    checked={false}
                    onCheckedChange={onCheckboxChange}
                />
                <JuSwitch
                    label="启用提醒"
                    checked={false}
                    onCheckedChange={onSwitchChange}
                />
            </>,
        );

        await user.click(screen.getByRole("checkbox", { name: "启用选项" }));
        await user.click(screen.getByRole("switch", { name: "启用提醒" }));
        expect(onCheckboxChange).toHaveBeenCalledWith(true);
        expect(onSwitchChange).toHaveBeenCalledWith(true);
    });

    it("prevents pagination from moving beyond the last page", async () => {
        const user = userEvent.setup();
        const onPageChange = vi.fn();
        render(
            <JuPagination page={2} pageCount={2} onPageChange={onPageChange} />,
        );

        expect(screen.getByRole("button", { name: "下一页" })).toBeDisabled();
        await user.click(screen.getByRole("button", { name: "上一页" }));
        expect(onPageChange).toHaveBeenCalledWith(1);
    });
});
