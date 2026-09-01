# IME Switch

`IME Switch` 是一个 Windows 输入法辅助工具：当前台窗口发生切换时，若该窗口使用的是中文 IME，则把该 IME 调整为英文/字母数字模式。它不会切换输入法种类，也不会修改普通英文键盘或非中文输入法。

## 功能与行为

- 使用 `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` 监听前台窗口变化，不做持续轮询。
- 延迟确认新窗口仍位于前台，避免快速切窗时修改过期目标。
- 仅处理语言标识为中文且 `ImmIsIME()` 确认是 IME 的键盘布局。
- 优先将 IME 的 OpenStatus 设为关闭状态；不可用时回退为清除 ConversionMode 中的中文和全角标志。
- 对刚激活、尚未就绪的应用最多重试三次；失败时尽量恢复原状态。
- 使用 `SendMessageTimeoutW`，避免目标应用无响应时阻塞脚本。
- 单实例运行，退出时释放 WinEvent hook 和回调资源。

## 架构

唯一源文件为 `IME_Switch.ahk`，由以下流程组成：

1. 安装前台窗口 WinEvent hook，并在启动时处理当前前台窗口。
2. 收到窗口切换事件后生成序号，延迟执行；过期事件自动失效。
3. 获取目标窗口线程的 HKL，确认其为中文 IME。
4. 获取键盘焦点窗口的默认 IME 窗口，尝试切换至英文模式。
5. 退出时卸载 hook 并释放 callback。

## 使用

要求：Windows 11、AutoHotkey v2.0 或更高版本。

双击 `IME_Switch.ahk` 运行。脚本常驻后无需额外配置；如需停止，可从 AutoHotkey 托盘图标退出该脚本。

关键调优参数位于脚本顶部：

- `SWITCH_DELAY_MS`：切换窗口后的首次处理延迟。
- `RETRY_DELAY_MS`：IME 未就绪时的重试间隔。
- `MAX_RETRIES`：最多重试次数。
- `MESSAGE_TIMEOUT`：向 IME 发送同步消息的超时。
