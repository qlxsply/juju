import { expect, test } from "@playwright/test";

test("overflow text shows and copies complete truncated content", async ({ page, context }) => {
    await context.grantPermissions(["clipboard-read", "clipboard-write"]);
    await page.goto("/");
    const [design] = await Promise.all([
        page.waitForEvent("popup"),
        page.getByRole("button", { name: /设计系统工作台/ }).click(),
    ]);
    const section = design.locator("#data");
    const short = section.getByText("简短内容", { exact: true });
    await expect(short).not.toHaveAttribute("tabindex");
    const text = "这是超出单元格宽度的长文本，可以展开查看和复制完整内容。";
    const trigger = section.getByRole("button", { name: `查看完整内容：${text}` });
    await expect(trigger).toBeVisible();
    await trigger.focus();
    await trigger.press("Enter");
    const popover = design.locator(".ju-overflow-popover");
    await expect(popover).toBeVisible();
    await expect(popover.locator(".ju-overflow-full-text")).toHaveText(text);
    await popover.getByRole("button", { name: "复制" }).click();
    await expect(popover.getByRole("status")).toHaveText("已复制");
    expect(await design.evaluate(() => navigator.clipboard.readText())).toBe(text);
    await design.keyboard.press("Escape");
    await expect(popover).toBeHidden();
    await trigger.hover();
    await expect(popover).toBeVisible();
    await trigger.click();
    await popover.getByRole("button", { name: "复制" }).click();
    await expect(popover.getByRole("status")).toHaveText("已复制");
    await design.close();
});

test("reference launcher and settings control the workbench theme", async ({
    page,
}) => {
    await page.goto("/");
    await expect(page.getByText("juju", { exact: true })).toBeVisible();
    const titlebar = page.locator(".main-window .titlebar");
    expect(await titlebar.evaluate((element) => getComputedStyle(element).cursor)).toBe("default");
    await expect(titlebar).not.toHaveAttribute("data-tauri-drag-region");
    await page.getByRole("button", { name: "设置" }).click();
    await page.getByRole("button", { name: "显示设置" }).click();
    await page.getByRole("combobox", { name: "应用主题" }).click();
    await page.getByRole("option", { name: "深色" }).click();
    await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
    await page.getByRole("button", { name: "关闭设置" }).click();
    const [showcase] = await Promise.all([
        page.waitForEvent("popup"),
        page.getByRole("button", { name: /设计系统工作台/ }).click(),
    ]);
    await expect(
        showcase.getByRole("heading", { name: "Design Tokens" }),
    ).toBeVisible();
    await expect(showcase.locator(".token-card")).toHaveCount(0);
    await expect(showcase.locator(".color-token:not(.layout-token)")).toHaveCount(27);
    await showcase.close();
    await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
});

test("button and status examples document each variant and provide a working actions menu", async ({ page }) => {
    await page.goto("/");
    const [design] = await Promise.all([
        page.waitForEvent("popup"),
        page.getByRole("button", { name: /设计系统工作台/ }).click(),
    ]);
    const section = design.locator("#buttons");
    const examples = section.locator(".showcase-example");
    await expect(examples).toHaveCount(16);
    for (const example of await examples.all()) {
        await expect(example.locator("h4")).not.toBeEmpty();
        await expect(example.locator("code")).not.toBeEmpty();
        await expect(example.locator("p")).not.toBeEmpty();
        await expect(example.locator(".showcase-example-preview")).toBeVisible();
        await expect(example).toHaveCSS("border-top-width", "0px");
    }
    await expect(section.getByRole("button", { name: "不可用" })).toBeDisabled();
    await expect(section.getByRole("button", { name: "正在处理" })).toHaveAttribute("aria-busy", "true");
    const more = section.getByRole("button", { name: "更多操作" });
    await more.click();
    for (const name of ["编辑", "复制", "删除"]) {
        await expect(design.getByRole("menuitem", { name })).toBeVisible();
    }
    await design.getByRole("menuitem", { name: "复制" }).click();
    await expect(section.getByRole("status").filter({ hasText: "示例操作：复制" })).toBeVisible();
    await expect(design.getByRole("menu")).toHaveCount(0);
    await more.focus();
    await more.press("Enter");
    await expect(design.getByRole("menu")).toBeVisible();
    await design.keyboard.press("Escape");
    await expect(design.getByRole("menu")).toHaveCount(0);
    await expect(more).toBeFocused();
    await design.close();
});

test("date and time pickers use bounded sliders and native navigation visuals", async ({ page }) => {
    await page.goto("/");
    const [design] = await Promise.all([
        page.waitForEvent("popup"),
        page.getByRole("button", { name: /设计系统工作台/ }).click(),
    ]);
    const pickerFor = async (name: string) => {
        const input = design.getByRole("textbox", { name, exact: true });
        const id = await input.getAttribute("id");
        await input.click();
        const picker = design.locator(`.air-datepicker.juju-air-picker-${id!.replace(/[^a-zA-Z0-9_-]/g, "")}`);
        await expect(picker).toHaveClass(/-juju-visible-/);
        return { input, picker };
    };

    const { input: dateInput, picker: date } = await pickerFor("计划日期");
    await expect(date.locator(".air-datepicker--pointer")).toBeVisible();
    const pointerCenterOffset = await date.evaluate((element) => {
        const popup = element.getBoundingClientRect();
        const arrow = element.querySelector(".air-datepicker--pointer")!.getBoundingClientRect();
        return Math.abs(arrow.left + arrow.width / 2 - (popup.left + popup.width / 2));
    });
    expect(pointerCenterOffset).toBeLessThan(2);
    await expect(date.locator(".air-datepicker-body--cells.-days- .air-datepicker-cell")).toHaveCount(42);
    await expect(date.locator(".air-datepicker-nav--action svg").first()).toHaveCSS("width", "32px");
    const dayColor = await date.locator(".air-datepicker-body--day-name").first().evaluate((element) => getComputedStyle(element).color);
    const mutedColor = await date.locator(".air-datepicker-nav--action path").first().evaluate((element) => getComputedStyle(element).stroke);
    expect(dayColor).not.toBe(mutedColor);
    await expect(date).toHaveCSS("transform-origin", /^[\d.]+px 0px/);
    const openAnimation = await date.evaluate((element) => element.getAnimations().map((animation) => ({
        duration: animation.effect?.getTiming().duration,
        easing: animation.effect?.getTiming().easing,
        frames: (animation.effect as KeyframeEffect).getKeyframes().map((frame) => frame.transform),
    })));
    expect(openAnimation.some((animation) => animation.duration === 520 && animation.frames.some((frame) => frame?.includes("rotateX(-88deg)")))).toBe(true);
    await dateInput.press("Escape");
    await design.getByRole("textbox", { name: "名称", exact: true }).focus();
    await dateInput.focus();
    await expect(date).toHaveClass(/-juju-visible-/);
    await date.evaluate(async (element) => { await Promise.allSettled(element.getAnimations().map((animation) => animation.finished)); });
    await expect(date).toBeVisible();

    const { input: timeInput, picker: time } = await pickerFor("计划时间");
    await expect(time.locator(".air-datepicker--time")).toHaveCSS("border-top-width", "0px");
    const hours = time.locator('input[name="hours"]');
    const minutes = time.locator('input[name="minutes"]');
    await expect(hours).toHaveAttribute("min", "0");
    await expect(hours).toHaveAttribute("max", "23");
    await expect(minutes).toHaveAttribute("min", "0");
    await expect(minutes).toHaveAttribute("max", "59");
    await timeInput.fill("12:60");
    await timeInput.press("Tab");
    await expect(timeInput).toHaveAttribute("aria-invalid", "true");
    await timeInput.fill("23:59");
    await timeInput.press("Tab");
    await expect(timeInput).not.toHaveAttribute("aria-invalid", "true");
    await expect(timeInput).toHaveValue("23:59");

    const { input: boundedInput, picker: bounded } = await pickerFor("时间范围与步长");
    await expect(bounded.locator('input[name="hours"]')).toHaveAttribute("min", "8");
    await expect(bounded.locator('input[name="hours"]')).toHaveAttribute("max", "18");
    await expect(bounded.locator('input[name="minutes"]')).toHaveAttribute("step", "5");
    await expect(bounded.locator('input[name="minutes"]')).toHaveAttribute("max", "59");
    await boundedInput.fill("18:00");
    await boundedInput.press("Tab");
    await expect(boundedInput).toHaveValue("18:00");
    await boundedInput.click();
    await expect(bounded.locator('input[name="minutes"]')).toHaveAttribute("max", "0");

    await design.getByRole("textbox", { name: "名称", exact: true }).focus();
    const { picker: datetime } = await pickerFor("日期和时间");
    await expect(datetime.locator(".air-datepicker--time")).toHaveCSS("border-top-width", "1px");
    await expect(datetime.locator(".air-datepicker--pointer")).toBeVisible();
    await expect(datetime.locator('input[name="hours"]')).toHaveAttribute("max", "23");
    await expect(datetime.locator('input[name="minutes"]')).toHaveAttribute("max", "59");
    await design.close();
});

