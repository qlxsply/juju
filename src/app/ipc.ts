import { invoke } from "@tauri-apps/api/core";

export type LauncherTarget = "json" | "settings";

interface LauncherReady {
  timeoutMs: number;
  armed: boolean;
}

export function notifyWindowReady() {
  return invoke<LauncherReady>("window_ready");
}

export function closeLauncher() {
  return invoke<void>("launcher_close");
}

export function openLauncherTarget(target: LauncherTarget) {
  return invoke<void>("launcher_open_target", { target });
}
