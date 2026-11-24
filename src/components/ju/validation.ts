export type ValidationLevel = "info" | "warning" | "error" | "success";
export type ValidationMessage = { level: ValidationLevel; text: string };
export const validationPriority: ValidationLevel[] = ["error", "warning", "success", "info"];

export function focusFirstInvalidField(container: ParentNode): boolean {
    const field = container.querySelector<HTMLElement>('[aria-invalid="true"]:not([disabled])');
    if (!field) return false;
    field.focus();
    if (field.id) document.dispatchEvent(new CustomEvent("juju:show-validation", { detail: field.id }));
    return true;
}

export function validationMessages(values: Partial<Record<ValidationLevel, string | string[] | undefined>>): ValidationMessage[] {
    return validationPriority.flatMap((level) => {
        const value = values[level];
        return (Array.isArray(value) ? value : value ? [value] : []).filter(Boolean).map((text) => ({ level, text }));
    });
}