test("time popup clears selected time while preserving slider position", async ({ page }) => {
    await page.goto("/");
    const [design] = await Promise.all([
        page.waitForEvent("popup"),
        page.getByRole("button", { name: /设计系统工作台/ }).click(),
    ]);
    const input = design.getByRole("textbox", { name: "计划时间", exact: true });
    await input.fill("14:18");
    await input.press("Tab");
    await input.click();
    const id = (await input.getAttribute("id"))!.replace(/[^a-zA-Z0-9_-]/g, "");
    const popup = design.locator(`.air-datepicker.juju-air-picker-${id}`);
    const hours = popup.locator('input[name="hours"]');
    const minutes = popup.locator('input[name="minutes"]');
    await expect(hours).toHaveValue("14");
    await expect(minutes).toHaveValue("18");
    await popup.getByRole("button", { name: "清空已选时间" }).click();
    await expect(input).toHaveValue("");
    await expect(popup).toHaveClass(/-juju-time-empty-/);
    await expect(hours).toHaveValue("14");
    await expect(minutes).toHaveValue("18");
    await minutes.focus();
    await minutes.press("ArrowRight");
    await expect(input).toHaveValue("14:19");
    await expect(popup).not.toHaveClass(/-juju-time-empty-/);
    await popup.getByRole("button", { name: "清空已选时间" }).focus();
    await design.keyboard.press("Enter");
    await expect(input).toHaveValue("");
    await design.getByRole("textbox", { name: "备注", exact: true }).click();
    await input.click();
    await expect(input).toHaveValue("");
    await expect(popup).toHaveClass(/-juju-time-empty-/);
    await expect(minutes).toHaveValue("19");
    await minutes.click();
    await expect(input).not.toHaveValue("");
    const bounded = design.getByRole("textbox", { name: "时间范围与步长", exact: true });
    await bounded.click();
    const boundedId = (await bounded.getAttribute("id"))!.replace(/[^a-zA-Z0-9_-]/g, "");
    const boundedPopup = design.locator(`.air-datepicker.juju-air-picker-${boundedId}`);
    await expect(boundedPopup.locator('input[name="minutes"]')).toHaveValue("30");
    await boundedPopup.getByRole("button", { name: "清空已选时间" }).click();
    await expect(bounded).toHaveValue("");
    const boundedMinutes = boundedPopup.locator('input[name="minutes"]');
    await expect(boundedMinutes).toHaveValue("30");
    await boundedMinutes.focus();
    await boundedMinutes.press("ArrowRight");
    await expect(bounded).toHaveValue("09:35");
    await boundedMinutes.press("ArrowRight");
    await expect(bounded).toHaveValue("09:40");
    await bounded.click();
    await boundedPopup.getByRole("button", { name: "清空已选时间" }).click();
    await expect(bounded).toHaveValue("");
    await boundedMinutes.focus();
    await boundedMinutes.press("ArrowLeft");
    await expect(bounded).toHaveValue("09:35");
    const dateTime = design.getByRole("textbox", { name: "日期和时间", exact: true });
    await dateTime.click();
    const datetimeId = (await dateTime.getAttribute("id"))!.replace(/[^a-zA-Z0-9_-]/g, "");
    await expect(design.locator(`.air-datepicker.juju-air-picker-${datetimeId} [data-juju-time-toggle]`)).toHaveCount(0);
    await design.close();
});

test("range pickers select dates and Delete clears the entire value", async ({ page }) => {
    await page.goto("/");
    const [design] = await Promise.all([
        page.waitForEvent("popup"),
        page.getByRole("button", { name: /设计系统工作台/ }).click(),
    ]);
    const input = design.getByRole("textbox", { name: "日期范围", exact: true });
    await input.fill("2026-11-09 - 2026-11-15");
    await input.press("Tab");
    await expect(input).toHaveValue("2026-11-09 - 2026-11-15");
    await input.focus();
    await input.press("Backspace");
    await expect(input).toHaveValue("");
    await input.press("Tab");
    await input.click();
    const rangeId = (await input.getAttribute("id"))!.replace(/[^a-zA-Z0-9_-]/g, "");
    const popup = design.locator(`.air-datepicker.juju-air-picker-${rangeId}`);
    await expect(popup).toBeVisible();
    await popup.locator(".air-datepicker-cell.-day-:not(.-other-month-)").nth(4).click();
    await expect(input).not.toHaveValue("");
    await popup.locator(".air-datepicker-cell.-day-:not(.-other-month-)").nth(8).click();
    await expect(input).toHaveValue(/ - /);
    const date = design.getByRole("textbox", { name: "计划日期", exact: true });
    await date.focus();
    await date.press("Delete");
    await expect(date).toHaveValue("");
    const restricted = design.getByRole("textbox", { name: "禁止跨越的日期范围" });
    await restricted.fill("2026-11-09 - 2026-11-15");
    await restricted.press("Tab");
    await expect(restricted).toHaveAttribute("aria-invalid", "true");
    await design.close();
});

test("data and feedback components use individual showcase rows", async ({ page }) => {
    await page.goto("/");
    const [design] = await Promise.all([
        page.waitForEvent("popup"),
        page.getByRole("button", { name: /设计系统工作台/ }).click(),
    ]);
    const data = design.locator("#data");
    const feedback = design.locator("#feedback");
    await expect(data.locator(".showcase-example")).toHaveCount(4);
    await expect(feedback.locator(".showcase-example")).toHaveCount(7);
    await expect(data.locator(".component-card")).toHaveCount(0);
    await expect(feedback.locator(".component-card")).toHaveCount(0);
    const table = data.getByRole("region", { name: "数据表格" });
    await expect(table.locator('[data-slot="table"]')).toHaveCount(1);
    await data.getByRole("searchbox", { name: "搜索" }).fill("Toto");
    await expect(table.getByRole("cell", { name: "Toto 提醒" })).toBeVisible();
    await expect(table.getByRole("cell", { name: "JSON 文档" })).toHaveCount(0);
    await feedback.getByRole("button", { name: "打开弹窗" }).click();
    await expect(design.getByRole("dialog", { name: "示例弹窗" })).toBeVisible();
    await design.getByRole("button", { name: "完成", exact: true }).click();
    await feedback.getByRole("tab", { name: "详情" }).click();
    await expect(feedback.getByText("第二个面板内容。")).toBeVisible();
    await design.close();
});

