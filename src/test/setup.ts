import { cleanup } from "@testing-library/react";
import "@testing-library/jest-dom/vitest";
import { afterEach } from "vitest";

if (!HTMLElement.prototype.scrollIntoView) {
    HTMLElement.prototype.scrollIntoView = () => undefined;
}

if (!HTMLElement.prototype.hasPointerCapture) {
    HTMLElement.prototype.hasPointerCapture = () => false;
    HTMLElement.prototype.setPointerCapture = () => undefined;
    HTMLElement.prototype.releasePointerCapture = () => undefined;
}

afterEach(cleanup);
