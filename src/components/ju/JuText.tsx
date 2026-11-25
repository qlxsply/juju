import { forwardRef, type ComponentPropsWithoutRef } from "react";

/** Shared inline text primitive; inherits typography and color from its context. */
export const JuText = forwardRef<HTMLSpanElement, ComponentPropsWithoutRef<"span">>(
    function JuText(props, ref) {
        return <span {...props} ref={ref} />;
    },
);