test("form showcase exposes checkbox icons and keyboard tooltips", async ({ page }) => {
    await page.goto("/");
    const [design] = await Promise.all([
        page.waitForEvent("popup"),
        page.getByRole("button", { name: /设计系统工作台/ }).click(),
    ]);
    const mixed = design.getByRole("checkbox", { name: "部分选中" });
    await expect(mixed).toHaveAttribute("aria-checked", "mixed");
    await expect(mixed.locator(".ju-checkbox-minus")).toBeVisible();
    await expect(mixed.locator(".ju-checkbox-check")).toBeHidden();
    const tooltipTrigger = design.getByRole("button", { name: "悬停或聚焦" });
    await design.bringToFront();
    await tooltipTrigger.scrollIntoViewIfNeeded();
    await design.getByRole("switch", { name: "无效开关" }).focus();
    await design.keyboard.press("Tab");
    await design.keyboard.press("Tab");
    await expect(design.getByRole("tooltip", { name: "保存当前内容" })).toBeVisible();
    await design.keyboard.press("Escape");
    await expect(design.getByRole("tooltip", { name: "保存当前内容" })).toBeHidden();
    await design.close();
});

test("form controls share a compact visible focus outline", async ({ page }) => {
    await page.goto("/");
    const [design] = await Promise.all([
        page.waitForEvent("popup"),
        page.getByRole("button", { name: /设计系统工作台/ }).click(),
    ]);
    for (const name of ["名称", "备注", "错误示例"]) {
        const field = design.getByRole("textbox", { name, exact: true });
        await field.focus();
        await expect(field).toHaveCSS("outline-width", "2px");
        await expect(field).toHaveCSS("outline-offset", "1px");
        if (name === "错误示例") {
            await expect(field).toHaveAttribute("aria-invalid", "true");
            expect(await field.evaluate((node) => getComputedStyle(node).borderTopColor)).not.toBe(
                await field.evaluate((node) => getComputedStyle(node).outlineColor),
            );
        }
    }
    await design.close();
});

test("date picker commits year and month and locks popup direction", async ({ page }) => {
    await page.goto("/");
    const [design] = await Promise.all([
        page.waitForEvent("popup"),
        page.getByRole("button", { name: /设计系统工作台/ }).click(),
    ]);
    const input = design.getByRole("textbox", { name: "计划日期", exact: true });
    await input.fill("2026-03-31");
    await input.press("Tab");
    await design.evaluate((id) => {
        const el = document.getElementById(id) as HTMLElement;
        Object.assign(el.style, { position: "fixed", top: "auto", bottom: "20px", left: "20px", zIndex: "1000" });
    }, (await input.getAttribute("id"))!);
    await design.waitForTimeout(400);
    await input.click();
    const id = (await input.getAttribute("id"))!.replace(/[^a-zA-Z0-9_-]/g, "");
    const picker = design.locator(`.air-datepicker.juju-air-picker-${id}`);
    await expect(picker).toHaveAttribute("data-popper-placement", /^top/);
    await picker.locator(".air-datepicker-nav--title").click();
    await picker.locator(".air-datepicker-nav--title").click();
    await picker.locator('[data-juju-year="2024"]').click();
    await expect(input).toHaveValue("2024-03-31");
    await expect(picker).toHaveAttribute("data-popper-placement", /^top/);
    await picker.locator('[data-juju-month="2"]').click();
    await expect(input).toHaveValue("2024-02-29");
    await expect(picker).toHaveAttribute("data-popper-placement", /^top/);
    await picker.locator(".air-datepicker-nav--title").click();
    await picker.locator(".air-datepicker-nav--action").first().click();
    await expect(input).toHaveValue("2024-02-29");
    await picker.locator('[data-juju-month="4"][data-juju-year="2023"]').click();
    await expect(input).toHaveValue("2023-04-29");
    await design.close();
});

