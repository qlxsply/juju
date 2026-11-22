import type { ReactNode } from "react";
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
}: {
    content: string;
    children: ReactNode;
}) {
    return (
        <TooltipProvider delayDuration={400}>
            <TooltipRoot>
                <TooltipTrigger asChild>{children}</TooltipTrigger>
                <TooltipPortal>
                    <TooltipContent className="ju-tooltip" sideOffset={5}>
                        {content}
                    </TooltipContent>
                </TooltipPortal>
            </TooltipRoot>
        </TooltipProvider>
    );
}
