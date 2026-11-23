import { createPopper, type Instance, type Placement } from "@popperjs/core";
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
    let animation: Animation | null = null;
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
        animation = currentElement.animate(
            [
                { transform: "perspective(850px) rotateX(0deg)", opacity: 1 },
                { transform: "perspective(850px) rotateX(-88deg)", opacity: 0 },
            ],
            { duration: 320, easing: "cubic-bezier(.55,0,.8,.35)", fill: "forwards" },
        );
        void animation.finished.then(() => finishHide(token)).catch(() => undefined);
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
                    { name: "flip", enabled: false },
                    { name: "preventOverflow", options: { padding: 8, altBoundary: true } },
                    { name: "arrow", options: { element: $pointer, padding: 6 } },
                    { name: "computeStyles", options: { gpuAcceleration: false, adaptive: false } },
                ],
                onFirstUpdate: () => {
                    if (!currentElement || !alive) return;
                    currentElement.style.transformOrigin = "center top";
                    currentElement.style.transform = "perspective(850px) rotateX(0deg)";
                    currentElement.style.opacity = "1";
                    if (isViewChange || reducedMotion.matches) {
                        return;
                    }
                    clearAnimation();
                    animation = currentElement.animate(
                        [
                            { transform: "perspective(850px) rotateX(-88deg)", opacity: 0.1 },
                            { transform: "perspective(850px) rotateX(0deg)", opacity: 1 },
                        ],
                        { duration: 520, easing: "cubic-bezier(.2,.72,.18,1)", fill: "forwards" },
                    );
                    void animation.finished.catch(() => undefined);
                },
            });
        } else {
            void popper.update();
        }

        return () => hide(done);
    };

    const onMotionPreferenceChange = (event: MediaQueryListEvent) => {
        if (!event.matches || !currentElement) return;
        generation += 1;
        clearAnimation();
        currentElement.style.opacity = "1";
        currentElement.style.transform = "perspective(850px) rotateX(0deg)";
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