test("design workbench showcases public components and keeps theme controls stable", async ({ page }) => {
    await page.goto("/");
    const [design] = await Promise.all([
        page.waitForEvent("popup"),
        page.getByRole("button", { name: /设计系统工作台/ }).click(),
    ]);
    await expect(design.locator(".showcase-header")).toHaveCount(0);
    const navigation = design.getByRole("navigation", { name: "设计系统导航" });
    await expect(navigation).toBeVisible();
    await expect(design.locator(".sidebar-heading")).toHaveCount(0);
    await expect(navigation.getByRole("link", { name: "预览环境" })).toBeVisible();
    await expect(design.locator(".showcase-section")).toHaveCount(6);
    for (const section of ["#forms", "#data", "#feedback"]) {
        const borders = await design.locator(`${section} .showcase-example`).evaluateAll((rows) =>
            rows.map((row) => getComputedStyle(row).borderBottomWidth),
        );
        expect(borders.every((width) => width === "0px")).toBe(true);
    }
    for (const heading of ["预览环境", "Design Tokens", "按钮与状态", "表单组件", "数据组件", "弹窗与反馈"]) {
        await expect(design.getByRole("heading", { name: heading })).toBeVisible();
    }
    const headingStyles = await design.locator("#environment h2, #design-tokens h2").evaluateAll((headings) =>
        headings.map((heading) => {
            const style = getComputedStyle(heading);
            return [style.fontFamily, style.fontSize, style.fontWeight, style.lineHeight];
        }),
    );
    expect(headingStyles[0]).toEqual(headingStyles[1]);
    await expect(design.locator("#environment + #design-tokens")).toHaveCount(1);
    await navigation.getByRole("link", { name: "颜色", exact: true }).click();
    await expect(design).toHaveURL(/#token-/);
    await expect(design.locator("#token-颜色")).toBeInViewport();
    await navigation.getByText("组件示例", { exact: true }).click();
    await expect(navigation.getByRole("link", { name: "表单组件" })).toBeHidden();
    await navigation.getByText("组件示例", { exact: true }).click();
    await navigation.getByRole("link", { name: "表单组件" }).click();
    await expect(design.locator("#forms")).toBeInViewport();
    await expect(design.getByRole("button", { name: "返回 Juju" })).toHaveCount(0);
    await expect(design.locator(".token-environment-settings")).toBeVisible();
    await expect(design.locator(".token-environment-settings")).toHaveCSS("border-top-width", "0px");
    await expect(design.locator(".token-environment-settings")).toHaveCSS("background-color", "rgba(0, 0, 0, 0)");
    await expect(design.locator(".token-card")).toHaveCount(0);
    await expect(design.locator(".theme-hint")).toHaveCount(0);
    const colorTokens = design.locator(".color-token:not(.layout-token)");
    await expect(colorTokens).toHaveCount(27);
    const swatchColors = await design.locator(".color-token-swatch").evaluateAll((swatches) =>
        swatches.map((swatch) => getComputedStyle(swatch).backgroundColor),
    );
    expect(swatchColors).toHaveLength(27);
    expect(swatchColors.every((color) => color !== "rgba(0, 0, 0, 0)")).toBe(true);
    const colorTokenList = await colorTokens.all();
    for (const [index, token] of colorTokenList.entries()) {
        await expect(token.locator("h4")).not.toBeEmpty();
        await expect(token.locator("code").first()).toHaveText(/^--/);
        const values = swatchColors[index].match(/[\d.]+/g)!.map(Number);
        const toHex = (value: number) => Math.round(value).toString(16).padStart(2, "0").toUpperCase();
        const expectedHex = `#${values.slice(0, 3).map(toHex).join("")}${values[3] === undefined || values[3] >= 1 ? "" : toHex(values[3] * 255)}`;
        await expect(token.locator("code").last()).toHaveText(expectedHex);
        await expect(token.locator("p")).not.toBeEmpty();
        await expect(token.locator(".color-token-example")).not.toBeEmpty();
        await expect(token).toHaveCSS("border-top-width", "0px");
    }
    const firstColorBox = await colorTokens.nth(0).boundingBox();
    const secondColorBox = await colorTokens.nth(1).boundingBox();
    expect(firstColorBox).not.toBeNull();
    expect(secondColorBox).not.toBeNull();
    expect(secondColorBox!.y).toBeGreaterThanOrEqual(firstColorBox!.y + firstColorBox!.height);
    await expect(design.locator('[data-token="--foreground"] .color-token-example')).toHaveClass(/is-text/);
    await expect(design.locator('[data-token="--background"] .color-token-example')).toHaveClass(/is-background/);
    await expect(design.locator('[data-token="--overlay"] code').last()).toHaveText("#0F172A61");
    const headerBoxes = await Promise.all([
        colorTokens.first().locator("h4").boundingBox(),
        colorTokens.first().locator("code").first().boundingBox(),
        colorTokens.first().locator("code").last().boundingBox(),
    ]);
    expect(headerBoxes.every((box) => box && Math.abs(box.y - headerBoxes[0]!.y) < 5)).toBe(true);
    for (const [category, count] of [["圆角", 6], ["边框", 6], ["尺寸", 12], ["间距", 6], ["字体", 12], ["阴影", 3]] as const) {
        const tokens = design.locator(`[id="token-${category}"] .layout-token`);
        await expect(tokens).toHaveCount(count);
        for (const token of await tokens.all()) {
            await expect(token.locator("p")).not.toBeEmpty();
            await expect(token.locator(".layout-token-example")).toBeVisible();
            await expect(token).toHaveCSS("border-top-width", "0px");
        }
        const first = await tokens.nth(0).boundingBox();
        const second = await tokens.nth(1).boundingBox();
        expect(second!.y).toBeGreaterThanOrEqual(first!.y + first!.height);
    }
    await expect(design.locator('[data-token="--radius-lg"] .layout-token-example')).toHaveCSS("border-radius", "10px");
    await expect(design.locator('[data-token="--border-style-dashed"] .layout-token-example')).toHaveCSS("border-top-style", "dashed");
    await expect(design.locator('[data-token="--button-height-lg"] .layout-token-example')).toHaveCSS("height", "40px");
    await expect(design.locator('[data-token="--space-8"] .layout-token-example')).toHaveCSS("column-gap", "32px");
    await expect(design.locator('[data-token="--font-size-xl"] .layout-token-example')).toHaveCSS("font-size", "18px");
    await expect(design.locator('[data-token="--font-weight-semibold"] .layout-token-example')).toHaveCSS("font-weight", "600");
    for (const [variable, height] of [["--line-height-tight", "15px"], ["--line-height-normal", "18px"], ["--line-height-relaxed", "19.2px"]] as const) {
        const example = design.locator(`[data-token="${variable}"] .layout-token-example`);
        await expect(example).toHaveCSS("line-height", height);
        await expect(example.locator("span")).toHaveCount(3);
    }
    for (const variable of ["--shadow-popover", "--shadow-dialog", "--shadow-menu"]) {
        const example = design.locator(`[data-token="${variable}"] .layout-token-example`);
        const [shadow, token] = await example.evaluate((element, name) => [
            getComputedStyle(element).boxShadow,
            getComputedStyle(document.documentElement).getPropertyValue(name).trim(),
        ], variable);
        expect(shadow).not.toBe("none");
        expect(token).not.toBe("");
    }
    for (const [variable, duration, slowDuration] of [["--duration-fast", "0.1s", "0.5s"], ["--duration-normal", "0.16s", "0.8s"], ["--ease-standard", "0.16s", "0.8s"]] as const) {
        const token = design.locator(`[data-token="${variable}"]`);
        const example = token.locator(".motion-token-example");
        const button = example.getByRole("button", { name: /使用示例/ });
        const thumb = example.locator(".motion-token-track:not(.is-slow) .motion-token-thumb");
        const slowThumb = example.locator(".motion-token-track.is-slow:not(.is-linear) .motion-token-thumb");
        await expect(thumb).toHaveCSS("transition-duration", duration);
        await expect(slowThumb).toHaveCSS("transition-duration", slowDuration);
        await expect(thumb).toHaveCSS("transition-timing-function", "cubic-bezier(0.2, 0, 0, 1)");
        await expect(example.getByText("慢放（时长×5）")).toBeVisible();
        if (variable === "--ease-standard") {
            await expect(example.locator(".is-linear .motion-token-thumb")).toHaveCSS("transition-timing-function", "linear");
        }
        await button.click();
        await expect(button).toHaveAttribute("aria-pressed", "true");
        await expect.poll(async () => {
            const [track, dot] = await Promise.all([slowThumb.locator("..").boundingBox(), slowThumb.boundingBox()]);
            return Math.abs(track!.x + track!.width - dot!.x - dot!.width - 2);
        }).toBeLessThan(1);
        await button.click();
        await expect(button).toHaveAttribute("aria-pressed", "false");
    }
    for (const [variable, zIndex] of [["--z-dropdown", "20"], ["--z-popover", "30"], ["--z-dialog", "40"], ["--z-toast", "50"]] as const) {
        const example = design.locator(`[data-token="${variable}"] .layer-token-example`);
        await example.scrollIntoViewIfNeeded();
        await expect(example.locator(".layer-token-item")).toHaveCount(4);
        await expect(example.locator(".is-current")).toHaveCSS("z-index", zIndex);
        const stacked = await example.locator(".layer-token-item").evaluateAll((items) =>
            items.map((item) => ({ z: Number(getComputedStyle(item).zIndex), box: item.getBoundingClientRect().toJSON() })),
        );
        expect(stacked.map((item) => item.z)).toEqual([50, 40, 30, 20]);
        expect(stacked[0].box.left).toBeLessThan(stacked[1].box.right);
        expect(stacked[0].box.top).toBeLessThan(stacked[1].box.bottom);
        const topLayer = await example.evaluate((element) => {
            const items = element.querySelectorAll(".layer-token-item");
            const top = items[0].getBoundingClientRect();
            const below = items[1].getBoundingClientRect();
            const x = (Math.max(top.left, below.left) + Math.min(top.right, below.right)) / 2;
            const y = (Math.max(top.top, below.top) + Math.min(top.bottom, below.bottom)) / 2;
            return document.elementFromPoint(x, y)?.textContent;
        });
        expect(topLayer).toBe("通知");
    }
    const nameField = design.getByRole("textbox", { name: "名称", exact: true });
    await expect(nameField).toHaveAccessibleDescription("提示：名称仅用于本地显示");
    const nameRow = design.locator(".ju-field").filter({ has: nameField });
    await nameRow.getByRole("button", { name: "查看提示信息" }).click();
    await expect(design.locator(".ju-validation-popover")).toContainText("名称仅用于本地显示");
    await design.keyboard.press("Escape");
    await expect(design.getByRole("textbox", { name: "警告示例" })).toHaveAccessibleDescription("警告：接近长度上限");
    await expect(design.getByRole("textbox", { name: "错误示例" })).toHaveAccessibleDescription("错误：请检查该字段");
    const dateField = design.getByRole("textbox", { name: "计划日期", exact: true });
    const dateInputId = await dateField.getAttribute("id");
    const datePicker = design.locator(`.air-datepicker.juju-air-picker-${dateInputId!.replace(/[^a-zA-Z0-9_-]/g, "")}`);
    const dateFieldBox = await dateField.boundingBox();
    expect(dateFieldBox).not.toBeNull();
    await dateField.click({ position: { x: dateFieldBox!.width - 8, y: dateFieldBox!.height / 2 } });
    let picker = datePicker;
    await expect(picker).toBeVisible();
    await expect(picker).toHaveCSS("width", "246px");
    const monthActions = picker.locator(".air-datepicker-nav--action");
    await expect(monthActions).toHaveCount(2);
    const monthTitle = picker.locator(".air-datepicker-nav--title");
    const monthBeforeNavigation = await monthTitle.innerText();
    await monthActions.last().click();
    await expect(monthTitle).not.toHaveText(monthBeforeNavigation);
    await monthActions.first().click();
    await expect(monthTitle).toHaveText(monthBeforeNavigation);
    await picker.locator(".air-datepicker-nav--title").click();
    await picker.locator(".air-datepicker-nav--title").click();
    await picker.locator(".air-datepicker-cell.-year-").filter({ hasText: /^2027$/ }).click();
    const afterYearView = await picker.locator(".air-datepicker-body--cells:visible").getAttribute("class");
    if (afterYearView?.includes("-years-")) await picker.locator(".air-datepicker-nav--title").click();
    await picker.getByText("二月", { exact: true }).click();
    await picker.locator(".air-datepicker-cell.-day-:not(.-other-month-)").filter({ hasText: /^4$/ }).click();
    await expect(dateField).toHaveValue("2027-02-04");
    await expect(picker).toBeVisible();
    await expect(picker.locator(".air-datepicker-buttons")).toHaveCount(0);

    const timeField = design.getByRole("textbox", { name: "计划时间", exact: true });
    const timeInputId = await timeField.getAttribute("id");
    const timePickerElement = design.locator(`.air-datepicker.juju-air-picker-${timeInputId!.replace(/[^a-zA-Z0-9_-]/g, "")}`);
    const timeFieldBox = await timeField.boundingBox();
    expect(timeFieldBox).not.toBeNull();
    await timeField.fill("09:45");
    await timeField.press("Tab");
    await expect(timeField).toHaveValue("09:45");
    await timeField.click();
    picker = timePickerElement;
    await expect(picker).toBeVisible();
    await expect(picker.locator(".air-datepicker-time--row")).toHaveCount(2);
    await expect(picker.locator(".air-datepicker-body--cells.-days-")).toBeHidden();
    await picker.locator(".air-datepicker-time--row input").last().focus();
    await picker.locator(".air-datepicker-time--row input").last().press("ArrowRight");
    await expect(picker).toBeVisible();
    await design.getByRole("textbox", { name: "备注", exact: true }).click();
    await expect(picker).not.toBeVisible();

    const dateTimeField = design.getByRole("textbox", { name: "日期和时间", exact: true });
    const dateTimeInputId = await dateTimeField.getAttribute("id");
    const dateTimePicker = design.locator(`.air-datepicker.juju-air-picker-${dateTimeInputId!.replace(/[^a-zA-Z0-9_-]/g, "")}`);
    await dateTimeField.fill("2026-11-23 09:45");
    await dateTimeField.press("Tab");
    await expect(dateTimeField).toHaveValue("2026-11-23 09:45");
    await expect(design.getByText(/日期时间（UTC）：2026-11-23T01:45:00Z/)).toBeVisible();
    await dateTimeField.click();
    picker = dateTimePicker;
    await expect(picker).toHaveClass(/-juju-visible-/);
    await picker.locator(".air-datepicker-cell.-day-").filter({ hasText: /^24$/ }).click();
    await expect(picker).toHaveClass(/-juju-visible-/);
    await expect(dateTimeField).toHaveValue("2026-11-24 09:45");
    await expect(design.getByText(/日期时间（UTC）：2026-11-24T01:45:00Z/)).toBeVisible();
    await dateTimeField.press("Escape");
    await design.getByRole("combobox", { name: "预览时区" }).click();
    await design.getByRole("option", { name: "America/New_York" }).click();
    await expect(dateTimeField).toHaveValue("2026-11-23 20:45");
    await expect(design.getByText(/日期时间（UTC）：2026-11-24T01:45:00Z/)).toBeVisible();
    const temporalHeights = await design.locator(".ju-air-picker-input").evaluateAll((controls) =>
        controls.map((control) => Math.round(control.getBoundingClientRect().height)),
    );
    expect(new Set(temporalHeights).size).toBe(1);
    expect(await page.evaluate(() => getComputedStyle(document.documentElement).scrollbarGutter)).toBe("auto");
    await expect(design.getByText("Juju UI Foundation")).toHaveCount(0);

    await design.setViewportSize({ width: 1200, height: 420 });
    await design.evaluate(() => window.scrollTo(0, 0));
    const themePicker = design.getByRole("combobox", { name: "应用主题" });
    const triggerBox = await themePicker.boundingBox();
    const arrowBox = await themePicker.locator("svg").boundingBox();
    expect(triggerBox).not.toBeNull();
    expect(arrowBox).not.toBeNull();
    expect(Math.abs(triggerBox!.y + triggerBox!.height / 2 - (arrowBox!.y + arrowBox!.height / 2))).toBeLessThanOrEqual(1);
    const scrollBeforeOpen = await design.evaluate(() => window.scrollY);
    await themePicker.click();
    expect(await design.evaluate(() => window.scrollY)).toBe(scrollBeforeOpen);
    await expect(design.getByRole("option", { name: "深色" })).toBeVisible();
    expect(await design.evaluate(() => window.scrollY)).toBe(scrollBeforeOpen);
    await design.getByRole("option", { name: "深色" }).click();
    expect(await design.evaluate(() => window.scrollY)).toBe(scrollBeforeOpen);
    await expect(design.locator('[data-token="--primary"] code').last()).toHaveText("#8AA9FF");
    await expect(design.locator('[data-token="--overlay"] code').last()).toHaveText("#0000009E");
    const categoryPicker = design.getByRole("combobox", { name: "工具分类" });
    await categoryPicker.scrollIntoViewIfNeeded();
    const categoryScrollBefore = await design.evaluate(() => window.scrollY);
    await categoryPicker.click();
    await expect(design.getByRole("option", { name: "数据工具" })).toBeVisible();
    expect(await design.evaluate(() => window.scrollY)).toBe(categoryScrollBefore);
    await design.getByRole("option", { name: "数据工具" }).click();
    expect(await design.evaluate(() => window.scrollY)).toBe(categoryScrollBefore);
    await design.close();
});

test("date-time controls use explicit business zone and locale, not the browser zone", async ({ browser }) => {
    const context = await browser.newContext({ timezoneId: "Pacific/Honolulu" });
    try {
        const page = await context.newPage();
        await page.goto("/");
        const [design] = await Promise.all([
            page.waitForEvent("popup"),
            page.getByRole("button", { name: /设计系统工作台/ }).click(),
        ]);
        const dateField = design.getByRole("textbox", { name: "计划日期", exact: true });
        const timeField = design.getByRole("textbox", { name: "计划时间", exact: true });
        const dateTimeField = design.getByRole("textbox", { name: "日期和时间", exact: true });
        const environmentTime = design.getByRole("textbox", { name: "预览时间", exact: true });
        const environmentNow = design.locator(".token-environment-settings .sr-only").filter({ hasText: "传入组件的 now" });
        await expect(environmentTime).toHaveValue(/^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$/);
        await expect(environmentNow).toContainText(/传入组件的 now（绝对时间，UTC）：\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z/);
        const timeSettings = design.locator(".ju-field").filter({ has: environmentTime });
        await timeSettings.getByRole("button", { name: "查看提示信息" }).click();
        await expect(design.locator(".ju-validation-popover")).toContainText("传入组件的 now（绝对时间，UTC）");
        await design.keyboard.press("Escape");
        await environmentTime.fill("2026-11-23 09:30");
        await expect(environmentNow).toContainText("2026-11-23T01:30:00Z");
        await expect(dateField).toHaveValue("2026-11-21");
        await expect(timeField).toHaveValue("09:30");
        await expect(dateTimeField).toHaveValue("2026-11-21 09:30");

        await design.getByRole("combobox", { name: "预览时区" }).click();
        await design.getByRole("option", { name: "UTC" }).click();
        await expect(environmentNow).toContainText("2026-11-23T01:30:00Z");
        await expect(environmentTime).toHaveValue("2026-11-23 01:30");
        await expect(dateTimeField).toHaveValue("2026-11-21 01:30");
        await expect(design.getByText(/日期时间（UTC）：2026-11-21T01:30:00Z/)).toBeVisible();
        await expect(dateField).toHaveValue("2026-11-21");
        await expect(timeField).toHaveValue("09:30");

        await design.getByRole("combobox", { name: "语言地区" }).click();
        await design.getByRole("option", { name: "英语（美国） · en-US" }).click();
        await expect(environmentTime).toHaveValue("11/23/2026 01:30 AM");
        await expect(dateTimeField).toHaveValue("11/21/2026 01:30 AM");
        await expect(dateField).toHaveValue("11/21/2026");
        await expect(timeField).toHaveValue("09:30 AM");
        await design.getByRole("button", { name: "使用当前时间" }).click();
        await expect(environmentTime).toHaveValue(/^\d{2}\/\d{2}\/\d{4} \d{2}:\d{2} (AM|PM)$/);
        await expect(environmentNow).not.toContainText("2026-11-23T01:30:00Z");
        await design.close();
    } finally {
        await context.close();
    }
});

test("external datetime example displays UTC without fractional seconds", async ({ page }) => {
    await page.goto("/");
    const [design] = await Promise.all([
        page.waitForEvent("popup"),
        page.getByRole("button", { name: /设计系统工作台/ }).click(),
    ]);
    const environment = await design.locator(".token-environment-settings .sr-only").filter({ hasText: "传入组件的 now" }).textContent();
    const instant = environment?.match(/(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.\d+)?Z/)?.[1];
    expect(instant).toBeTruthy();
    await design.getByRole("button", { name: "将日期时间设为预览环境时间" }).click();
    await expect(design.getByLabel("日期时间示例的内部值")).toContainText(`日期时间（UTC）：${instant}Z`);
    await design.close();
});

test("launcher settings persist as plain application preferences", async ({
    page,
}) => {
    await page.goto("/");
    await page.getByRole("button", { name: "设置" }).click();
    await page.getByRole("switch", { name: "启动时仅显示托盘图标" }).click();
    await expect(page.getByText("已保存", { exact: true })).toHaveCount(0);
    await page.reload();
    await page.getByRole("button", { name: "设置" }).click();
    await expect(
        page.getByRole("switch", { name: "启动时仅显示托盘图标" }),
    ).toHaveAttribute("aria-checked", "true");
    await page.getByRole("switch", { name: "启动时仅显示托盘图标" }).click();
    await page.getByRole("button", { name: "关闭设置" }).click();
    await expect(page.getByText("juju", { exact: true })).toBeVisible();
});

test("settings use labeled horizontal paths and working JSON window controls", async ({ page }) => {
    await page.goto("/?window=settings");
    await page.getByRole("button", { name: "快捷键" }).click();
    await expect(page.getByRole("heading", { name: "全局快捷键" })).toBeVisible();
    await expect(page.getByText("注册状态")).toHaveCount(0);

    await page.getByRole("button", { name: "存储设置" }).click();
    const rootRow = page.locator(".setting-row--path");
    await expect(rootRow.getByText("根目录", { exact: true })).toBeVisible();
    await expect(rootRow.locator("svg")).toHaveCount(2);
    const rootLabel = await rootRow.locator(".path-label").boundingBox();
    const rootValue = await rootRow.locator(".path-value").boundingBox();
    const rootName = await rootRow.locator(".path-name").boundingBox();
    const rootRowBox = await rootRow.boundingBox();
    expect(rootLabel).not.toBeNull();
    expect(rootValue).not.toBeNull();
    expect(rootName).not.toBeNull();
    expect(rootRowBox).not.toBeNull();
    expect(rootName!.x).toBeCloseTo(rootLabel!.x, 0);
    expect(rootName!.x).toBeCloseTo(rootRowBox!.x + 16, 0);
    expect(Math.abs(rootLabel!.y + rootLabel!.height / 2 - (rootValue!.y + rootValue!.height / 2))).toBeLessThanOrEqual(1);

    await page.getByRole("button", { name: "JSON 格式化" }).click();
    await expect(page.getByRole("heading", { name: "窗口与进程" })).toBeVisible();
    for (const label of ["关闭窗口后保留会话", "默认最大化打开", "记住窗口大小与位置"]) {
        await expect(page.getByRole("switch", { name: label })).toBeVisible();
        await expect(page.locator(".settings-content-main .setting-row--toggle").filter({ has: page.getByRole("switch", { name: label }) })).toContainText(label);
    }
    const jsonStorage = page.locator(".settings-section").filter({ has: page.getByRole("heading", { name: "存储目录" }) });
    await expect(jsonStorage.locator(".path-label svg")).toHaveCount(0);
    await expect(jsonStorage.getByRole("button", { name: "复制路径" })).toBeVisible();
    await expect(jsonStorage.getByRole("button", { name: "修改路径" })).toHaveCount(0);
    await page.getByRole("switch", { name: "默认最大化打开" }).click();
    await expect(page.getByText("已保存", { exact: true })).toHaveCount(0);

    await page.getByRole("button", { name: "Toto 日程提醒" }).click();
    await expect(page.getByRole("heading", { name: "节假日" })).toHaveCount(0);
    await expect(page.getByRole("switch", { name: "仅工作日提醒" })).toBeVisible();
    const reviewStart = page.getByRole("textbox", { name: "开始盘点", exact: true });
    await reviewStart.click();
    const timePicker = page.locator(".air-datepicker.juju-air-date-picker.-juju-visible-");
    await expect(timePicker.locator(".air-datepicker-time")).toBeVisible();
    await reviewStart.fill("09:45");
    await reviewStart.press("Tab");
    await expect(reviewStart).toHaveValue("09:45");
    await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem("juju-toto-settings") ?? "{}").daily_review_start_time)).toBe("09:45");
    await reviewStart.fill("");
    await reviewStart.press("Tab");
    await expect(reviewStart).toHaveValue("");
    await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem("juju-toto-settings") ?? "{}").daily_review_start_time)).toBe("");
    const totoStorage = page.locator(".settings-section").filter({ has: page.getByRole("heading", { name: "存储目录" }) });
    await expect(totoStorage.locator(".path-label svg")).toHaveCount(0);
    await expect(totoStorage.getByRole("button", { name: "修改路径" })).toHaveCount(0);
    const totoPathRow = totoStorage.locator(".setting-row--path");
    const totoName = await totoPathRow.locator(".path-name").boundingBox();
    const totoLabel = await totoPathRow.locator(".path-label").boundingBox();
    const totoRowBox = await totoPathRow.boundingBox();
    expect(totoName).not.toBeNull();
    expect(totoLabel).not.toBeNull();
    expect(totoRowBox).not.toBeNull();
    expect(totoName!.x).toBeCloseTo(totoLabel!.x, 0);
    expect(totoName!.x).toBeCloseTo(totoRowBox!.x + 16, 0);

    for (const tool of ["EchoHttp", "Kairos"]) {
        await page.getByRole("button", { name: tool }).click();
        await expect(page.getByRole("heading", { name: "存储目录" })).toHaveCount(0);
    }
});

