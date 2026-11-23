import { useState, type ReactNode } from "react";
import { JuButton } from "./JuButton";
import { JuDialog, JuDialogClose } from "./JuDialog";

export function JuConfirmDialog({
    title,
    description,
    trigger,
    confirmLabel = "确认",
    cancelLabel = "取消",
    onConfirm,
    destructive = false,
}: {
    title: string;
    description: string;
    trigger: ReactNode;
    confirmLabel?: string;
    cancelLabel?: string;
    onConfirm: () => void;
    destructive?: boolean;
}) {
    const [open, setOpen] = useState(false);
    return (
        <JuDialog
            title={title}
            description={description}
            trigger={trigger}
            open={open}
            onOpenChange={setOpen}
            footer={
                <>
                    <JuDialogClose>
                        <JuButton variant="outline">{cancelLabel}</JuButton>
                    </JuDialogClose>
                    <JuButton
                        variant={destructive ? "destructive" : "primary"}
                        onClick={() => {
                            onConfirm();
                            setOpen(false);
                        }}
                    >
                        {confirmLabel}
                    </JuButton>
                </>
            }
        />
    );
}
