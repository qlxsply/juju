#Requires AutoHotkey v2.0
#SingleInstance Force
Persistent

; ============================================================
; IME Switch
;
; 每次前台窗口发生切换时：
;   1. 获取新前台窗口线程当前使用的 HKL。
;   2. 仅当该 HKL 属于中文且 ImmIsIME() 确认为 IME 时处理。
;   3. 不切换输入法种类，仅将当前中文 IME 切换到英文/字母数字模式。
;   4. 普通英文键盘、非中文输入法或无法控制的输入法保持原样。
;
; 使用 SetWinEventHook(EVENT_SYSTEM_FOREGROUND) 监听窗口切换，
; 不使用持续轮询。
; ============================================================

; ---------- Tuning ----------
SWITCH_DELAY_MS := 100
RETRY_DELAY_MS  := 120
MAX_RETRIES     := 3
MESSAGE_TIMEOUT := 200

; ---------- WinEvent constants ----------
EVENT_SYSTEM_FOREGROUND := 0x0003
WINEVENT_OUTOFCONTEXT   := 0x0000
WINEVENT_SKIPOWNPROCESS := 0x0002

; ---------- Runtime ----------
gEventSerial := 0
gCallback := 0
gHookForeground := 0

InstallHook()
OnExit(Cleanup)

; 脚本启动时，也对当前已经激活的窗口执行一次。
SetTimer(HandleStartupWindow, -250)


; ============================================================
; Initialization
; ============================================================

InstallHook()
{
    global gCallback
    global gHookForeground
    global EVENT_SYSTEM_FOREGROUND
    global WINEVENT_OUTOFCONTEXT
    global WINEVENT_SKIPOWNPROCESS

    flags := WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS

    ; WinEvent 回调可能在其他 AHK 线程运行期间到达，因此不使用 Fast 模式。
    gCallback := CallbackCreate(WinEventProc, , 7)

    gHookForeground := DllCall(
        "user32\SetWinEventHook",
        "UInt", EVENT_SYSTEM_FOREGROUND,
        "UInt", EVENT_SYSTEM_FOREGROUND,
        "Ptr", 0,
        "Ptr", gCallback,
        "UInt", 0,
        "UInt", 0,
        "UInt", flags,
        "Ptr"
    )

    if !gHookForeground {
        CallbackFree(gCallback)
        gCallback := 0
        ExitApp
    }
}


; ============================================================
; Foreground-window listener
; ============================================================

WinEventProc(hWinEventHook, event, hwnd, idObject, idChild, idEventThread, eventTime)
{
    global EVENT_SYSTEM_FOREGROUND
    global SWITCH_DELAY_MS
    global gEventSerial

    if (event != EVENT_SYSTEM_FOREGROUND || !hwnd)
        return

    if !DllCall("user32\IsWindow", "Ptr", hwnd, "Int")
        return

    ; 每次前台窗口变化都会生成新的序号。
    ; 旧窗口尚未执行的延迟/重试任务会自动失效。
    gEventSerial += 1
    serial := gEventSerial

    SetTimer(ProcessForeground.Bind(hwnd, serial, 1), -SWITCH_DELAY_MS)
}


HandleStartupWindow()
{
    global gEventSerial
    global SWITCH_DELAY_MS

    hwnd := DllCall("user32\GetForegroundWindow", "Ptr")
    if !hwnd
        return

    gEventSerial += 1
    serial := gEventSerial

    SetTimer(ProcessForeground.Bind(hwnd, serial, 1), -SWITCH_DELAY_MS)
}


; ============================================================
; Foreground processing
; ============================================================

ProcessForeground(hwnd, serial, attempt)
{
    global gEventSerial
    global MAX_RETRIES
    global RETRY_DELAY_MS

    ; 已经发生了更新的窗口切换事件。
    if (serial != gEventSerial)
        return

    ; 延迟结束后再次确认该窗口仍然位于前台。
    current := DllCall("user32\GetForegroundWindow", "Ptr")
    if (current != hwnd)
        return

    threadId := DllCall(
        "user32\GetWindowThreadProcessId",
        "Ptr", hwnd,
        "Ptr", 0,
        "UInt"
    )

    if !threadId
        return

    hkl := DllCall(
        "user32\GetKeyboardLayout",
        "UInt", threadId,
        "Ptr"
    )

    if !IsChineseIme(hkl)
        return

    ; 优先对真正拥有键盘焦点的子控件获取默认 IME 窗口。
    targetHwnd := GetFocusWindow(threadId, hwnd)

    if ForceImeEnglish(targetHwnd)
        return

    ; 某些应用或 IME 在窗口刚激活时尚未完全就绪。
    if (attempt < MAX_RETRIES) {
        SetTimer(
            ProcessForeground.Bind(hwnd, serial, attempt + 1),
            -RETRY_DELAY_MS
        )
    }
}


; ============================================================
; HKL / language detection
; ============================================================

IsChineseIme(hkl)
{
    if !hkl
        return false

    ; LOWORD(HKL) 为 LANGID。
    ; PRIMARYLANGID(langId) == 0x04 表示中文。
    langId := hkl & 0xFFFF
    primaryLangId := langId & 0x03FF

    if (primaryLangId != 0x04)
        return false

    ; 普通键盘布局不处理；只处理 Windows 识别为 IME 的布局。
    return DllCall("imm32\ImmIsIME", "Ptr", hkl, "Int") != 0
}