test("duplicate JSON shortcuts are reported inside the shortcut dialog", async ({ page }) => {
    await page.goto("/?window=settings");
    await page.getByRole("button", { name: "JSON 格式化" }).click();
    await page.getByRole("button", { name: "修改格式化" }).click();
    const shortcutInput = page.getByRole("textbox", { name: "新的快捷键" });
    await shortcutInput.press("Alt+s");
    await expect(shortcutInput).toHaveAttribute("aria-invalid", "true");
    await expect(page.getByText("快捷键已存在，请更换其他组合。")).toBeVisible();
    await page.getByRole("button", { name: "保存", exact: true }).click();
    await expect(page.getByRole("dialog", { name: "设置快捷键" })).toBeVisible();
    await expect(page.locator(".settings-content-main > .settings-error")).toHaveCount(0);
});

test("launcher matches the tool-list entries and single-key hints", async ({
    page,
}) => {
    await page.goto("/");
    await expect(page.getByText("juju", { exact: true })).toBeVisible();
    await expect(
        page.getByRole("button", { name: /JSON 格式化/ }),
    ).toContainText("J");
    await expect(
        page.getByRole("button", { name: /Toto 日程提醒/ }),
    ).toContainText("T");
    await expect(page.getByRole("button", { name: /EchoHttp/ })).toContainText(
        "E",
    );
    await expect(page.getByRole("button", { name: /Kairos/ })).toContainText(
        "K",
    );
    await expect(
        page.getByRole("button", { name: /MySQL Schema/ }),
    ).toContainText("M");
    await expect(
        page.getByRole("button", { name: /设计系统工作台/ }),
    ).toContainText("D");
});

