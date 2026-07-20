#Requires AutoHotkey v2.0
#SingleInstance Force

; 初始化全局变量
global isTiming := false
global startTime := 0
global elapsedTime := 0

; 创建 GUI 窗口 (-Caption 去除标题栏, +AlwaysOnTop 置顶, +ToolWindow 不在任务栏显示图标)
MyGui := Gui("-Caption +AlwaysOnTop +ToolWindow")
MyGui.BackColor := "1E1E1E" ; 深灰色背景

; 设置字体样式：白色、等宽字体、字号24
MyGui.SetFont("s24 cWhite bold", "Consolas")

; 添加文本控件，初始显示时间
TimerText := MyGui.Add("Text", "w150 Center", "00:00.00")

; 绑定点击事件
TimerText.OnEvent("Click", ToggleTimer)    ; 左键点击：开始/暂停
MyGui.OnEvent("ContextMenu", ResetTimer)   ; 右键点击：重置

; 先隐藏显示一次，以便获取窗口的实际宽高
MyGui.Show("Hide")
MyGui.GetPos(&gX, &gY, &gWidth, &gHeight)

; 获取主显示器的工作区域（不包含任务栏）
MonitorGetWorkArea(1, &WorkLeft, &WorkTop, &WorkRight, &WorkBottom)

; 计算右下角坐标 (距离边缘20像素)
posX := WorkRight - gWidth - 20
posY := WorkBottom - gHeight - 20

; 在右下角正式显示窗口 (NoActivate 保证不抢占当前焦点)
MyGui.Show("NoActivate x" posX " y" posY)

; ==========================================
; 核心逻辑函数
; ==========================================

; 开始 / 暂停 切换功能
ToggleTimer(*) {
    global isTiming, startTime, elapsedTime

    if (isTiming) {
        ; 暂停计时
        SetTimer(UpdateDisplay, 0) ; 关闭定时器
        isTiming := false
        UpdateDisplay() ; 最后刷新一次确保时间准确
    } else {
        ; 开始/继续计时 (利用 A_TickCount 系统开机以来的毫秒数进行精准计算)
        startTime := A_TickCount - elapsedTime
        isTiming := true
        SetTimer(UpdateDisplay, 10) ; 每10毫秒刷新一次UI
    }
}

; 刷新时间显示
UpdateDisplay(*) {
    global startTime, elapsedTime, TimerText

    if (isTiming)
        elapsedTime := A_TickCount - startTime

        ; 将总毫秒数转换为 分:秒.毫秒(两位) 的格式
        totalMs := elapsedTime
        ms := Mod(totalMs, 1000) // 10       ; 取两位数毫秒 (0-99)
        totalSecs := totalMs // 1000         ; 总秒数
        secs := Mod(totalSecs, 60)           ; 取余得到秒 (0-59)
        mins := totalSecs // 60              ; 取整得到分钟

        ; 格式化字符串并更新UI
        TimerText.Value := Format("{:02}:{:02}.{:02}", mins, secs, ms)
}

; 右键重置功能
ResetTimer(*) {
    global isTiming, elapsedTime, TimerText

    ; 只有在暂停状态下才能重置
    if (!isTiming) {
        elapsedTime := 0
        TimerText.Value := "00:00.00"
    }
}
