import { useEffect, useEffectEvent } from "react";

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
  const runAction = useEffectEvent((action: Exclude<LauncherAction, "none">) => {
    if (action === "openJson") {
      void openLauncherTarget("json");
    } else if (action === "openSettings") {
      void openLauncherTarget("settings");
    } else {
      void closeLauncher();
    }
  });
  const state = useLauncherKeyboard(runAction);
  const armed = state === "armed";

  useEffect(() => {
    let closeTimer: number | undefined;
    function handleBlur() {
      closeTimer = window.setTimeout(() => {
        if (!document.hasFocus()) {
          void closeLauncher();
        }
      }, 100);
    }

    window.addEventListener("blur", handleBlur);
    return () => {
      window.removeEventListener("blur", handleBlur);
      if (closeTimer !== undefined) {
        window.clearTimeout(closeTimer);
      }
    };
  }, []);

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
            className="launcher-entry"
            data-code={entry.code}
            disabled={!armed}
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
        <span>{armed ? "按下快捷键打开工具" : "松开 Ctrl / Shift / Alt 以继续"}</span>
        <span>
          <kbd>ESC</kbd> 关闭
        </span>
      </footer>
    </main>
  );
}
