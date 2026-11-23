export type TokenCategory =
    | "颜色"
    | "圆角"
    | "边框"
    | "尺寸"
    | "间距"
    | "字体"
    | "阴影"
    | "动效"
    | "层级";

export interface DesignToken {
    category: TokenCategory;
    name: string;
    variable: string;
    sample: string;
}

export const designTokens: DesignToken[] = [
    { category: "颜色", name: "主色", variable: "--primary", sample: "color" },
    { category: "颜色", name: "主色悬停", variable: "--primary-hover", sample: "color" },
    { category: "颜色", name: "主色文字", variable: "--primary-foreground", sample: "color" },
    { category: "颜色", name: "背景", variable: "--background", sample: "color" },
    { category: "颜色", name: "表面", variable: "--surface", sample: "color" },
    { category: "颜色", name: "表面悬停", variable: "--surface-hover", sample: "color" },
    { category: "颜色", name: "正文", variable: "--foreground", sample: "color" },
    { category: "颜色", name: "次要文字", variable: "--muted-foreground", sample: "color" },
    { category: "颜色", name: "边框", variable: "--border", sample: "color" },
    { category: "颜色", name: "选中背景", variable: "--selection", sample: "color" },
    { category: "颜色", name: "主色柔和底色", variable: "--primary-soft", sample: "color" },
    { category: "颜色", name: "焦点", variable: "--focus-ring", sample: "color" },
    { category: "颜色", name: "成功", variable: "--success", sample: "color" },
    { category: "颜色", name: "成功底色", variable: "--success-soft", sample: "color" },
    { category: "颜色", name: "警告", variable: "--warning", sample: "color" },
    { category: "颜色", name: "危险", variable: "--destructive", sample: "color" },
    { category: "颜色", name: "危险底色", variable: "--destructive-soft", sample: "color" },
    { category: "颜色", name: "危险文字", variable: "--destructive-foreground", sample: "color" },
    { category: "颜色", name: "禁用背景", variable: "--disabled-background", sample: "color" },
    { category: "颜色", name: "禁用文字", variable: "--disabled-foreground", sample: "color" },
    { category: "颜色", name: "遮罩", variable: "--overlay", sample: "color" },
    { category: "颜色", name: "JSON 工具", variable: "--tool-json", sample: "color" },
    { category: "颜色", name: "Toto 工具", variable: "--tool-toto", sample: "color" },
    { category: "颜色", name: "EchoHttp 工具", variable: "--tool-echo", sample: "color" },
    { category: "颜色", name: "Kairos 工具", variable: "--tool-kairos", sample: "color" },
    { category: "颜色", name: "MySQL 工具", variable: "--tool-mysql", sample: "color" },
    { category: "颜色", name: "设计系统工具", variable: "--tool-design", sample: "color" },

    { category: "圆角", name: "无圆角", variable: "--radius-none", sample: "shape" },
    { category: "圆角", name: "小圆角", variable: "--radius-sm", sample: "shape" },
    { category: "圆角", name: "控件圆角", variable: "--radius-control", sample: "shape" },
    { category: "圆角", name: "中圆角", variable: "--radius-md", sample: "shape" },
    { category: "圆角", name: "大圆角", variable: "--radius-lg", sample: "shape" },
    { category: "圆角", name: "胶囊圆角", variable: "--radius-pill", sample: "shape" },

    { category: "边框", name: "细线", variable: "--border-width-thin", sample: "border-width" },
    { category: "边框", name: "标准线", variable: "--border-width", sample: "border-width" },
    { category: "边框", name: "强调线", variable: "--border-width-strong", sample: "border-width" },
    { category: "边框", name: "实线", variable: "--border-style-solid", sample: "border-style" },
    { category: "边框", name: "虚线", variable: "--border-style-dashed", sample: "border-style" },
    { category: "边框", name: "焦点线", variable: "--focus-outline", sample: "outline" },

    { category: "尺寸", name: "紧凑控件", variable: "--control-height-sm", sample: "size" },
    { category: "尺寸", name: "标准控件", variable: "--control-height", sample: "size" },
    { category: "尺寸", name: "大型控件", variable: "--control-height-lg", sample: "size" },
    { category: "尺寸", name: "紧凑按钮", variable: "--button-height-sm", sample: "size" },
    { category: "尺寸", name: "标准按钮", variable: "--button-height", sample: "size" },
    { category: "尺寸", name: "大型按钮", variable: "--button-height-lg", sample: "size" },
    { category: "尺寸", name: "图标按钮", variable: "--icon-button-size", sample: "size" },
    { category: "尺寸", name: "紧凑图标按钮", variable: "--icon-button-size-sm", sample: "size" },
    { category: "尺寸", name: "小图标", variable: "--icon-size-sm", sample: "size" },
    { category: "尺寸", name: "标准图标", variable: "--icon-size", sample: "size" },
    { category: "尺寸", name: "按钮内边距", variable: "--button-padding-inline", sample: "space" },
    { category: "尺寸", name: "按钮图文间距", variable: "--button-icon-gap", sample: "space" },

    { category: "间距", name: "间距 1", variable: "--space-1", sample: "space" },
    { category: "间距", name: "间距 2", variable: "--space-2", sample: "space" },
    { category: "间距", name: "间距 3", variable: "--space-3", sample: "space" },
    { category: "间距", name: "间距 4", variable: "--space-4", sample: "space" },
    { category: "间距", name: "间距 6", variable: "--space-6", sample: "space" },
    { category: "间距", name: "间距 8", variable: "--space-8", sample: "space" },

    { category: "字体", name: "辅助文字", variable: "--font-size-xs", sample: "type" },
    { category: "字体", name: "小号正文", variable: "--font-size-sm", sample: "type" },
    { category: "字体", name: "正文", variable: "--font-size-md", sample: "type" },
    { category: "字体", name: "大号正文", variable: "--font-size-lg", sample: "type" },
    { category: "字体", name: "小标题", variable: "--font-size-xl", sample: "type" },
    { category: "字体", name: "页面标题", variable: "--font-size-2xl", sample: "type" },
    { category: "字体", name: "字体族", variable: "--font-family", sample: "family" },
    { category: "字体", name: "常规字重", variable: "--font-weight-normal", sample: "weight" },
    { category: "字体", name: "强调字重", variable: "--font-weight-semibold", sample: "weight" },
    { category: "字体", name: "紧凑行高", variable: "--line-height-tight", sample: "line-height" },
    { category: "字体", name: "标准行高", variable: "--line-height-normal", sample: "line-height" },
    { category: "字体", name: "宽松行高", variable: "--line-height-relaxed", sample: "line-height" },

    { category: "阴影", name: "弹出层", variable: "--shadow-popover", sample: "shadow" },
    { category: "阴影", name: "对话框", variable: "--shadow-dialog", sample: "shadow" },
    { category: "阴影", name: "菜单", variable: "--shadow-menu", sample: "shadow" },

    { category: "动效", name: "快速过渡", variable: "--duration-fast", sample: "motion-duration" },
    { category: "动效", name: "标准过渡", variable: "--duration-normal", sample: "motion-duration" },
    { category: "动效", name: "标准缓动", variable: "--ease-standard", sample: "motion-ease" },

    { category: "层级", name: "下拉菜单", variable: "--z-dropdown", sample: "layer" },
    { category: "层级", name: "浮层", variable: "--z-popover", sample: "layer" },
    { category: "层级", name: "对话框", variable: "--z-dialog", sample: "layer" },
    { category: "层级", name: "通知", variable: "--z-toast", sample: "layer" },
];

export const tokenCategories: TokenCategory[] = [
    "颜色",
    "圆角",
    "边框",
    "尺寸",
    "间距",
    "字体",
    "阴影",
    "动效",
    "层级",
];
