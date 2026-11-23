import type { ButtonHTMLAttributes, ReactNode } from "react";
import { ButtonPrimitive } from "../ui/button";

type JuButtonVariant =
    | "primary"
    | "secondary"
    | "outline"
    | "ghost"
    | "destructive"
    | "navigation"
    | "navigationActive"
    | "navHead"
    | "subnav"
    | "subnavActive"
    | "launcher";
type JuButtonSize = "default" | "compact" | "large";

interface JuButtonProps extends Omit<
    ButtonHTMLAttributes<HTMLButtonElement>,
    "className" | "style"
> {
    variant?: JuButtonVariant;
    size?: JuButtonSize;
    loading?: boolean;
    children: ReactNode;
}

export function JuButton({
    variant = "primary",
    size = "default",
    loading = false,
    disabled,
    children,
    ...props
}: JuButtonProps) {
    return (
        <ButtonPrimitive
            {...props}
            aria-busy={loading || undefined}
            className={`ju-button ju-button-${variant} ju-button-${size}`}
            disabled={disabled || loading}
            type={props.type ?? "button"}
        >
            {loading && <span className="ju-spinner" aria-hidden="true" />}
            {children}
        </ButtonPrimitive>
    );
}
