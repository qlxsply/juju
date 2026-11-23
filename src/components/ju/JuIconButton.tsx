import type { ButtonHTMLAttributes, ReactNode } from "react";
import { ButtonPrimitive } from "../ui/button";

interface JuIconButtonProps extends Omit<
    ButtonHTMLAttributes<HTMLButtonElement>,
    "className" | "style"
> {
    label: string;
    children: ReactNode;
    variant?: "default" | "ghost" | "destructive";
}

export function JuIconButton({
    label,
    children,
    variant = "ghost",
    ...props
}: JuIconButtonProps) {
    return (
        <ButtonPrimitive
            {...props}
            aria-label={label}
            className={`ju-icon-button ju-icon-button-${variant}`}
            type={props.type ?? "button"}
        >
            {children}
        </ButtonPrimitive>
    );
}
