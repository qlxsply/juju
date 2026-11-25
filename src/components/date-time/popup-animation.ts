import { createPopper, type Instance, type Placement } from "@popperjs/core";
import type { AirDatepickerPositionCallback } from "air-datepicker";
import type { PickerPlacement } from "./types";

export interface PopupPositionController {
    position: AirDatepickerPositionCallback;
    dispose: () => void;
}

// Only one picker should be closing when another picker starts opening.
let closingPopup: { owner: object; finish: () => void } | null = null;

export function createPopupPositionController(placement: PickerPlacement): PopupPositionController {
    const owner = {};
    let popper: Instance | null = null;
    let animation: Animation | null = null;
    let pendingDone: (() => void) | null = null;
    let generation = 0;
    let alive = true;
    let currentElement: HTMLDivElement | null = null;
    let lockedSide: "top" | "bottom" | null = null;
    let resizeObserver: ResizeObserver | null = null;
    const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");

    const clearAnimation = () => {
        animation?.cancel();
        animation = null;
    };

    const finishHide = (token: number) => {
        if (token !== generation) return;
        if (closingPopup?.owner === owner) closingPopup = null;
        const done = pendingDone;
        pendingDone = null;
        clearAnimation();
        popper?.destroy();
        popper = null;
        resizeObserver?.disconnect();
        resizeObserver = null;
        lockedSide = null;
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
        closingPopup = { owner, finish: () => finishHide(token) };
        clearAnimation();
        if (reducedMotion.matches) {
            finishHide(token);
            return;
        }
        animation = currentElement.animate(
            [
                { transform: "perspective(850px) rotateX(0deg)", opacity: 1 },
                { transform: `perspective(850px) rotateX(${lockedSide === "top" ? 88 : -88}deg)`, opacity: 0 },
            ],
            { duration: 320, easing: "cubic-bezier(.55,0,.8,.35)", fill: "forwards" },
        );
        void animation.finished.then(() => finishHide(token)).catch(() => undefined);
    };

    const position: AirDatepickerPositionCallback = ({
        $datepicker,
        $target,
        isViewChange,
        done,
    }) => {
        if (!alive) {
            done();
            return;
        }

        // Finish a different picker's close immediately, so two popups never overlap.
        if (closingPopup && closingPopup.owner !== owner) closingPopup.finish();

        // Reuse the same mounted node on a fast reopen. Calling the previous
        // done callback here would destroy it in the middle of Air Datepicker's
        // show(), making its measured height zero. The next actual hide will
        // run that same cleanup callback once the popup is really closed.
        if (pendingDone) {
            pendingDone = null;
            if (closingPopup?.owner === owner) closingPopup = null;
            generation += 1;
            clearAnimation();
            popper?.destroy();
            popper = null;
            resizeObserver?.disconnect();
            resizeObserver = null;
            lockedSide = null;
            currentElement?.classList.remove("-juju-visible-");
            currentElement = null;
        }

        currentElement = $datepicker;
        if (!popper) {
            const targetRect = $target.getBoundingClientRect();
            const padding = 8;
            const gap = 10;
            const above = Math.max(0, targetRect.top - padding - gap);
            const below = Math.max(0, window.innerHeight - targetRect.bottom - padding - gap);
            const height = $datepicker.scrollHeight;
            const preferred = placement.startsWith("top") ? "top" : "bottom";
            const preferredSpace = preferred === "top" ? above : below;
            const oppositeSpace = preferred === "top" ? below : above;
            lockedSide = preferredSpace >= height || preferredSpace >= oppositeSpace ? preferred : preferred === "top" ? "bottom" : "top";
            const updateAvailableHeight = () => {
                if (!currentElement || !lockedSide) return;
                const rect = $target.getBoundingClientRect();
                const available = lockedSide === "top" ? rect.top - padding - gap : window.innerHeight - rect.bottom - padding - gap;
                currentElement.style.maxHeight = `${Math.max(0, available)}px`;
                currentElement.style.overflowY = currentElement.scrollHeight > Math.max(0, available) ? "auto" : "";
            };
            updateAvailableHeight();
            popper = createPopper($target, $datepicker, {
                placement: `${lockedSide}-${placement.endsWith("end") ? "end" : "start"}` as Placement,
                modifiers: [
                    { name: "offset", options: { offset: [0, 10] } },
                    { name: "flip", enabled: false },
                    { name: "preventOverflow", options: { padding: 8, altBoundary: true } },
                    { name: "computeStyles", options: { gpuAcceleration: false, adaptive: false } },
                    { name: "jujuAvailableHeight", enabled: true, phase: "beforeRead", fn: updateAvailableHeight },
                ],
                onFirstUpdate: () => {
                    if (!currentElement || !alive) return;
                    currentElement.style.transformOrigin = lockedSide === "top" ? "center bottom" : "center top";
                    currentElement.style.transform = "perspective(850px) rotateX(0deg)";
                    currentElement.style.opacity = "1";
                    if (isViewChange || reducedMotion.matches) {
                        return;
                    }
                    clearAnimation();
                    animation = currentElement.animate(
                        [
                            { transform: `perspective(850px) rotateX(${lockedSide === "top" ? 88 : -88}deg)`, opacity: 0.1 },
                            { transform: "perspective(850px) rotateX(0deg)", opacity: 1 },
                        ],
                        { duration: 520, easing: "cubic-bezier(.2,.72,.18,1)", fill: "forwards" },
                    );
                    void animation.finished.catch(() => undefined);
                },
            });
            resizeObserver = new ResizeObserver(() => { void popper?.update(); });
            resizeObserver.observe($datepicker);
            resizeObserver.observe($target);
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
            if (closingPopup?.owner === owner) closingPopup = null;
            generation += 1;
            reducedMotion.removeEventListener("change", onMotionPreferenceChange);
            clearAnimation();
            const done = pendingDone;
            pendingDone = null;
            popper?.destroy();
            popper = null;
            resizeObserver?.disconnect();
            resizeObserver = null;
            lockedSide = null;
            currentElement = null;
            done?.();
        },
    };
}
