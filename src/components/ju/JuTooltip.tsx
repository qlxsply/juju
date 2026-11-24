import type { ReactElement, ReactNode } from "react";
import {
    TooltipContent,
    TooltipPortal,
    TooltipProvider,
    TooltipRoot,
    TooltipTrigger,
} from "../ui/tooltip";

export function JuTooltip({
    content,
    children,
    side = "top",
    sideOffset = 5,
    delayDuration = 400,
}: {
    content: ReactNode;
    children: ReactElement;
    side?: "top" | "bottom" | "left" | "right";
    sideOffset?: number;
    delayDuration?: number;
}) {
    return (
        <TooltipProvider delayDuration={delayDuration}>
            <TooltipRoot>
                <TooltipTrigger asChild>{children}</TooltipTrigger>
                <TooltipPortal>
                    <TooltipContent className="ju-tooltip" side={side} sideOffset={sideOffset}>
                        {content}
                    </TooltipContent>
                </TooltipPortal>
            </TooltipRoot>
        </TooltipProvider>
    );
}
