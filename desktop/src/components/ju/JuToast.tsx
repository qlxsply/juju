import type { ReactNode } from "react";

export function JuToast({ children }: { children: ReactNode }) {
    return (
        <div className="ju-toast" role="status" aria-live="polite">
            {children}
        </div>
    );
}
