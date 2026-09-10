import { useEffect, useState } from "react";
import { getSettings, setAutostart, updateLauncherSettings, type AppSettings } from "../app/ipc";
import "./settings.css";

export function SettingsApp() {
  const [settings, setSettings] = useState<AppSettings | null>(null);
  const [shortcut, setShortcut] = useState("");
  const [timeout, setTimeoutValue] = useState(2000);
  const [message, setMessage] = useState("正在加载设置...");
  useEffect(() => { void getSettings().then((value) => { setSettings(value); setShortcut(value.launcher.shortcut); setTimeoutValue(value.launcher.timeout_ms); setMessage(""); }).catch((error: unknown) => setMessage(String(error))); }, []);
  async function save() { try { await updateLauncherSettings(shortcut, timeout); setMessage("设置已保存"); } catch (error) { setMessage(`保存失败: ${String(error)}`); } }
  return <main className="settings-shell"><header><p>JUJU SETTINGS</p><h1>设置</h1></header><section><h2>常规</h2><label><input checked={settings?.app.autostart ?? false} type="checkbox" onChange={(event) => void setAutostart(event.target.checked).then(() => setSettings((value) => value ? { ...value, app: { autostart: event.target.checked } } : value)).catch((error: unknown) => setMessage(String(error)))} /> 开机自动启动 juju</label><label>全局唤醒快捷键<input value={shortcut} onChange={(event) => setShortcut(event.target.value)} /></label><label>Launcher 自动关闭时间（毫秒）<input min="100" type="number" value={timeout} onChange={(event) => setTimeoutValue(Number(event.target.value))} /></label><button onClick={() => void save()}>保存更改</button></section><section><h2>数据</h2><p>{settings?.data_root ?? ""}</p><small>数据目录切换与迁移由后端安全命令处理，将在此页后续接入选择目录界面。</small></section><footer>{message}</footer></main>;
}
