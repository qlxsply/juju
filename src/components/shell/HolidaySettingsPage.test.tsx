import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { HolidaySettingsPage } from "./HolidaySettingsPage";
import { fetchHolidayAnnouncement, getHolidayRange, setHolidayOverrides, type HolidayOverride } from "../../services/holidays";

vi.mock("../../services/holidays", () => ({
    getHolidayRange: vi.fn(),
    setHolidayOverrides: vi.fn(),
    fetchHolidayAnnouncement: vi.fn(),
}));

const pad = (value: number) => String(value).padStart(2, "0");
const dateString = (date: Date) => `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
let overrides: Record<string, HolidayOverride>;

beforeEach(() => {
    vi.resetAllMocks();
    overrides = {};
    vi.mocked(getHolidayRange).mockImplementation(async (start, end) => {
        const [year, month, day] = start.split("-").map(Number);
        const count = Number(end.slice(8));
        const days = Array.from({ length: count - day + 1 }, (_, i) => {
            const date = new Date(year, month - 1, day + i);
            const key = dateString(date);
            const weekend = date.getDay() === 0 || date.getDay() === 6;
            const manual = overrides[key];
            return {
                date: key, isOffDay: manual?.isOffDay ?? weekend, isWeekend: weekend,
                source: manual ? "manual" as const : "default" as const,
                label: manual?.remark ?? (weekend ? "休" : ""), isManual: !!manual,
                officialYear: null, officialIsOffDay: null, announcementYears: [],
            };
        });
        return { days, overrides: { ...overrides }, hasAnnouncement: true, fetching: false, manualError: null };
    });
    vi.mocked(setHolidayOverrides).mockImplementation(async (dates, value) => {
        for (const date of dates) {
            if (value) overrides[date] = value;
            else delete overrides[date];
        }
    });
    vi.mocked(fetchHolidayAnnouncement).mockResolvedValue();
});

describe("HolidaySettingsPage", () => {
    it("fills today's saved adjustment and restores it", async () => {
        const user = userEvent.setup();
        const today = dateString(new Date());
        overrides[today] = { isOffDay: false, remark: "项目上线" };
        render(<HolidaySettingsPage onMessage={vi.fn()} />);
        const remark = await screen.findByRole("textbox", { name: "备注（选填）" });
        await waitFor(() => expect(remark).toHaveValue("项目上线"));
        expect(screen.getByRole("combobox", { name: "调整类型" })).toHaveTextContent("设为工作日");
        await user.click(screen.getByRole("combobox", { name: "调整类型" }));
        await user.click(screen.getByRole("option", { name: "恢复自动判定" }));
        expect(remark).toBeDisabled();
        await user.click(screen.getByRole("button", { name: "应用" }));
        await waitFor(() => expect(setHolidayOverrides).toHaveBeenCalledWith([today], null));
    });

    it("keeps batch selection and draft across months and retains them on failure", async () => {
        const user = userEvent.setup();
        const now = new Date();
        const first = dateString(new Date(now.getFullYear(), now.getMonth(), 1));
        const second = dateString(new Date(now.getFullYear(), now.getMonth() + 1, 1));
        const message = vi.fn();
        vi.mocked(setHolidayOverrides).mockRejectedValueOnce(new Error("disk"));
        render(<HolidaySettingsPage onMessage={message} />);
        await screen.findByRole("button", { name: new RegExp(`^${first}`) });
        await user.click(screen.getByRole("button", { name: "批量调整" }));
        expect(screen.getByRole("button", { name: "批量应用 (0)" })).toBeDisabled();
        await user.click(screen.getByRole("button", { name: new RegExp(`^${first}`) }));
        await user.type(screen.getByRole("textbox", { name: "备注（选填）" }), "调休");
        await user.click(screen.getByRole("button", { name: "下个月" }));
        await user.click(await screen.findByRole("button", { name: new RegExp(`^${second}`) }));
        expect(screen.getByRole("textbox", { name: "备注（选填）" })).toHaveValue("调休");
        await user.click(screen.getByRole("button", { name: "批量应用 (2)" }));
        await waitFor(() => expect(message).toHaveBeenCalled());
        expect(screen.getByRole("button", { name: "批量应用 (2)" })).toBeEnabled();
        expect(screen.getByRole("textbox", { name: "备注（选填）" })).toHaveValue("调休");
        await user.click(screen.getByRole("button", { name: "批量应用 (2)" }));
        await waitFor(() => expect(screen.getByRole("button", { name: "批量调整" })).toBeInTheDocument());
        expect(setHolidayOverrides).toHaveBeenLastCalledWith([first, second].sort(), { isOffDay: true, remark: "调休" });
    });

    it("supports cross-year selection, removing tags, clearing and cancelling without writes", async () => {
        const user = userEvent.setup();
        const year = new Date().getFullYear();
        const first = `${year}-12-31`;
        const second = `${year + 1}-01-01`;
        render(<HolidaySettingsPage onMessage={vi.fn()} />);
        await screen.findByRole("button", { name: "批量调整" });
        await user.click(screen.getByRole("combobox", { name: "月份" }));
        await user.click(screen.getByRole("option", { name: "12 月" }));
        await user.click(await screen.findByRole("button", { name: new RegExp(`^${first}`) }));
        await user.click(screen.getByRole("button", { name: "批量调整" }));
        await user.click(screen.getByRole("button", { name: new RegExp(`^${first}`) }));
        await user.click(screen.getByRole("button", { name: "下个月" }));
        await user.click(await screen.findByRole("button", { name: new RegExp(`^${second}`) }));
        expect(screen.getByRole("button", { name: "批量应用 (2)" })).toBeEnabled();
        await user.click(screen.getByRole("button", { name: `${first} ×` }));
        expect(screen.getByRole("button", { name: "批量应用 (1)" })).toBeEnabled();
        await user.click(screen.getByRole("button", { name: "清空选择" }));
        expect(screen.getByRole("button", { name: "批量应用 (0)" })).toBeDisabled();
        await user.click(screen.getByRole("button", { name: "取消批量调整" }));
        expect(setHolidayOverrides).not.toHaveBeenCalled();
    });

    it("locates and restores a record without mixing it into batch selection", async () => {
        const user = userEvent.setup();
        const year = new Date().getFullYear();
        const date = `${year - 1}-05-01`;
        overrides[date] = { isOffDay: true, remark: "个人调休" };
        render(<HolidaySettingsPage onMessage={vi.fn()} />);
        await screen.findByRole("button", { name: "手动设置记录 (1)" });
        await user.click(screen.getByRole("button", { name: "批量调整" }));
        await user.click(screen.getByRole("button", { name: "手动设置记录 (1)" }));
        await user.click(screen.getByRole("button", { name: "定位" }));
        await screen.findByRole("button", { name: new RegExp(`^${date}`) });
        expect(screen.getByRole("button", { name: "批量应用 (0)" })).toBeDisabled();
        await user.click(screen.getByRole("button", { name: "手动设置记录 (1)" }));
        await user.click(screen.getByRole("button", { name: "恢复" }));
        await waitFor(() => expect(setHolidayOverrides).toHaveBeenCalledWith([date], null));
        await screen.findByText("暂无手动设置记录");
    });

    it("keeps the requested year when switching views during a manual download", async () => {
        const user = userEvent.setup();
        const year = new Date().getFullYear();
        const implementation = vi.mocked(getHolidayRange).getMockImplementation()!;
        vi.mocked(getHolidayRange).mockImplementation(async (...args) => ({
            ...await implementation(...args), hasAnnouncement: false,
        }));
        let finish!: () => void;
        vi.mocked(fetchHolidayAnnouncement).mockImplementation(() => new Promise<void>((resolve) => { finish = resolve; }));
        render(<HolidaySettingsPage onMessage={vi.fn()} />);
        await screen.findByRole("button", { name: "更新" });
        await user.click(screen.getByRole("button", { name: "更新" }));
        expect(fetchHolidayAnnouncement).toHaveBeenCalledWith(year);
        await user.click(screen.getByRole("combobox", { name: "年份" }));
        await user.click(screen.getByRole("option", { name: `${year - 1} 年` }));
        finish();
        await waitFor(() => expect(fetchHolidayAnnouncement).toHaveBeenCalledTimes(1));
        expect(screen.getByRole("combobox", { name: "年份" })).toHaveTextContent(`${year - 1} 年`);
    });

    it("sets a single day with an empty remark and then batch restores it", async () => {
        const user = userEvent.setup();
        const today = dateString(new Date());
        render(<HolidaySettingsPage onMessage={vi.fn()} />);
        await screen.findByRole("button", { name: new RegExp(`^${today}`) });
        await user.click(screen.getByRole("combobox", { name: "调整类型" }));
        await user.click(screen.getByRole("option", { name: "设为休息日" }));
        await user.click(screen.getByRole("button", { name: "应用" }));
        await waitFor(() => expect(setHolidayOverrides).toHaveBeenCalledWith([today], { isOffDay: true, remark: "" }));
        await screen.findByRole("button", { name: "手动设置记录 (1)" });
        await user.click(screen.getByRole("button", { name: "批量调整" }));
        await user.click(screen.getByRole("button", { name: new RegExp(`^${today}`) }));
        await user.click(screen.getByRole("combobox", { name: "调整类型" }));
        await user.click(screen.getByRole("option", { name: "恢复自动判定" }));
        expect(screen.getByRole("textbox", { name: "备注（选填）" })).toBeDisabled();
        await user.click(screen.getByRole("button", { name: "批量应用 (1)" }));
        await waitFor(() => expect(setHolidayOverrides).toHaveBeenLastCalledWith([today], null));
    });

    it("disables editing and reports an unreadable manual file", async () => {
        const implementation = vi.mocked(getHolidayRange).getMockImplementation()!;
        vi.mocked(getHolidayRange).mockImplementation(async (...args) => ({
            ...await implementation(...args), manualError: "无法读取手动设置",
        }));
        render(<HolidaySettingsPage onMessage={vi.fn()} />);
        expect(await screen.findByRole("alert")).toHaveTextContent("无法读取手动设置");
        expect(screen.getByRole("button", { name: "应用" })).toBeDisabled();
    });
});
