import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ErrorBoundary } from "./ErrorBoundary";

describe("ErrorBoundary", () => {
    it("shows a user-facing fallback when a child fails to render", () => {
        const logError = vi
            .spyOn(console, "error")
            .mockImplementation(() => {});

        function BrokenView(): never {
            throw new Error("render failed");
        }

        render(
            <ErrorBoundary>
                <BrokenView />
            </ErrorBoundary>,
        );

        expect(
            screen.getByRole("heading", { name: "界面暂时无法显示" }),
        ).toBeInTheDocument();
        expect(logError).toHaveBeenCalled();
    });
});
