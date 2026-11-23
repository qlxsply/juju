import type { ButtonHTMLAttributes, ReactNode } from "react";
import { ButtonPrimitive } from "../ui/button";

interface JuIconButtonProps extends Omit<
    ButtonHTMLAttributes<HTMLButtonElement>,
    "className" | "style"
> {
    label: string;
    children: ReactNode;
    variant?: "default" | "ghost" | "destructive";
    size?: "default" | "compact";
}

export function JuIconButton({
    label,
    children,
    variant = "ghost",
    size = "default",
    ...props
}: JuIconButtonProps) {
    return (
        <ButtonPrimitive
            {...props}
            aria-label={label}
            className={`ju-icon-button ju-icon-button-${variant} ju-icon-button-${size}`}
            type={props.type ?? "button"}
        >
            {children}
        </ButtonPrimitive>
    );
}
