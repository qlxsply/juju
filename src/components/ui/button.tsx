import type { ButtonHTMLAttributes, ReactNode } from "react";

export function ButtonPrimitive({
    children,
    ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { children: ReactNode }) {
    return <button {...props}>{children}</button>;
}
