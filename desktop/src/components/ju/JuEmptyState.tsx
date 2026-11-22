import type { ReactNode } from "react";

export function JuEmptyState({
    title,
    description,
    action,
}: {
    title: string;
    description?: string;
    action?: ReactNode;
}) {
    return (
        <div className="ju-empty-state">
            <span className="ju-empty-icon" aria-hidden="true">
                —
            </span>
            <h3>{title}</h3>
            {description && <p>{description}</p>}
            {action}
        </div>
    );
}
