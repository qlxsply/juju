import type { InputHTMLAttributes } from "react";

export function InputPrimitive(props: InputHTMLAttributes<HTMLInputElement>) {
    return <input {...props} />;
}
