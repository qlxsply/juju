import { useCallback, useEffect, useRef, useState } from "react";
import { JuButton, JuDialog, JuInput, JuSelect } from "../ju";
import { fetchHolidayAnnouncement, getHolidayRange, setHolidayOverrides, type HolidaySnapshot } from "../../services/holidays";
import "./holiday-settings.css";

const pad = (n: number) => String(n).padStart(2, "0");
const civil = (year: number, month: number, day: number) => `${year}-${pad(month)}-${pad(day)}`;
const localToday = () => {
    const date = new Date();
    return civil(date.getFullYear(), date.getMonth() + 1, date.getDate());
};
const options = [
    { value: "auto", label: "恢复自动判定" },
    { value: "off", label: "设为休息日" },
    { value: "work", label: "设为工作日" },
];

export function HolidaySettingsPage({ onMessage }: { onMessage: (message: string) => void }) {
    const [today] = useState(localToday);
    const [year, setYear] = useState(Number(today.slice(0, 4)));
    const [month, setMonth] = useState(Number(today.slice(5, 7)));
    const [selected, setSelected] = useState(today);
    const [batch, setBatch] = useState(false);
    const [chosen, setChosen] = useState<Set<string>>(() => new Set());
    const [type, setType] = useState("auto");
    const [remark, setRemark] = useState("");
    const [recordsOpen, setRecordsOpen] = useState(false);
    const [loaded, setLoaded] = useState<{ year: number; month: number; data: HolidaySnapshot } | null>(null);
    const [revision, setRevision] = useState(0);
    const [fetchingYears, setFetchingYears] = useState<Set<number>>(() => new Set());
    const [saving, setSaving] = useState(false);
    const initialFill = useRef(true);
    const count = new Date(year, month, 0).getDate();
    const firstDay = (new Date(year, month - 1, 1).getDay() + 6) % 7;
    const refresh = useCallback(() => setRevision((n) => n + 1), []);
    const snapshot = loaded?.year === year && loaded.month === month ? loaded.data : null;

    useEffect(() => {
        const timer = window.setInterval(refresh, 10_000);
        return () => window.clearInterval(timer);
    }, [refresh]);

    useEffect(() => {
        let active = true;
        void getHolidayRange(civil(year, month, 1), civil(year, month, count), year)
            .then((data) => {
                if (!active) return;
                setLoaded({ year, month, data });
                if (initialFill.current) {
                    initialFill.current = false;
                    const value = data.overrides[today];
                    setType(value ? (value.isOffDay ? "off" : "work") : "auto");
                    setRemark(value?.remark ?? "");
                }
            })
            .catch((error: unknown) => { if (active) onMessage(`读取节假日失败：${String(error)}`); });
        return () => { active = false; };
    }, [year, month, count, revision, onMessage, today]);

    const fill = (date: string, data = snapshot ?? loaded?.data) => {
        setSelected(date);
        const value = data?.overrides[date];
        setType(value ? (value.isOffDay ? "off" : "work") : "auto");
        setRemark(value?.remark ?? "");
    };
    const move = (delta: number) => {
        const date = new Date(year, month - 1 + delta, 1);
        setYear(date.getFullYear());
        setMonth(date.getMonth() + 1);
    };
    const apply = async (dates: string[], value: string, text: string) => {
        setSaving(true);
        try {
            await setHolidayOverrides(dates, value === "auto" ? null : { isOffDay: value === "off", remark: text });
            refresh();
            return true;
        } catch (error) {
            onMessage(`保存节假日设置失败：${String(error)}`);
            return false;
        } finally { setSaving(false); }
    };
    const update = async () => {
        const target = year;
        setFetchingYears((years) => new Set(years).add(target));
        try { await fetchHolidayAnnouncement(target); refresh(); }
        catch (error) { onMessage(`获取 ${target} 年公告失败：${String(error)}`); }
        finally { setFetchingYears((years) => { const next = new Set(years); next.delete(target); return next; }); }
    };
    const years = Array.from({ length: 201 }, (_, i) => Number(today.slice(0, 4)) - 100 + i);
    const records = Object.entries((snapshot ?? loaded?.data)?.overrides ?? {}).sort(([a], [b]) => a.localeCompare(b));
    const selectedDates = [...chosen].sort();
    const busy = fetchingYears.has(year) || snapshot?.fetching;

    return <section className="holiday-page">
        <div className="holiday-heading">
            <h2>节假日管理</h2>
            <div className="holiday-actions">
                <JuButton variant="secondary" disabled={!snapshot} onClick={() => {
                    setBatch(!batch); setChosen(new Set()); setType(batch ? "auto" : "off"); setRemark("");
                    if (batch) fill(selected);
                }}>{batch ? "取消批量调整" : "批量调整"}</JuButton>
                <JuButton variant="secondary" onClick={() => setRecordsOpen(true)}>手动设置记录 ({records.length})</JuButton>
            </div>
        </div>
        {snapshot?.manualError && <p role="alert" className="holiday-error">{snapshot.manualError}；请修复文件后再调整。</p>}
        <div className="holiday-toolbar">
            <div className="holiday-controls">
                <JuSelect ariaLabel="年份" value={String(year)} onValueChange={(v) => setYear(Number(v))} options={years.map((n) => ({ value: String(n), label: `${n} 年` }))} />
                <JuButton variant="secondary" aria-label="上个月" onClick={() => move(-1)}>‹</JuButton>
                <JuSelect ariaLabel="月份" value={String(month)} onValueChange={(v) => setMonth(Number(v))} options={Array.from({ length: 12 }, (_, i) => ({ value: String(i + 1), label: `${i + 1} 月` }))} />
                <JuButton variant="secondary" aria-label="下个月" onClick={() => move(1)}>›</JuButton>
                <JuButton variant="secondary" onClick={() => { const date = localToday(); setYear(Number(date.slice(0, 4))); setMonth(Number(date.slice(5, 7))); if (!batch) fill(date); }}>今天</JuButton>
            </div>
            <div className="holiday-status">{busy ? "正在获取官方公告…" : !snapshot ? "正在读取…" : snapshot.hasAnnouncement ? "已获取官方公告" : "未获取官方公告"}
                {(busy || (snapshot && !snapshot.hasAnnouncement && year <= new Date().getFullYear())) && <JuButton variant="secondary" disabled={!!busy} onClick={() => void update()}>{busy ? "更新中" : "更新"}</JuButton>}
            </div>
        </div>
        <div className="holiday-grid">
            {["一", "二", "三", "四", "五", "六", "日"].map((day) => <div className="holiday-weekday" key={day}>周{day}</div>)}
            {Array.from({ length: firstDay }, (_, i) => <div key={`blank-${i}`} />)}
            {snapshot?.days.map((day) => {
                const specialWork = !day.isOffDay && (day.isWeekend || day.officialIsOffDay === true || day.source === "official");
                const active = batch ? chosen.has(day.date) : selected === day.date;
                return <button key={day.date} type="button" title={day.label} aria-label={`${day.date} ${day.label}`}
                    className={`holiday-day ${day.isOffDay ? "off" : specialWork ? "special" : "work"} ${day.isManual ? "manual" : ""} ${active ? "selected" : ""} ${day.date === localToday() ? "today" : ""}`}
                    onClick={() => {
                        if (!batch) fill(day.date);
                        else setChosen((old) => { const next = new Set(old); if (next.has(day.date)) next.delete(day.date); else next.add(day.date); return next; });
                    }}>
                    <span>{Number(day.date.slice(8))}</span>{batch && active && <b className="holiday-check">✓</b>}
                    <span className="holiday-label">{day.label}</span>
                </button>;
            })}
        </div>
        <div className="holiday-legend"><span>🟩 休息日</span><span>🟧 补班／周末工作</span><span>▧ 手动设置</span><span>⬜ 普通工作日</span>{batch && <span>☑ 批量选中</span>}</div>
        {batch && <div className="holiday-selection">
            <div>已选日期 ({chosen.size}) <button type="button" onClick={() => setChosen(new Set())}>清空选择</button></div>
            <div className="holiday-tags">{selectedDates.length ? selectedDates.map((date) => <button type="button" key={date} onClick={() => setChosen((old) => { const next = new Set(old); next.delete(date); return next; })}>{date} ×</button>) : "点击日历选择日期，支持跨年、跨月选择。"}</div>
        </div>}
        <div className="holiday-edit">
            <JuSelect ariaLabel="调整类型" value={type} onValueChange={setType} options={options} />
            <JuInput label="备注（选填）" labelHidden placeholder="备注（选填）" value={remark} disabled={type === "auto"} onChange={(e) => setRemark(e.target.value)} />
            <JuButton disabled={saving || !snapshot || !!snapshot.manualError || (batch ? !chosen.size : !selected.startsWith(`${year}-${pad(month)}-`))} onClick={() => {
                void apply(batch ? selectedDates : [selected], type, remark).then((ok) => {
                    if (ok && batch) { setBatch(false); setChosen(new Set()); setType("auto"); setRemark(""); }
                });
            }}>{batch ? `批量应用 (${chosen.size})` : "应用"}</JuButton>
        </div>
        <JuDialog title="手动设置记录" open={recordsOpen} onOpenChange={setRecordsOpen}>
            <div className="holiday-records">{records.length ? <table><thead><tr><th>日期</th><th>调整类型</th><th>备注</th><th>操作</th></tr></thead><tbody>
                {records.map(([date, value]) => <tr key={date}><td>{date}</td><td>{value.isOffDay ? "休息日" : "工作日"}</td><td>{value.remark}</td><td>
                    <button type="button" onClick={() => { setRecordsOpen(false); setYear(Number(date.slice(0, 4))); setMonth(Number(date.slice(5, 7))); if (!batch) fill(date); }}>定位</button>
                    <button type="button" disabled={saving || !!snapshot?.manualError} onClick={() => void apply([date], "auto", "")}>恢复</button>
                </td></tr>)}</tbody></table> : "暂无手动设置记录"}</div>
        </JuDialog>
    </section>;
}
