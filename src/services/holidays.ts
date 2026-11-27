import { invoke } from "@tauri-apps/api/core";

export interface HolidayOverride {
    isOffDay: boolean;
    remark: string;
}

export interface DayInfo {
    date: string;
    isOffDay: boolean;
    source: "default" | "official" | "manual";
    label: string;
    isWeekend: boolean;
    isManual: boolean;
    officialYear: number | null;
    announcementYears: number[];
    officialIsOffDay: boolean | null;
}

export interface HolidaySnapshot {
    days: DayInfo[];
    overrides: Record<string, HolidayOverride>;
    manualError: string | null;
    hasAnnouncement: boolean;
    fetching: boolean;
}

/** Each call consults the backend's current persisted state. Dates are civil YYYY-MM-DD strings. */
export const getHolidayRange = (start: string, end: string, year = Number(start.slice(0, 4))) =>
    invoke<HolidaySnapshot>("holiday_snapshot", { start, end, year });
export async function getHolidayDay(date: string): Promise<DayInfo> {
    const snapshot = await getHolidayRange(date, date);
    if (snapshot.manualError) throw new Error(snapshot.manualError);
    return snapshot.days[0];
}
export async function isWorkday(date: string): Promise<boolean> {
    return !(await getHolidayDay(date)).isOffDay;
}
export async function getHolidayAnnouncementStatus(year: number) {
    const date = `${year}-01-01`;
    const { hasAnnouncement, fetching } = await getHolidayRange(date, date, year);
    return { hasAnnouncement, fetching };
}
export async function getHolidayOverrides() {
    const today = new Date();
    const date = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, "0")}-${String(today.getDate()).padStart(2, "0")}`;
    const { overrides, manualError } = await getHolidayRange(date, date);
    if (manualError) throw new Error(manualError);
    return overrides;
}
export const setHolidayOverrides = (dates: string[], adjustment: HolidayOverride | null) =>
    invoke<void>("holiday_set_overrides", { dates, adjustment });
export const removeHolidayOverrides = (dates: string[]) => setHolidayOverrides(dates, null);
export const fetchHolidayAnnouncement = (year: number) =>
    invoke<void>("holiday_fetch", { year });
