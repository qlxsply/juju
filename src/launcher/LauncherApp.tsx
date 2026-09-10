import { useEffectEvent, useState } from "react";

import {
  closeLauncher,
  openLauncherTarget,
  type LauncherTarget,
} from "../app/ipc";
import { useLauncherKeyboard } from "./useLauncherKeyboard";
import type { LauncherAction } from "./launcherState";
import "./launcher.css";

const entries: Array<{
  code: string;
  keyLabel: string;
  name: string;
  description: string;
  target: LauncherTarget;
}> = [
  {
    code: "Digit1",
    keyLabel: "1",
    name: "JSON",
    description: "Format, inspect and compare documents",
    target: "json",
  },
  {
    code: "KeyS",
    keyLabel: "S",
    name: "设置",
    description: "Shortcut, startup and storage",
    target: "settings",
  },
];

export function LauncherApp() {
  const [error, setError] = useState("");
  const runAction = useEffectEvent((action: Exclude<LauncherAction, "none">) => {
    let operation: Promise<void>;
    if (action === "openJson") {
      operation = openLauncherTarget("json");
    } else if (action === "openSettings") {
      operation = openLauncherTarget("settings");
    } else {
      operation = closeLauncher();
    }
    void operation.catch((reason: unknown) => setError(String(reason)));
  });
  const state = useLauncherKeyboard(runAction);
  const armed = state === "armed";

  return (
    <main className="launcher-shell" aria-label="juju 启动器">
      <header className="launcher-header">
        <div className="launcher-brand">
          <span className="launcher-glyph" aria-hidden="true">
            J
          </span>
          <div>
            <p>QUICK SWITCH</p>
            <h1>juju</h1>
          </div>
        </div>
        <span className={`launcher-status ${armed ? "is-armed" : ""}`}>
          <span aria-hidden="true" />
          {armed ? "READY" : "RELEASE KEYS"}
        </span>
      </header>

      <section className="launcher-entries" aria-label="工具列表">
        {entries.map((entry) => (
          <button
            aria-disabled={!armed}
            autoFocus={entry.code === "Digit1"}
            className="launcher-entry"
            data-code={entry.code}
            key={entry.code}
            onClick={() => void openLauncherTarget(entry.target)}
            type="button"
          >
            <kbd>{entry.keyLabel}</kbd>
            <span className="launcher-entry-copy">
              <strong>{entry.name}</strong>
              <small>{entry.description}</small>
            </span>
            <span className="launcher-arrow" aria-hidden="true">
              &#8594;
            </span>
          </button>
        ))}
      </section>

      <footer className="launcher-footer">
        <span>{error || (armed ? "按下快捷键打开工具" : "松开 Ctrl / Shift / Alt 以继续")}</span>
        <span>
          <kbd>ESC</kbd> 关闭
        </span>
      </footer>
    </main>
  );
}
