import type { HTMLAttributes, ReactNode } from "react";

export function JuStatusBar({
    children,
    ...props
}: Omit<HTMLAttributes<HTMLDivElement>, "className" | "style"> & {
    children: ReactNode;
}) {
    return (
        <div {...props} className="ju-status-bar">
            {children}
        </div>
    );
}