test("settings stay inside their window and navigation keeps a fixed layout", async ({
    page,
}) => {
    await page.goto("/?window=settings");
    await expect(page.getByRole("heading", { name: "启动与关闭" })).toBeVisible();
    const outerHeight = await page.evaluate(
        () => document.documentElement.scrollHeight - window.innerHeight,
    );
    expect(outerHeight).toBeLessThanOrEqual(1);

    const general = page.getByRole("button", { name: "常用设置" });
    const before = await general.boundingBox();
    await page.getByRole("button", { name: "JSON 格式化" }).click();
    await expect(page.getByRole("heading", { name: "工具入口" })).toBeVisible();
    const after = await general.boundingBox();
    expect(after).not.toBeNull();
    expect(after?.x).toBe(before?.x);
    expect(after?.y).toBe(before?.y);
    expect(after?.width).toBe(before?.width);
    expect(after?.height).toBe(before?.height);
    const generalIcon = await general.locator("svg").boundingBox();
    const jsonNavigation = page.getByRole("button", { name: "JSON 格式化" });
    const jsonIcon = await jsonNavigation.locator("svg").boundingBox();
    expect(jsonIcon?.x).toBe(generalIcon?.x);
    expect(jsonIcon?.width).toBe(17);
    expect(jsonIcon?.height).toBe(17);
    await expect(page.locator(".settings-sidebar .nav-dot")).toHaveCount(0);
});

