import { createPopper, type Instance, type Placement } from "@popperjs/core";
import { animate, spring, type JSAnimation } from "animejs";
import type { AirDatepickerPositionCallback } from "air-datepicker";
import type { PickerPlacement } from "./types";

export interface PopupPositionController {
    position: AirDatepickerPositionCallback;
    dispose: () => void;
}

const placementMap: Record<PickerPlacement, Placement> = {
    "bottom-start": "bottom-start",
    "bottom-end": "bottom-end",
    "top-start": "top-start",
    "top-end": "top-end",
};

export function createPopupPositionController(placement: PickerPlacement): PopupPositionController {
    let popper: Instance | null = null;
    let animation: JSAnimation | null = null;
    let pendingDone: (() => void) | null = null;
    let generation = 0;
    let alive = true;
    let currentElement: HTMLDivElement | null = null;
    const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");

    const clearAnimation = () => {
        animation?.cancel();
        animation = null;
    };

    const finishHide = (token: number) => {
        if (token !== generation) return;
        const done = pendingDone;
        pendingDone = null;
        clearAnimation();
        popper?.destroy();
        popper = null;
        currentElement?.classList.remove("-juju-visible-");
        currentElement = null;
        done?.();
    };

    const hide = (done: () => void) => {
        if (!alive) {
            done();
            return;
        }
        if (!popper || !currentElement) {
            currentElement?.classList.remove("-juju-visible-");
            done();
            return;
        }
        if (pendingDone) return;
        currentElement.classList.remove("-juju-visible-");
        pendingDone = done;
        const token = ++generation;
        clearAnimation();
        if (reducedMotion.matches) {
            finishHide(token);
            return;
        }
        animation = animate(currentElement, {
            opacity: { to: 0, duration: 300, ease: "out(3)" },
            rotateX: { to: "-90deg", duration: 300, ease: "out(3)" },
        });
        void animation.then(() => finishHide(token));
    };

    const position: AirDatepickerPositionCallback = ({
        $datepicker,
        $target,
        $pointer,
        isViewChange,
        done,
    }) => {
        if (!alive) {
            done();
            return;
        }

        // A fast reopen completes the previous hide handshake before replacing its Popper instance.
        if (pendingDone) {
            const previousDone = pendingDone;
            pendingDone = null;
            generation += 1;
            clearAnimation();
            popper?.destroy();
            popper = null;
            currentElement?.classList.remove("-juju-visible-");
            currentElement = null;
            previousDone();
        }

        currentElement = $datepicker;
        if (!popper) {
            popper = createPopper($target, $datepicker, {
                placement: placementMap[placement],
                modifiers: [
                    { name: "offset", options: { offset: [0, 10] } },
                    { name: "flip", options: { padding: 8 } },
                    { name: "preventOverflow", options: { padding: 8, altBoundary: true } },
                    { name: "arrow", options: { element: $pointer, padding: 6 } },
                    { name: "computeStyles", options: { gpuAcceleration: false } },
                ],
                onFirstUpdate: (state) => {
                    if (!currentElement || !alive) return;
                    currentElement.style.transformOrigin = state.placement?.startsWith("top")
                        ? "center bottom"
                        : "center top";
                    if (isViewChange || reducedMotion.matches) {
                        currentElement.style.opacity = "1";
                        currentElement.style.rotate = "x 0deg";
                        return;
                    }
                    clearAnimation();
                    animation = animate(currentElement, {
                        opacity: { from: 0, to: 1, ease: spring({ mass: 1.3, stiffness: 80, damping: 5, velocity: 0 }) },
                        rotateX: { from: "-90deg", to: "0deg", ease: spring({ mass: 1.3, stiffness: 80, damping: 5, velocity: 0 }) },
                    });
                },
            });
        } else {
            void popper.update();
            const state = popper.state;
            $datepicker.style.transformOrigin = state.placement?.startsWith("top") ? "center bottom" : "center top";
        }

        return () => hide(done);
    };

    const onMotionPreferenceChange = (event: MediaQueryListEvent) => {
        if (!event.matches || !currentElement) return;
        generation += 1;
        clearAnimation();
        currentElement.style.opacity = "1";
        currentElement.style.rotate = "x 0deg";
        if (pendingDone) finishHide(generation);
    };
    reducedMotion.addEventListener("change", onMotionPreferenceChange);

    return {
        position,
        dispose() {
            if (!alive) return;
            alive = false;
            generation += 1;
            reducedMotion.removeEventListener("change", onMotionPreferenceChange);
            clearAnimation();
            const done = pendingDone;
            pendingDone = null;
            popper?.destroy();
            popper = null;
            currentElement = null;
            done?.();
        },
    };
}
