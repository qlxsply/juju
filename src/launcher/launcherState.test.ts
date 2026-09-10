import { describe, expect, it } from "vitest";

import { transitionLauncher } from "./launcherState";

describe("launcher keyboard state machine", () => {
  it("does not accept commands before modifiers are released", () => {
    expect(
      transitionLauncher("waitingForModifiers", {
        type: "keyDown",
        code: "Digit1",
        repeat: false,
      }),
    ).toEqual({ state: "waitingForModifiers", action: "none" });
  });

  it("arms after modifier release", () => {
    expect(
      transitionLauncher("waitingForModifiers", { type: "modifiersReleased" }),
    ).toEqual({ state: "armed", action: "none" });
  });

  it.each([
    ["Digit1", "openJson"],
    ["Numpad1", "openJson"],
    ["KeyS", "openSettings"],
    ["Escape", "close"],
  ] as const)("maps %s to %s", (code, action) => {
    expect(
      transitionLauncher("armed", { type: "keyDown", code, repeat: false }),
    ).toEqual({ state: "closed", action });
  });

  it("ignores repeated and unknown keys", () => {
    expect(
      transitionLauncher("armed", {
        type: "keyDown",
        code: "Digit1",
        repeat: true,
      }),
    ).toEqual({ state: "armed", action: "none" });
    expect(
      transitionLauncher("armed", {
        type: "keyDown",
        code: "KeyX",
        repeat: false,
      }),
    ).toEqual({ state: "armed", action: "none" });
  });

  it("closes on timeout only after arming", () => {
    expect(transitionLauncher("armed", { type: "timeout" })).toEqual({
      state: "closed",
      action: "close",
    });
    expect(
      transitionLauncher("waitingForModifiers", { type: "timeout" }),
    ).toEqual({ state: "waitingForModifiers", action: "none" });
  });
});
