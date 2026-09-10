import { listen, type UnlistenFn } from "@tauri-apps/api/event";
import { useEffect, useEffectEvent, useState } from "react";

import { notifyWindowReady } from "../app/ipc";
import {
  transitionLauncher,
  type LauncherAction,
  type LauncherState,
} from "./launcherState";

const ARMED_EVENT = "launcher://armed";
const ARM_FALLBACK_MS = 500;

export function useLauncherKeyboard(
  onAction: (action: Exclude<LauncherAction, "none">) => void,
) {
  const [state, setState] = useState<LauncherState>("waitingForModifiers");
  const [timeoutMs, setTimeoutMs] = useState(2_000);
  const handleAction = useEffectEvent(onAction);

  useEffect(() => {
    let disposed = false;
    let unlisten: UnlistenFn | undefined;
    let armFallback: number | undefined;

    async function initialize() {
      try {
        unlisten = await listen(ARMED_EVENT, () => {
          setState((current) =>
            transitionLauncher(current, { type: "modifiersReleased" }).state,
          );
        });
        if (disposed) {
          unlisten();
          return;
        }
      } catch {
        // The ready handshake must not depend on optional event delivery.
      }

      performance.mark("launcher-react-mounted");
      const ready = await notifyWindowReady();
      if (!disposed) {
        setTimeoutMs(ready.timeoutMs);
        if (ready.armed) {
          setState((current) =>
            transitionLauncher(current, { type: "modifiersReleased" }).state,
          );
        } else {
          armFallback = window.setTimeout(() => {
            setState((current) =>
              transitionLauncher(current, { type: "modifiersReleased" }).state,
            );
          }, ARM_FALLBACK_MS);
        }
      }
    }

    void initialize().catch(() => {
      if (!disposed) {
        setState("armed");
      }
    });
    return () => {
      disposed = true;
      if (armFallback !== undefined) window.clearTimeout(armFallback);
      unlisten?.();
    };
  }, []);

  useEffect(() => {
    function handleKeyUp(event: KeyboardEvent) {
      if (!event.ctrlKey && !event.shiftKey && !event.altKey) {
        setState((current) =>
          transitionLauncher(current, { type: "modifiersReleased" }).state,
        );
      }
    }
    document.addEventListener("keyup", handleKeyUp, true);
    return () => document.removeEventListener("keyup", handleKeyUp, true);
  }, []);

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      const code = event.key === "1"
        ? "Digit1"
        : event.key.toLowerCase() === "s"
          ? "KeyS"
          : event.key === "Escape"
            ? "Escape"
            : event.code;
      const effectiveState = state === "waitingForModifiers"
        && !event.ctrlKey
        && !event.shiftKey
        && !event.altKey
        ? transitionLauncher(state, { type: "modifiersReleased" }).state
        : state;
      const transition = transitionLauncher(effectiveState, {
        type: "keyDown",
        code,
        repeat: event.repeat,
      });
      if (transition.action === "none") {
        return;
      }

      event.preventDefault();
      setState(transition.state);
      handleAction(transition.action);
    }

    document.addEventListener("keydown", handleKeyDown, true);
    return () => document.removeEventListener("keydown", handleKeyDown, true);
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
