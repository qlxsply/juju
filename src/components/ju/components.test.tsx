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
import { focusFirstInvalidField } from "./validation";
import { ValidationIndicator } from "./ValidationIndicator";

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
        expect(screen.getByRole("textbox", { name: "名称" })).toHaveAccessibleDescription("错误：名称不能为空");
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
        expect(screen.getByRole("dialog", { name: "确认操作" })).not.toHaveAttribute("aria-describedby");
        expect(document.querySelector(".ju-dialog-description")).not.toBeInTheDocument();
        expect(screen.getByText("确认内容")).toBeInTheDocument();
    });

    it("keeps an optional dialog description accessible", async () => {
        const user = userEvent.setup();
        render(<JuDialog title="说明" description="补充说明" trigger={<JuButton>打开说明</JuButton>} />);
        await user.click(screen.getByRole("button", { name: "打开说明" }));
        expect(screen.getByRole("dialog", { name: "说明" })).toHaveAccessibleDescription("补充说明");
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

    it("submits and resets an uncontrolled named select", async () => {
        const user = userEvent.setup();
        render(<form aria-label="筛选">
            <JuSelect label="分类" name="category" defaultValue="a" options={[
                { value: "a", label: "甲" }, { value: "b", label: "乙" },
            ]} />
            <button type="reset">重置</button>
        </form>);
        const form = screen.getByRole("form", { name: "筛选" }) as HTMLFormElement;
        await user.click(screen.getByRole("combobox", { name: "分类" }));
        await user.click(screen.getByRole("option", { name: "乙" }));
        expect(new FormData(form).get("category")).toBe("b");
        await user.click(screen.getByRole("button", { name: "重置" }));
        expect(new FormData(form).get("category")).toBe("a");
    });

    it("keeps a select usable inside a dialog", async () => {
        const user = userEvent.setup();
        render(<JuDialog title="筛选设置" trigger={<JuButton>打开筛选</JuButton>}>
            <JuSelect label="筛选分类" options={[{ value: "a", label: "甲" }, { value: "b", label: "乙" }]} />
        </JuDialog>);
        await user.click(screen.getByRole("button", { name: "打开筛选" }));
        const select = screen.getByRole("combobox", { name: "筛选分类" });
        await user.click(select);
        await user.click(screen.getByRole("option", { name: "乙" }));
        expect(select).toHaveTextContent("乙");
        expect(screen.getByRole("dialog", { name: "筛选设置" })).toBeInTheDocument();
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

    it("opens multiple messages and focuses the first invalid field on request", async () => {
        const user = userEvent.setup();
        const { container } = render(<>
            <JuInput label="错误字段" error="必须填写" />
            <ValidationIndicator fieldId="multiple" messages={[{ level: "warning", text: "检查格式" }, { level: "info", text: "辅助说明" }]} />
        </>);
        await user.click(screen.getByRole("button", { name: "查看警告信息" }));
        expect(screen.getByText("检查格式")).toBeVisible();
        expect(screen.getByText("辅助说明")).toBeVisible();
        await user.keyboard("{Escape}");
        expect(focusFirstInvalidField(container)).toBe(true);
        expect(screen.getByRole("textbox", { name: "错误字段" })).toHaveFocus();
        expect(await screen.findByText("必须填写", { selector: ".ju-validation-popover p" })).toBeVisible();
    });

    it("supports uncontrolled and indeterminate choices with accessible descriptions", async () => {
        const user = userEvent.setup();
        render(<>
            <JuCheckbox label="部分选中" checked="indeterminate" description="可选择" />
            <JuCheckbox label="普通勾选" defaultChecked />
            <JuSwitch label="普通开关" defaultChecked />
        </>);
        const mixed = screen.getByRole("checkbox", { name: "部分选中" });
        expect(mixed).toHaveAttribute("aria-checked", "mixed");
        expect(mixed).toHaveAccessibleDescription("提示：可选择");
        expect(mixed.querySelector("svg")).toBeInTheDocument();
        await user.click(screen.getByLabelText("普通勾选"));
        expect(screen.getByRole("checkbox", { name: "普通勾选" })).not.toBeChecked();
        await user.click(screen.getByLabelText("普通开关"));
        expect(screen.getByRole("switch", { name: "普通开关" })).not.toBeChecked();
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