test("settings controls of the same type share row height and vertical alignment", async ({ page }) => {
    await page.setViewportSize({ width: 860, height: 590 });
    await page.goto("/?window=settings");
    const rows = page.locator(".settings-content-main .settings-section:first-child .setting-row");
    await expect(rows).toHaveCount(4);
    const boxes = await rows.evaluateAll((elements) => elements.map((element) => element.getBoundingClientRect().height));
    expect(new Set(boxes).size).toBe(1);
    expect(boxes[0]).toBe(65);
    const firstGroup = await page.locator(".settings-content-main .settings-section:first-child .settings-group").boundingBox();
    expect(firstGroup?.x).toBe(255);
    expect(firstGroup?.y).toBeCloseTo(112, 0);
    const allRows = page.locator(".settings-content-main .setting-row");
    await expect(allRows).toHaveCount(6);
    const allHeights = await allRows.evaluateAll((elements) => elements.map((element) => element.getBoundingClientRect().height));
    expect(new Set(allHeights)).toEqual(new Set([65]));
    const secondGroup = await page.locator(".settings-content-main .settings-section:nth-child(2) .settings-group").boundingBox();
    expect(secondGroup?.y).toBeCloseTo(426, 0);
    const firstRow = rows.first();
    const rowBox = await firstRow.boundingBox();
    const switchBox = await firstRow.getByRole("switch").boundingBox();
    expect(rowBox).not.toBeNull();
    expect(switchBox).not.toBeNull();
    expect(Math.abs((rowBox!.y + rowBox!.height / 2) - (switchBox!.y + switchBox!.height / 2))).toBeLessThanOrEqual(1);
    const timeoutRow = allRows.last();
    const timeoutBox = await timeoutRow.boundingBox();
    const timeoutInput = await timeoutRow.getByRole("spinbutton").boundingBox();
    expect(Math.abs((timeoutBox!.y + timeoutBox!.height / 2) - (timeoutInput!.y + timeoutInput!.height / 2))).toBeLessThanOrEqual(1);
});

