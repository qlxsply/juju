import { CircleAlert, CircleCheck, Info, TriangleAlert } from "lucide-react";
import type { ValidationLevel } from "./validation";

/** Shared visual and semantic definitions for field-level indicators. */
export const validationIndicatorDefinitions = {
    info: { label: "提示", icon: Info, description: "说明字段用途、填写规则或限制。" },
    warning: { label: "警告", icon: TriangleAlert, description: "提示需要留意但不一定阻止操作的情况。" },
    error: { label: "错误", icon: CircleAlert, description: "提示需要修正的无效输入或错误。" },
    success: { label: "成功", icon: CircleCheck, description: "提示字段已通过校验或操作成功。" },
} satisfies Record<ValidationLevel, { label: string; icon: typeof Info; description: string }>;
