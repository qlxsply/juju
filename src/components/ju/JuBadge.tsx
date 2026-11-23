import type { HTMLAttributes, ReactNode } from "react";

export function JuBadge({
    children,
    variant = "muted",
    ...props
}: Omit<HTMLAttributes<HTMLSpanElement>, "className" | "style"> & {
    children: ReactNode;
    variant?: "muted" | "success" | "warning" | "destructive";
}) {
    return (
        <span {...props} className={`ju-badge ju-badge-${variant}`}>
            {children}
        </span>
    );
}
