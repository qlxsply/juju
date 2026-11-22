import { expect, test } from "@playwright/test";

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
    await expect(showcase.locator(".token-card")).toHaveCount(6);
    await showcase.close();
    await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
});

test("design workbench showcases public components and keeps theme controls stable", async ({ page }) => {
    await page.goto("/");
    const [design] = await Promise.all([
        page.waitForEvent("popup"),
        page.getByRole("button", { name: /设计系统工作台/ }).click(),
    ]);
    await expect(design.locator(".showcase-header")).toHaveCount(0);
    await expect(design.locator(".showcase-sidebar")).toHaveCount(0);
    await expect(design.locator(".showcase-section")).toHaveCount(5);
    for (const heading of ["Design Tokens", "按钮与状态", "表单组件", "数据组件", "弹窗与反馈"]) {
        await expect(design.getByRole("heading", { name: heading })).toBeVisible();
    }
    await expect(design.getByRole("button", { name: "返回 Juju" })).toHaveCount(0);
    await expect(design.locator(".token-theme-control")).toBeVisible();
    const infoHint = design.getByText("名称仅用于本地显示");
    await expect(infoHint).toBeHidden();
    await design.getByRole("textbox", { name: "名称", exact: true }).focus();
    await expect(infoHint).toBeVisible();
    await expect(design.getByText("接近长度上限")).toBeVisible();
    await expect(design.getByText("请检查该字段")).toBeVisible();
    for (const label of ["计划日期", "计划时间", "日期和时间"]) {
        const group = design.getByRole("group", { name: label, exact: true });
        const input = group.locator(".ju-temporal-segment").first();
        await expect(input).not.toHaveValue("");
        await input.press("Backspace");
        for (const segment of await group.locator(".ju-temporal-segment").all()) await expect(segment).toHaveValue("");
    }
    const date = design.getByRole("group", { name: "计划日期", exact: true });
    await date.getByLabel("计划日期 年").fill("2026");
    await expect(date.getByLabel("计划日期 月")).toBeFocused();
    await design.keyboard.type("24");
    await expect(date.getByLabel("计划日期 月")).toHaveValue("02");
    await expect(date.getByLabel("计划日期 日")).toHaveValue("04");
    expect(await page.evaluate(() => getComputedStyle(document.documentElement).scrollbarGutter)).toBe("auto");
    await expect(design.getByText("Juju UI Foundation")).toHaveCount(0);

    await design.setViewportSize({ width: 1200, height: 420 });
    await design.evaluate(() => window.scrollTo(0, 0));
    const themePicker = design.getByRole("combobox", { name: "主题模式" });
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
    const reviewStart = page.getByRole("group", { name: "开始盘点" });
    await reviewStart.getByLabel("开始盘点 时").fill("09");
    await reviewStart.getByLabel("开始盘点 分").fill("30");
    await reviewStart.getByLabel("开始盘点 分").press("Tab");
    await reviewStart.getByLabel("开始盘点 时").press("Backspace");
    await expect(reviewStart.getByLabel("开始盘点 时")).toHaveValue("");
    await expect(reviewStart.getByLabel("开始盘点 分")).toHaveValue("");
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

test("Toto follows the quick-add, edit, finish and separate history workflows", async ({ page }) => {
    await page.goto("/");
    const [toto] = await Promise.all([
        page.waitForEvent("popup"),
        page.getByRole("button", { name: /Toto/ }).click(),
    ]);
    await expect(toto.getByText("Toto 日程提醒", { exact: true })).toBeVisible();
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
        Array.from(element.querySelectorAll(".ju-select-trigger, .ju-temporal-control, .toto-history-filterbar > .ju-button"))
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