test("Toto table layout and dialog Escape behavior", async ({ page }) => {
    await page.goto("/");
    const [toto] = await Promise.all([page.waitForEvent("popup"), page.getByRole("button", { name: /Toto/ }).click()]);
    await toto.getByRole("button", { name: /新增/ }).click();
    await expect(toto.getByRole("heading", { name: "新增事项" })).toBeVisible();
    await toto.keyboard.press("Escape");
    await expect(toto.getByRole("heading", { name: "新增事项" })).toBeHidden();
    expect(toto.isClosed()).toBe(false);
    await toto.getByRole("button", { name: /新增/ }).click();
    await toto.getByRole("textbox", { name: "事项内容 @计划时间[@提前提醒分钟]" }).fill("测试事项内容用于检查表格列宽与文本溢出显示效果");
    await toto.getByRole("button", { name: "新增", exact: true }).last().click();
    const row = toto.getByRole("row", { name: /测试事项内容用于检查/ });
    await expect(row).toBeVisible();
    await expect(row.locator(".ju-overflow-text")).toHaveCount(2);
    const table = toto.locator(".toto-tool-window .ju-table-container");
    const dimensions = await table.evaluate((element) => ({ scroll: element.scrollWidth, client: element.clientWidth }));
    expect(dimensions.scroll).toBeLessThanOrEqual(dimensions.client);
    const heights = await toto.locator(".toto-tool-actions button").evaluateAll((buttons) => buttons.map((button) => button.getBoundingClientRect().height));
    expect(new Set(heights).size).toBe(1);
    await row.click();
    await toto.keyboard.press("Alt+E");
    await expect(toto.getByRole("heading", { name: "编辑事项" })).toBeVisible();
    await toto.keyboard.press("Escape");
    await expect(toto.getByRole("heading", { name: "编辑事项" })).toBeHidden();
    expect(toto.isClosed()).toBe(false);
    for (const [shortcut, title] of [["Alt+D", "事项详情"], ["Alt+F", "完成事项"], ["Alt+C", "取消事项"]] as const) {
        await toto.keyboard.press(shortcut);
        await expect(toto.getByRole("heading", { name: title })).toBeVisible();
        await toto.keyboard.press("Escape");
        await expect(toto.getByRole("heading", { name: title })).toBeHidden();
        expect(toto.isClosed()).toBe(false);
    }
    await toto.close();
});

test("Toto follows the quick-add, edit, finish and separate history workflows", async ({ page }) => {
    await page.goto("/");
    const [toto] = await Promise.all([
        page.waitForEvent("popup"),
        page.getByRole("button", { name: /Toto/ }).click(),
    ]);
    await expect(toto.getByText("Toto 日程提醒", { exact: true })).toBeVisible();
    const grid = toto.locator(".toto-main-grid");
    await grid.focus();
    await expect(grid).toHaveCSS("outline-style", "none");
    const tableRegion = toto.getByRole("region", { name: "Toto 进行中事项" });
    await tableRegion.focus();
    await expect(tableRegion).toHaveCSS("outline-style", "none");
    await expect(toto.locator(".toto-tool-brand svg")).toBeVisible();
    await expect(toto.locator(".toto-selected-actions")).toHaveCount(0);
    await toto.getByRole("button", { name: /新增/ }).click();
    await toto.getByRole("heading", { name: "新增事项" }).waitFor();
    const quickInput = toto.getByRole("textbox", { name: "事项内容 @计划时间[@提前提醒分钟]" });
    await quickInput.fill("阶段三验收事项@202612011600");
    await expect(toto.locator(".toto-quick-preview")).toContainText("阶段三验收事项");
    await quickInput.press("Enter");

    await expect(
        toto.getByRole("row", { name: /阶段三验收事项/ }),
    ).toBeVisible();
    await expect(toto.locator(".toto-tool-footer [role=status]")).toHaveText("事项已新增。");
    await toto.getByRole("row", { name: /阶段三验收事项/ }).click();
    await toto.keyboard.press("Alt+E");
    await toto
        .getByRole("textbox", { name: "事项内容" })
        .fill("阶段三验收事项已编辑");
    await toto.getByRole("button", { name: "保存", exact: true }).last().click();
    await toto.keyboard.press("Alt+F");
    await toto.getByRole("textbox", { name: "完成备注" }).fill("验收完成");
    await toto.getByRole("button", { name: "确认", exact: true }).click();
    await expect(toto.locator(".toto-tool-footer [role=status]")).toHaveText("事项已完成。");
    await toto.getByRole("button", { name: /新增/ }).click();
    const cancelInput = toto.getByRole("textbox", { name: "事项内容 @计划时间[@提前提醒分钟]" });
    await cancelInput.fill("阶段三取消验收事项");
    await cancelInput.press("Enter");
    await toto.getByRole("row", { name: /阶段三取消验收事项/ }).click();
    await toto.keyboard.press("Alt+C");
    await toto.getByRole("textbox", { name: "取消备注" }).fill("已取消验收");
    await toto.getByRole("button", { name: "确认", exact: true }).click();
    await expect(toto.locator(".toto-tool-footer [role=status]")).toHaveText("事项已取消。");
    const [history] = await Promise.all([
        toto.waitForEvent("popup"),
        toto.getByRole("button", { name: "历史" }).click(),
    ]);
    await expect(
        history.getByRole("row", { name: /阶段三验收事项已编辑.*已完成/ }),
    ).toBeVisible();
    await expect(history.getByRole("row", { name: /阶段三取消验收事项.*已取消/ })).toBeVisible();
    await expect(history.locator(".toto-tool-brand svg")).toBeVisible();
    const filterControls = await history.locator(".toto-history-filterbar").evaluate((element) =>
        Array.from(element.querySelectorAll(".ju-select-trigger, .ju-air-picker-input, .toto-history-filterbar > .ju-button"))
            .map((control) => {
                const rect = control.getBoundingClientRect();
                return { top: rect.top, bottom: rect.bottom };
            }),
    );
    expect(filterControls.length).toBeGreaterThanOrEqual(4);
    const controlCenters = filterControls.map((rect) => (rect.top + rect.bottom) / 2);
    expect(Math.max(...controlCenters) - Math.min(...controlCenters)).toBeLessThanOrEqual(1);
    const paginationButtons = await history.locator(".toto-history-pagination .ju-button").evaluateAll((buttons) =>
        buttons.map((button) => button.getBoundingClientRect().width),
    );
    expect(paginationButtons.every((width) => width >= 58)).toBe(true);
    await history.getByRole("textbox", { name: "搜索事项内容或备注" }).fill("验收");
    await history.getByRole("button", { name: "查询" }).click();
    await expect(history.getByRole("row", { name: /验收完成/ })).toBeVisible();
    await history.close();
    await toto.close();
    await expect(page.getByText("juju", { exact: true })).toBeVisible();
    const [json] = await Promise.all([
        page.waitForEvent("popup"),
        page.getByRole("button", { name: /JSON 格式化/ }).click(),
    ]);
    await expect(json.getByText("JSON 格式化", { exact: true })).toBeVisible();
    await json.close();
    await expect(page.getByText("juju", { exact: true })).toBeVisible();
});

test("JSON tool edits, formats, renames, and deletes persisted documents", async ({ page }) => {
    page.on("dialog", (dialog) => dialog.accept());
    await page.goto("/?tool=json");
    const json = page;
    await expect(json.getByText("JSON 格式化", { exact: true })).toBeVisible();
    const editor = json.locator(".monaco-editor");
    await expect(editor).toBeVisible();
    await editor.click({ position: { x: 70, y: 30 } });
    await json.keyboard.type('{"hello":"world"}');
    await json.getByRole("button", { name: /格式化/ }).click();
    await json.getByRole("button", { name: /保存/ }).click();
    await expect(json.getByText("已保存", { exact: true })).toBeVisible();
    await json.getByRole("button", { name: /新建/ }).click();
    await expect(json.locator(".json-document-row")).toHaveCount(2);
    await json.getByRole("button", { name: "重命名文档" }).click();
    const rename = json.getByRole("textbox", { name: "重命名文档" });
    await rename.fill("renamed-document");
    await rename.press("Enter");
    await expect(json.getByText("renamed-document", { exact: true })).toBeVisible();
    await json.getByRole("button", { name: "删除文档" }).click();
    await expect(json.locator(".json-document-row")).toHaveCount(1);
    await json.close();
});
