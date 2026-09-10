export type LauncherState = "waitingForModifiers" | "armed" | "closed";

export type LauncherAction = "none" | "openJson" | "openSettings" | "close";

export type LauncherEvent =
  | { type: "modifiersReleased" }
  | { type: "timeout" }
  | { type: "keyDown"; code: string; repeat: boolean };

export interface LauncherTransition {
  state: LauncherState;
  action: LauncherAction;
}

export function transitionLauncher(
  state: LauncherState,
  event: LauncherEvent,
): LauncherTransition {
  if (state === "closed") {
    return { state, action: "none" };
  }

  if (event.type === "modifiersReleased") {
    return state === "waitingForModifiers"
      ? { state: "armed", action: "none" }
      : { state, action: "none" };
  }

  if (event.type === "timeout") {
    return state === "armed"
      ? { state: "closed", action: "close" }
      : { state, action: "none" };
  }

  if (state !== "armed" || event.repeat) {
    return { state, action: "none" };
  }

  switch (event.code) {
    case "Digit1":
    case "Numpad1":
      return { state: "closed", action: "openJson" };
    case "KeyS":
      return { state: "closed", action: "openSettings" };
    case "Escape":
      return { state: "closed", action: "close" };
    default:
      return { state, action: "none" };
  }
}
