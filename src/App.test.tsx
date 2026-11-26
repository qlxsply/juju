import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { App } from "./App";

describe("App", () => {
    it("shows the reference launcher entries and hotkeys", () => {
        render(<App />);

        expect(screen.getByText("juju", { exact: true })).toBeInTheDocument();
        expect(
            screen.getByRole("button", { name: /JSON 格式化/ }),
        ).toContainElement(screen.getByText("J", { exact: true }));
        expect(
            screen.getByRole("button", { name: /ToTo 日程/ }),
        ).toBeEnabled();
        expect(
            screen.getByRole("button", { name: /MySQL Schema/ }),
        ).toHaveTextContent("M");
        expect(
            screen.getByRole("button", { name: /设计系统工作台/ }),
        ).toBeInTheDocument();
    });
});
