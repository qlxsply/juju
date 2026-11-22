import type { HTMLAttributes, ReactNode } from "react";

export function JuToolbar({
    children,
    ...props
}: Omit<HTMLAttributes<HTMLDivElement>, "className" | "style"> & {
    children: ReactNode;
}) {
    return (
        <div {...props} className="ju-toolbar">
            {children}
        </div>
    );
}
