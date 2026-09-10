import { listen, type UnlistenFn } from "@tauri-apps/api/event";
import { useEffect, useEffectEvent, useState } from "react";

import { notifyWindowReady } from "../app/ipc";
import {
  transitionLauncher,
  type LauncherAction,
  type LauncherState,
} from "./launcherState";

const ARMED_EVENT = "launcher://armed";

export function useLauncherKeyboard(
  onAction: (action: Exclude<LauncherAction, "none">) => void,
) {
  const [state, setState] = useState<LauncherState>("waitingForModifiers");
  const [timeoutMs, setTimeoutMs] = useState(2_000);
  const handleAction = useEffectEvent(onAction);

  useEffect(() => {
    let disposed = false;
    let unlisten: UnlistenFn | undefined;

    async function initialize() {
      unlisten = await listen(ARMED_EVENT, () => {
        setState((current) =>
          transitionLauncher(current, { type: "modifiersReleased" }).state,
        );
      });
      if (disposed) {
        unlisten();
        return;
      }

      performance.mark("launcher-react-mounted");
      const ready = await notifyWindowReady();
      if (!disposed) {
        setTimeoutMs(ready.timeoutMs);
        if (ready.armed) {
          setState((current) =>
            transitionLauncher(current, { type: "modifiersReleased" }).state,
          );
        }
      }
    }

    void initialize().catch(() => handleAction("close"));
    return () => {
      disposed = true;
      unlisten?.();
    };
  }, []);

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      const transition = transitionLauncher(state, {
        type: "keyDown",
        code: event.code,
        repeat: event.repeat,
      });
      if (transition.action === "none") {
        return;
      }

      event.preventDefault();
      setState(transition.state);
      handleAction(transition.action);
    }

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [state]);

  useEffect(() => {
    if (state !== "armed") {
      return;
    }

    const timeout = window.setTimeout(() => {
      const transition = transitionLauncher("armed", { type: "timeout" });
      setState(transition.state);
      if (transition.action !== "none") {
        handleAction(transition.action);
      }
    }, timeoutMs);
    return () => window.clearTimeout(timeout);
  }, [state, timeoutMs]);

  return state;
}
