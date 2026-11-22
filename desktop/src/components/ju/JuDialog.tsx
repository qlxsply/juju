import type { ReactNode } from "react";
import {
    DialogClose,
    DialogContent,
    DialogDescription,
    DialogOverlay,
    DialogPortal,
    DialogRoot,
    DialogTitle,
    DialogTrigger,
} from "../ui/dialog";

interface JuDialogProps {
    title: string;
    description?: string;
    trigger?: ReactNode;
    children?: ReactNode;
    footer?: ReactNode;
    open?: boolean;
    onOpenChange?: (open: boolean) => void;
}

export function JuDialog({
    title,
    description,
    trigger,
    children,
    footer,
    open,
    onOpenChange,
}: JuDialogProps) {
    return (
        <DialogRoot open={open} onOpenChange={onOpenChange}>
            {trigger && <DialogTrigger asChild>{trigger}</DialogTrigger>}
            <DialogPortal>
                <DialogOverlay className="ju-dialog-overlay" />
                <DialogContent className="ju-dialog-content">
                    <div className="ju-dialog-heading">
                        <DialogTitle className="ju-dialog-title">
                            {title}
                        </DialogTitle>
                        <DialogDescription className="ju-dialog-description">
                            {description ?? " "}
                        </DialogDescription>
                    </div>
                    {children && (
                        <div className="ju-dialog-body">{children}</div>
                    )}
                    {footer && <div className="ju-dialog-footer">{footer}</div>}
                </DialogContent>
            </DialogPortal>
        </DialogRoot>
    );
}

export function JuDialogClose({ children }: { children: ReactNode }) {
    return <DialogClose asChild>{children}</DialogClose>;
}