GetFocusWindow(threadId, fallbackHwnd)
{
    ; GUITHREADINFO:
    ; DWORD cbSize
    ; DWORD flags
    ; HWND  hwndActive
    ; HWND  hwndFocus
    ; HWND  hwndCapture
    ; HWND  hwndMenuOwner
    ; HWND  hwndMoveSize
    ; HWND  hwndCaret
    ; RECT  rcCaret
    cbSize := 8 + (6 * A_PtrSize) + 16
    gti := Buffer(cbSize, 0)
    NumPut("UInt", cbSize, gti, 0)

    ok := DllCall(
        "user32\GetGUIThreadInfo",
        "UInt", threadId,
        "Ptr", gti.Ptr,
        "Int"
    )

    if ok {
        hwndFocus := NumGet(gti, 8 + A_PtrSize, "Ptr")
        if hwndFocus
            return hwndFocus
    }

    return fallbackHwnd
}


; ============================================================
; IME control
; ============================================================

ForceImeEnglish(targetHwnd)
{
    static IMC_GETCONVERSIONMODE := 0x0001
    static IMC_SETCONVERSIONMODE := 0x0002
    static IMC_GETOPENSTATUS     := 0x0005
    static IMC_SETOPENSTATUS     := 0x0006

    static IME_CMODE_NATIVE    := 0x0001
    static IME_CMODE_FULLSHAPE := 0x0008

    imeWnd := DllCall(
        "imm32\ImmGetDefaultIMEWnd",
        "Ptr", targetHwnd,
        "Ptr"
    )

    if !imeWnd
        return false

    if !DllCall("user32\IsWindow", "Ptr", imeWnd, "Int")
        return false

    open0 := ImeControl(imeWnd, IMC_GETOPENSTATUS)
    conv0 := ImeControl(imeWnd, IMC_GETCONVERSIONMODE)

    if (!open0.Ok && !conv0.Ok)
        return false

    ; --------------------------------------------------------
    ; Strategy A: OpenStatus
    ;
    ; 对微软拼音、搜狗等常见中文 IME，OpenStatus=0 通常表示
    ; 当前 IME 保持选中，但处于英文/关闭中文转换的状态。
    ;
    ; 一旦确认 OpenStatus=0，就不再继续修改 ConversionMode。
    ; 某些 IME 的两套状态并不完全同步，同时修改反而可能触发
    ; IME/TSF 将状态恢复成中文。
    ; --------------------------------------------------------

    if open0.Ok {
        if (open0.Value = 0)
            return true

        setOpen := ImeControl(imeWnd, IMC_SETOPENSTATUS, 0)

        Sleep 15

        open1 := ImeControl(imeWnd, IMC_GETOPENSTATUS)

        if (open1.Ok && open1.Value = 0)
            return true

        ; 修改后无法确认成功时，尽量恢复原始 OpenStatus。
        if setOpen.Ok {
            ImeControl(
                imeWnd,
                IMC_SETOPENSTATUS,
                open0.Value ? 1 : 0
            )
            Sleep 10
        }
    }

    ; --------------------------------------------------------
    ; Strategy B: ConversionMode fallback
    ;
    ; 只有 OpenStatus 无法建立英文状态时才使用。
    ; 清除 NATIVE 与 FULLSHAPE，保留其他 conversion flags。
    ; --------------------------------------------------------

    convBeforeFallback := ImeControl(
        imeWnd,
        IMC_GETCONVERSIONMODE
    )

    if !convBeforeFallback.Ok
        return false

    if !(convBeforeFallback.Value & IME_CMODE_NATIVE)
        return true

    newMode := (
        convBeforeFallback.Value
        & ~(IME_CMODE_NATIVE | IME_CMODE_FULLSHAPE)
    )

    setConv := ImeControl(
        imeWnd,
        IMC_SETCONVERSIONMODE,
        newMode
    )

    Sleep 15

    conv2 := ImeControl(
        imeWnd,
        IMC_GETCONVERSIONMODE
    )

    if (conv2.Ok && !(conv2.Value & IME_CMODE_NATIVE))
        return true

    ; 无法确认成功时，尽量恢复修改前的 ConversionMode。
    if setConv.Ok {
        ImeControl(
            imeWnd,
            IMC_SETCONVERSIONMODE,
            convBeforeFallback.Value
        )
    }

    return false
}


ImeControl(imeWnd, command, param := 0)
{
    global MESSAGE_TIMEOUT

    static WM_IME_CONTROL := 0x0283
    static SMTO_ABORTIFHUNG := 0x0002

    resultBuf := Buffer(A_PtrSize, 0)

    ok := DllCall(
        "user32\SendMessageTimeoutW",
        "Ptr", imeWnd,
        "UInt", WM_IME_CONTROL,
        "UPtr", command,
        "Ptr", param,
        "UInt", SMTO_ABORTIFHUNG,
        "UInt", MESSAGE_TIMEOUT,
        "Ptr", resultBuf.Ptr,
        "Ptr"
    )

    if !ok {
        return {
            Ok: false,
            Value: 0
        }
    }

    return {
        Ok: true,
        Value: NumGet(resultBuf, 0, "UPtr")
    }
}


; ============================================================
; Cleanup
; ============================================================

Cleanup(*)
{
    global gHookForeground
    global gCallback

    if gHookForeground {
        DllCall(
            "user32\UnhookWinEvent",
            "Ptr", gHookForeground,
            "Int"
        )
        gHookForeground := 0
    }

    if gCallback {
        CallbackFree(gCallback)
        gCallback := 0
    }
}
