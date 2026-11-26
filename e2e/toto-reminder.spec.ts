import { expect, test } from "@playwright/test";

test("Toto reminder matches detail layout without horizontal overflow", async ({ page }) => {
    await page.setViewportSize({ width: 520, height: 320 });
    await page.addInitScript(() => {
        localStorage.setItem("juju-toto-items", JSON.stringify([{
            id: "reminder-layout-test",
            content: "事项内容".repeat(80) + "unbroken-content-".repeat(40),
            planned_at: "2026-11-26 15:47:00",
            reminder_at: "2026-11-26 15:42:00",
            created_at: "2026-11-26 09:00:00",
            created_order: 1,
            reminder_status: "待确认",
            reminded_at: null,
            notes: "长备注".repeat(200) + "unbroken-note-".repeat(40),
        }]));
    });
    await page.goto("/?window=toto-reminder&id=reminder-layout-test");
    const detail = page.locator(".toto-reminder-body .toto-item-detail");
    await expect(detail.locator("dt")).toHaveText(["事项内容", "计划时间", "备注"]);
    await expect(detail.locator("dd").nth(1)).toHaveText("2026-11-26 15:47:00");
    await expect(page.getByRole("button", { name: "稍等" })).toBeVisible();
    await expect(page.getByRole("button", { name: "收到" })).toBeVisible();
    for (const width of [520, 320]) {
        await page.setViewportSize({ width, height: 320 });
        const dimensions = await page.evaluate(() => {
            const body = document.querySelector<HTMLElement>(".toto-reminder-body")!;
            return {
                viewport: document.documentElement.clientWidth,
                documentWidth: document.documentElement.scrollWidth,
                bodyWidth: body.clientWidth,
                bodyScrollWidth: body.scrollWidth,
                bodyHeight: body.clientHeight,
                bodyScrollHeight: body.scrollHeight,
            };
        });
        expect(dimensions.documentWidth).toBe(dimensions.viewport);
        expect(dimensions.bodyScrollWidth).toBe(dimensions.bodyWidth);
        expect(dimensions.bodyScrollHeight).toBeGreaterThan(dimensions.bodyHeight);
        await expect(page.getByRole("button", { name: "收到" })).toBeInViewport();
    }
});
