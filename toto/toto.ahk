#Requires AutoHotkey v2.0
#SingleInstance Off

TraySetIcon("my_icon.ico")

Persistent()
DetectHiddenWindows(true)
SetTitleMatchMode(3)

; ============================================================
; toto - 轻量待办提醒工具
; AutoHotkey v2
; ============================================================

global APP_NAME := "toto"
global APP_VERSION := "0.1.0"
global INSTANCE_TITLE := "toto.instance.3A44A2C6-9357-45CB-A8B1-9247AE39E43B"
global INSTANCE_MUTEX := "Local\toto.instance.3A44A2C6-9357-45CB-A8B1-9247AE39E43B"
global WM_TOTO_SHOW := 0x8001
global WM_POWERBROADCAST := 0x0218
global WM_TIMECHANGE := 0x001E
global WM_WTSSESSION_CHANGE := 0x02B1
global WTS_SESSION_LOCK := 0x7
global WTS_SESSION_UNLOCK := 0x8

global DATA_DIR := EnvGet("USERPROFILE") "\.toto"
global CONFIG_PATH := DATA_DIR "\config.ini"
global ING_PATH := DATA_DIR "\toto_ing.csv"
global END_PATH := DATA_DIR "\toto_end.csv"
global STARTUP_LINK := A_Startup "\toto.lnk"

global ING_HEADER := [
    "事项ID",
    "事项内容",
    "计划时间",
    "提醒时间",
    "创建时间",
    "创建序号",
    "提醒状态",
    "已提醒时间"
]

global END_HEADER := [
    "事项ID",
    "事项内容",
    "计划时间",
    "提醒时间",
    "创建时间",
    "创建序号",
    "结束状态",
    "结束时间"
]

global gConfig := Map()
global gIngItems := []
global gEndItems := []
global gNextCreatedSeq := 1
global gRegisteredHotkey := ""
global gRegisteredAppHotkeys := Map()

global gMainGui := 0
global gMainLV := 0
global gMainStatus := 0
global gMainRowIds := []

global gHistoryGui := 0
global gHistoryLV := 0
global gHistoryRowIds := []

global gEditorGui := 0
global gEditorEdit := 0
global gEditorContentEdit := 0
global gEditorPlannedEdit := 0
global gEditorReminderEdit := 0
global gEditorErrorText := 0
global gEditorItemId := ""

global gSettingsGui := 0
global gSettingsHotkey := 0
global gSettingsHotkeyValue := ""
global gSettingsWinModifier := 0
global gSettingsDefaultMinutes := 0
global gSettingsAutoStart := 0
global gSettingsAppHotkeys := Map()
global gSettingsGlobalHotkeySuspended := false

global gReminderGui := 0
global gReminderLV := 0
global gReminderRowIds := []
global gReminderQueueIds := []
global gSessionLocked := false
global gMutexHandle := 0

; ------------------------------------------------------------
; 自动执行入口
; ------------------------------------------------------------

gMutexHandle := DllCall(
    "Kernel32\CreateMutexW",
    "Ptr", 0,
    "Int", false,
    "Str", INSTANCE_MUTEX,
    "Ptr"
)
mutexAlreadyExists := (A_LastError = 183) ; ERROR_ALREADY_EXISTS

if !gMutexHandle {
    MsgBox("无法创建 toto 单实例互斥锁。", "toto", "Iconx")
    ExitApp()
}

if mutexAlreadyExists {
    ; 首实例可能仍在初始化，短暂重试查找其隐藏主窗口。
    Loop 20 {
        existingHwnd := WinExist(INSTANCE_TITLE " ahk_class AutoHotkey")
        if existingHwnd {
            try PostMessage(WM_TOTO_SHOW, 0, 0, , "ahk_id " existingHwnd)
            break
        }
        Sleep(50)
    }
    ExitApp()
}

WinSetTitle(INSTANCE_TITLE, "ahk_id " A_ScriptHwnd)
InitializeToto()

; ------------------------------------------------------------
; 初始化
; ------------------------------------------------------------

InitializeToto() {
    global WM_TOTO_SHOW, WM_POWERBROADCAST, WM_TIMECHANGE
    global WM_WTSSESSION_CHANGE, gConfig

    EnsureDataFiles()
    LoadConfig()
    LoadAllData(true)
    CreateMainGui()
    ConfigureTray()

    OnMessage(WM_TOTO_SHOW, HandleSecondInstance)
    OnMessage(WM_POWERBROADCAST, HandlePowerBroadcast)
    OnMessage(WM_TIMECHANGE, HandleTimeChange)
    OnMessage(WM_WTSSESSION_CHANGE, HandleSessionChange)
    OnMessage(0x0100, HandleSettingsHotkeyInput) ; WM_KEYDOWN
    OnMessage(0x0104, HandleSettingsHotkeyInput) ; WM_SYSKEYDOWN

    ; 接收锁屏/解锁消息。失败不影响基础功能。
    try DllCall(
        "Wtsapi32\WTSRegisterSessionNotification",
        "Ptr", A_ScriptHwnd,
        "UInt", 0
    )

    OnExit(CleanupBeforeExit)

    hotkeysReady := RegisterConfiguredHotkey()
    if !RegisterConfiguredAppHotkeys()
        hotkeysReady := false

    if !hotkeysReady {
        MsgBox(
            "一个或多个配置快捷键以及对应默认值均无法注册。"
            . "toto 仍会在托盘运行，请修改快捷键。",
            "toto",
            "Icon!"
        )
        ShowMain()
        ShowSettings()
    }

    ApplyAutoStart(gConfig["auto_start"], false)

    ProcessDueReminders()
    ScheduleNextReminder()
}

EnsureDataFiles() {
    global DATA_DIR, CONFIG_PATH, ING_PATH, END_PATH
    global ING_HEADER, END_HEADER

    if !DirExist(DATA_DIR)
        DirCreate(DATA_DIR)

    if !FileExist(CONFIG_PATH) {
        ; IniRead/IniWrite 对 Unicode INI 的可靠支持要求 UTF-16。
        file := FileOpen(CONFIG_PATH, "w", "UTF-16")
        file.Write("[General]`r`n")
        file.Write("hotkey=^!Space`r`n")
        file.Write("shortcut_add=!a`r`n")
        file.Write("shortcut_history=!q`r`n")
        file.Write("shortcut_settings=!s`r`n")
        file.Write("shortcut_refresh=!r`r`n")
        file.Write("shortcut_edit=!e`r`n")
        file.Write("shortcut_complete=!f`r`n")
        file.Write("shortcut_cancel=!c`r`n")
        file.Write("default_remind_minutes=5`r`n")
        file.Write("auto_start=0`r`n")
        file.Close()
    }

    if !FileExist(ING_PATH)
        WriteCsvAtomic(ING_PATH, ING_HEADER, [])

    if !FileExist(END_PATH)
        WriteCsvAtomic(END_PATH, END_HEADER, [])
}

LoadConfig() {
    global CONFIG_PATH, gConfig

    hotkey := NormalizeHotkey(
        IniRead(CONFIG_PATH, "General", "hotkey", "^!Space")
    )
    if (hotkey = "")
        hotkey := "^!Space"

    appHotkeys := Map()
    for definition in GetAppShortcutDefinitions() {
        configKey := definition["key"]
        shortcut := NormalizeHotkey(
            IniRead(
                CONFIG_PATH,
                "General",
                configKey,
                definition["default"]
            )
        )
        if (shortcut = "" || InStr(shortcut, "#"))
            shortcut := definition["default"]
        appHotkeys[configKey] := shortcut
    }

    minutesRaw := Trim(
        IniRead(CONFIG_PATH, "General", "default_remind_minutes", "5")
    )
    if !RegExMatch(minutesRaw, "^\d+$")
        minutesRaw := "5"

    autoStartRaw := Trim(IniRead(CONFIG_PATH, "General", "auto_start", "0"))
    autoStart := (autoStartRaw = "1") ? 1 : 0

    gConfig := Map(
        "hotkey", hotkey,
        "default_remind_minutes", minutesRaw + 0,
        "auto_start", autoStart
    )

    for configKey, shortcut in appHotkeys
        gConfig[configKey] := shortcut

    SaveConfig()
}

SaveConfig() {
    global CONFIG_PATH, gConfig

    IniWrite(gConfig["hotkey"], CONFIG_PATH, "General", "hotkey")

    for definition in GetAppShortcutDefinitions() {
        configKey := definition["key"]
        IniWrite(gConfig[configKey], CONFIG_PATH, "General", configKey)
    }

    IniWrite(
        gConfig["default_remind_minutes"],
        CONFIG_PATH,
        "General",
        "default_remind_minutes"
    )
    IniWrite(gConfig["auto_start"], CONFIG_PATH, "General", "auto_start")
}

LoadAllData(showMalformedWarning := false) {
    global gIngItems, gEndItems, gNextCreatedSeq

    ingResult := LoadIngCsv()
    endResult := LoadEndCsv()

    gIngItems := ingResult["items"]
    gEndItems := endResult["items"]

    ReconcileDuplicateItems()
    SortIngItems()
    SortEndItems()

    maxSeq := 0
    for item in gIngItems {
        if (item["createdSeq"] > maxSeq)
            maxSeq := item["createdSeq"]
    }
    for item in gEndItems {
        if (item["createdSeq"] > maxSeq)
            maxSeq := item["createdSeq"]
    }
    gNextCreatedSeq := maxSeq + 1

    malformedCount := ingResult["malformed"] + endResult["malformed"]
    if (showMalformedWarning && malformedCount > 0) {
        MsgBox(
            "读取数据时跳过了 " malformedCount
            . " 条格式异常的 CSV 记录。原文件未被删除，请检查数据文件。",
            "toto",
            "Icon!"
        )
    }
}

ReconcileDuplicateItems() {
    global gIngItems, gEndItems

    endIds := Map()
    for item in gEndItems
        endIds[item["id"]] := true

    filtered := []
    changed := false

    for item in gIngItems {
        if endIds.Has(item["id"]) {
            changed := true
            continue
        }
        filtered.Push(item)
    }

    if changed {
        gIngItems := filtered
        SaveIngItems(false)
    }
}

; ------------------------------------------------------------
; 主界面
; ------------------------------------------------------------

CreateMainGui() {
    global gMainGui, gMainLV, gMainStatus

    gMainGui := Gui("-MaximizeBox", "toto - 进行中事项")
    gMainGui.Opt("+OwnDialogs")
    gMainGui.SetFont("s10", "Microsoft YaHei UI")
    gMainGui.MarginX := 12
    gMainGui.MarginY := 12

    gMainLV := gMainGui.Add(
        "ListView",
        "x12 y12 w920 h442 Grid -Multi NoSortHdr",
        ["事项内容", "计划时间", "提醒时间", "提醒状态", "创建时间"]
    )
    gMainLV.ModifyCol(1, 345)
    gMainLV.ModifyCol(2, 155)
    gMainLV.ModifyCol(3, 155)
    gMainLV.ModifyCol(4, 100)
    gMainLV.ModifyCol(5, 150)

    gMainStatus := gMainGui.Add(
        "Text",
        "x12 y466 w920 h24",
        "应用内快捷键可在设置中调整；双击事项编辑；关闭窗口后 toto 继续在托盘运行。"
    )

    gMainLV.OnEvent("DoubleClick", MainListDoubleClick)
    gMainGui.OnEvent("Close", HideMain)
    gMainGui.OnEvent("Escape", HideMain)
}

ShowMain(*) {
    global gMainGui

    LoadAllData(false)
    ProcessDueReminders()
    RefreshMainList()
    ScheduleNextReminder()

    gMainGui.Show("w944 h502 Center")
    try WinActivate("ahk_id " gMainGui.Hwnd)
}

HideMain(*) {
    global gMainGui
    gMainGui.Hide()
}

RefreshMainFromDisk(*) {
    LoadAllData(false)
    ProcessDueReminders()
    RefreshMainList()
    ScheduleNextReminder()
}

RefreshMainList() {
    global gMainLV, gMainRowIds, gIngItems, gMainStatus, DATA_DIR

    if !IsObject(gMainLV)
        return

    SortIngItems()
    gMainRowIds := []

    gMainLV.Opt("-Redraw")
    gMainLV.Delete()

    for item in gIngItems {
        gMainLV.Add(
            "",
            item["content"],
            item["plannedAt"],
            item["remindAt"],
            item["remindStatus"],
            item["createdAt"]
        )
        gMainRowIds.Push(item["id"])
    }

    gMainLV.Opt("+Redraw")
    gMainStatus.Text := "进行中：" . gIngItems.Length
        . " 项；数据目录：" . DATA_DIR
}

MainListDoubleClick(ctrl, rowNumber) {
    global gMainRowIds

    if (rowNumber < 1 || rowNumber > gMainRowIds.Length)
        return

    ShowItemEditor(gMainRowIds[rowNumber])
}

GetSelectedIngId(showMessage := true) {
    global gMainLV, gMainRowIds

    row := gMainLV.GetNext()
    if !row {
        if showMessage
            MsgBox("请先选择一条进行中事项。", "toto", "Iconi")
        return ""
    }

    if (row > gMainRowIds.Length)
        return ""

    return gMainRowIds[row]
}

EditSelectedItem(*) {
    id := GetSelectedIngId()
    if (id != "")
        ShowItemEditor(id)
}

CompleteSelectedItem(*) {
    id := GetSelectedIngId()
    if (id != "")
        EndItemById(id, "已完成")
}

CancelSelectedItem(*) {
    id := GetSelectedIngId()
    if (id = "")
        return

    item := FindIngItemById(id)
    if !IsObject(item)
        return

    result := MsgBox(
        "确定取消以下事项吗？`n`n" item["content"],
        "toto",
        "YesNo Icon?"
    )
    if (result = "Yes")
        EndItemById(id, "已取消")
}

; ------------------------------------------------------------
; 新增与编辑
; ------------------------------------------------------------

ShowItemEditor(itemId := "") {
    global gEditorGui, gEditorEdit, gEditorContentEdit, gEditorPlannedEdit
    global gEditorReminderEdit, gEditorErrorText, gEditorItemId
    global gMainGui, gConfig

    if IsObject(gEditorGui) {
        try gEditorGui.Destroy()
    }

    gEditorEdit := 0
    gEditorContentEdit := 0
    gEditorPlannedEdit := 0
    gEditorReminderEdit := 0
    gEditorErrorText := 0

    gEditorItemId := itemId
    title := (itemId = "") ? "toto - 新增事项" : "toto - 编辑事项"
    ownerOption := IsObject(gMainGui) ? "+Owner" gMainGui.Hwnd : ""

    gEditorGui := Gui(ownerOption " +AlwaysOnTop -MaximizeBox", title)
    gEditorGui.Opt("+OwnDialogs")
    gEditorGui.SetFont("s10", "Microsoft YaHei UI")
    gEditorGui.MarginX := 14
    gEditorGui.MarginY := 12

    if (itemId = "") {
        helpText := "输入格式：事项内容[@计划时间[@提前提醒分钟数]]`n"
            . "时间支持：HHmm、ddHHmm、MMddHHmm、yyyyMMddHHmm`n"
            . "未填写提前分钟数时，使用默认值 "
            . gConfig["default_remind_minutes"] " 分钟。"

        gEditorGui.Add("Text", "x14 y12 w570 h62", helpText)
        gEditorEdit := gEditorGui.Add(
            "Edit",
            "x14 y82 w570 h28"
        )
        gEditorErrorText := gEditorGui.Add(
            "Text",
            "x14 y116 w370 h36 cRed",
            ""
        )

        btnSave := gEditorGui.Add("Button", "x398 y156 w88 h30 Default", "保存")
        btnCancel := gEditorGui.Add("Button", "x496 y156 w88 h30", "取消")

        btnSave.OnEvent("Click", SaveEditorItem)
        btnCancel.OnEvent("Click", CloseEditor)
        gEditorGui.OnEvent("Close", CloseEditor)
        gEditorGui.OnEvent("Escape", CloseEditor)

        gEditorGui.Show("w600 h202 Center")
        FocusControlIfAlive(gEditorEdit)
        return
    }

    helpText := "事项内容可直接包含 @。时间格式：yyyy-MM-dd HH:mm:ss；"
        . "计划时间和提醒时间均可留空，提醒时间不会随计划时间自动联动。"

    gEditorGui.Add("Text", "x14 y12 w610 h22", helpText)

    initialContent := ""
    initialPlannedAt := ""
    initialReminderAt := ""
    if (itemId != "") {
        item := FindIngItemById(itemId)
        if !IsObject(item) {
            MsgBox("事项不存在，可能已被完成或取消。", "toto", "Icon!")
            gEditorGui.Destroy()
            gEditorGui := 0
            return
        }
        initialContent := item["content"]
        initialPlannedAt := item["plannedAt"]
        initialReminderAt := item["remindAt"]
    }

    gEditorGui.Add("Text", "x14 y48 w72 h24", "事项内容：")
    gEditorContentEdit := gEditorGui.Add(
        "Edit",
        "x92 y44 w532 h28",
        initialContent
    )

    gEditorGui.Add("Text", "x14 y88 w72 h24", "计划时间：")
    gEditorPlannedEdit := gEditorGui.Add(
        "Edit",
        "x92 y84 w240 h28",
        initialPlannedAt
    )

    gEditorGui.Add("Text", "x14 y128 w72 h24", "提醒时间：")
    gEditorReminderEdit := gEditorGui.Add(
        "Edit",
        "x92 y124 w240 h28",
        initialReminderAt
    )
    gEditorErrorText := gEditorGui.Add(
        "Text",
        "x14 y158 w410 h36 cRed",
        ""
    )

    btnSave := gEditorGui.Add("Button", "x438 y194 w88 h30 Default", "保存")
    btnCancel := gEditorGui.Add("Button", "x536 y194 w88 h30", "取消")

    btnSave.OnEvent("Click", SaveEditorItem)
    btnCancel.OnEvent("Click", CloseEditor)
    gEditorGui.OnEvent("Close", CloseEditor)
    gEditorGui.OnEvent("Escape", CloseEditor)

    gEditorGui.Show("w640 h238 Center")
    FocusControlIfAlive(gEditorContentEdit)
}

SaveEditorItem(*) {
    global gEditorEdit, gEditorContentEdit, gEditorPlannedEdit
    global gEditorReminderEdit, gEditorItemId, gIngItems, gNextCreatedSeq

    if (gEditorItemId = "") {
        rawInput := Trim(gEditorEdit.Value)
        parsed := ParseItemInput(rawInput)
    } else {
        contentRaw := Trim(gEditorContentEdit.Value)
        plannedRaw := Trim(gEditorPlannedEdit.Value)
        remindRaw := Trim(gEditorReminderEdit.Value)
        parsed := ParseEditorInput(contentRaw, plannedRaw, remindRaw)
    }

    if !parsed["ok"] {
        SetEditorError(parsed["error"])
        if (gEditorItemId = "")
            FocusControlIfAlive(gEditorEdit)
        else
            FocusEditorField(parsed["focusTarget"])
        return
    }

    SetEditorError()

    currentDisplay := NowDisplay()

    if (gEditorItemId = "") {
        item := Map(
            "id", NewGuid(),
            "content", parsed["content"],
            "plannedAt", parsed["plannedAt"],
            "remindAt", parsed["remindAt"],
            "createdAt", currentDisplay,
            "createdSeq", gNextCreatedSeq,
            "remindStatus", parsed["remindAt"] = "" ? "无提醒" : "未提醒",
            "remindedAt", ""
        )
        gNextCreatedSeq += 1
        gIngItems.Push(item)
    } else {
        index := FindIngIndexById(gEditorItemId)
        if !index {
            MsgBox("事项不存在，可能已被完成或取消。", "toto", "Icon!")
            CloseEditor()
            return
        }

        item := gIngItems[index]
        reminderChanged := (item["remindAt"] != parsed["remindAt"])

        item["content"] := parsed["content"]
        item["plannedAt"] := parsed["plannedAt"]
        item["remindAt"] := parsed["remindAt"]

        if (parsed["remindAt"] = "") {
            item["remindStatus"] := "无提醒"
            item["remindedAt"] := ""
        } else if reminderChanged {
            item["remindStatus"] := "未提醒"
            item["remindedAt"] := ""
        }
    }

    SortIngItems()

    if !SaveIngItems() {
        LoadAllData(false)
        return
    }

    CloseEditor()
    RefreshMainList()
    ProcessDueReminders()
    ScheduleNextReminder()
}

CloseEditor(*) {
    global gEditorGui, gEditorEdit, gEditorContentEdit, gEditorPlannedEdit
    global gEditorReminderEdit, gEditorErrorText, gEditorItemId

    if IsObject(gEditorGui) {
        try gEditorGui.Destroy()
    }
    gEditorGui := 0
    gEditorEdit := 0
    gEditorContentEdit := 0
    gEditorPlannedEdit := 0
    gEditorReminderEdit := 0
    gEditorErrorText := 0
    gEditorItemId := ""
}

ParseItemInput(rawInput) {
    global gConfig

    if (rawInput = "")
        return ParseError("事项内容不能为空。")

    if InStr(rawInput, "`n") || InStr(rawInput, "`r")
        return ParseError("事项内容不能包含换行。")

    parts := StrSplit(rawInput, "@")
    if (parts.Length > 3)
        return ParseError("事项内容不能包含 @，且输入最多包含两个 @ 分隔符。")

    content := Trim(parts[1])
    if (content = "")
        return ParseError("事项内容不能为空。")

    if (parts.Length = 1) {
        return Map(
            "ok", true,
            "content", content,
            "plannedAt", "",
            "remindAt", ""
        )
    }

    planRaw := Trim(parts[2])
    if (planRaw = "")
        return ParseError("设置提前提醒前必须先设置计划时间。")

    planResult := ParsePlanTime(planRaw)
    if !planResult["ok"]
        return planResult

    if (parts.Length = 2) {
        remindMinutes := gConfig["default_remind_minutes"]
    } else {
        minutesRaw := Trim(parts[3])
        if (minutesRaw = "")
            return ParseError("提前提醒分钟数不能为空。")

        if !RegExMatch(minutesRaw, "^\d+$")
            return ParseError("提前提醒分钟数必须是非负整数。")

        remindMinutes := minutesRaw + 0
    }

    planStamp := planResult["stamp"]

    try remindStamp := DateAdd(planStamp, -remindMinutes, "Minutes")
    catch {
        return ParseError("提前提醒分钟数过大，无法计算有效的提醒时间。")
    }

    return Map(
        "ok", true,
        "content", content,
        "plannedAt", StampToDisplay(planStamp),
        "remindAt", StampToDisplay(remindStamp)
    )
}

ParseEditorInput(contentRaw, plannedRaw, remindRaw) {
    if (contentRaw = "")
        return ParseError("事项内容不能为空。", "content")

    if InStr(contentRaw, "`n") || InStr(contentRaw, "`r")
        return ParseError("事项内容不能包含换行。", "content")

    plannedAt := ""
    if (plannedRaw != "") {
        planResult := ParseEditorDateTime(plannedRaw, "计划时间", "plannedAt")
        if !planResult["ok"]
            return planResult
        plannedAt := planResult["display"]
    }

    remindAt := ""
    if (remindRaw != "") {
        remindResult := ParseEditorDateTime(remindRaw, "提醒时间", "remindAt")
        if !remindResult["ok"]
            return remindResult
        remindAt := remindResult["display"]
    }

    return Map(
        "ok", true,
        "content", contentRaw,
        "plannedAt", plannedAt,
        "remindAt", remindAt
    )
}

ParseEditorDateTime(raw, fieldLabel, focusTarget) {
    if !RegExMatch(raw, "^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$") {
        return ParseError(
            fieldLabel . "格式必须是 yyyy-MM-dd HH:mm:ss。",
            focusTarget
        )
    }

    stamp := DisplayToStamp(raw)
    if (stamp = "")
        return ParseError(fieldLabel . "不是有效的日期或时间。", focusTarget)

    return Map("ok", true, "stamp", stamp, "display", StampToDisplay(stamp))
}

ParsePlanTime(raw) {
    if !RegExMatch(raw, "^\d+$")
        return ParseError("计划时间只能包含数字。")

    length := StrLen(raw)
    now := A_Now
    year := SubStr(now, 1, 4) + 0
    month := SubStr(now, 5, 2) + 0
    day := SubStr(now, 7, 2) + 0
    hour := 0
    minute := 0

    switch length {
        case 4:
            hour := SubStr(raw, 1, 2) + 0
            minute := SubStr(raw, 3, 2) + 0

        case 6:
            day := SubStr(raw, 1, 2) + 0
            hour := SubStr(raw, 3, 2) + 0
            minute := SubStr(raw, 5, 2) + 0

        case 8:
            month := SubStr(raw, 1, 2) + 0
            day := SubStr(raw, 3, 2) + 0
            hour := SubStr(raw, 5, 2) + 0
            minute := SubStr(raw, 7, 2) + 0

        case 12:
            year := SubStr(raw, 1, 4) + 0
            month := SubStr(raw, 5, 2) + 0
            day := SubStr(raw, 7, 2) + 0
            hour := SubStr(raw, 9, 2) + 0
            minute := SubStr(raw, 11, 2) + 0

        default:
            return ParseError(
                "计划时间长度必须是 4、6、8 或 12 位："
                . "HHmm、ddHHmm、MMddHHmm、yyyyMMddHHmm。"
            )
    }

    if !IsValidDateTime(year, month, day, hour, minute)
        return ParseError("计划时间不是有效的日期或时间。")

    stamp := Format(
        "{:04}{:02}{:02}{:02}{:02}00",
        year,
        month,
        day,
        hour,
        minute
    )

    if (stamp <= A_Now)
        return ParseError(
            "计划时间已经过去。相对格式不会自动滚动到明天、下月或下一年。"
        )

    return Map("ok", true, "stamp", stamp)
}

ParseError(message, focusTarget := "") {
    return Map("ok", false, "error", message, "focusTarget", focusTarget)
}

IsValidDateTime(year, month, day, hour, minute) {
    if (year < 1601 || year > 9999)
        return false
    if (month < 1 || month > 12)
        return false
    if (hour < 0 || hour > 23)
        return false
    if (minute < 0 || minute > 59)
        return false

    maxDay := DaysInMonth(year, month)
    return day >= 1 && day <= maxDay
}

DaysInMonth(year, month) {
    static days := [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]

    if (month = 2 && IsLeapYear(year))
        return 29

    return days[month]
}

IsLeapYear(year) {
    return Mod(year, 400) = 0
        || (Mod(year, 4) = 0 && Mod(year, 100) != 0)
}

; ------------------------------------------------------------
; 完成、取消和历史事项
; ------------------------------------------------------------

EndItemById(id, endStatus) {
    global gIngItems, gEndItems

    index := FindIngIndexById(id)
    if !index {
        MsgBox("事项不存在，可能已被其他操作处理。", "toto", "Icon!")
        return false
    }

    item := gIngItems[index]
    endItem := Map(
        "id", item["id"],
        "content", item["content"],
        "plannedAt", item["plannedAt"],
        "remindAt", item["remindAt"],
        "createdAt", item["createdAt"],
        "createdSeq", item["createdSeq"],
        "endStatus", endStatus,
        "endedAt", NowDisplay()
    )

    ; 先写历史，再删除进行中记录。若中途失败，下一次读取会按 ID 去重，
    ; 优先保留历史记录，避免事项永久丢失。
    gEndItems.Push(endItem)
    SortEndItems()

    if !SaveEndItems() {
        LoadAllData(false)
        return false
    }

    gIngItems.RemoveAt(index)
    SortIngItems()

    if !SaveIngItems() {
        LoadAllData(false)
        return false
    }

    RemoveReminderQueueId(id)
    RefreshMainList()
    RefreshHistoryList()
    RefreshReminderWindow()
    ScheduleNextReminder()

    return true
}

ShowHistory(*) {
    global gHistoryGui, gHistoryLV, gMainGui

    LoadAllData(false)

    if IsObject(gHistoryGui) {
        try gHistoryGui.Destroy()
    }

    gHistoryGui := Gui(
        "+Owner" gMainGui.Hwnd " -MaximizeBox",
        "toto - 历史事项"
    )
    gHistoryGui.Opt("+OwnDialogs")
    gHistoryGui.SetFont("s10", "Microsoft YaHei UI")
    gHistoryGui.MarginX := 12
    gHistoryGui.MarginY := 12

    gHistoryLV := gHistoryGui.Add(
        "ListView",
        "x12 y12 w996 h410 Grid -Multi NoSortHdr",
        ["事项内容", "计划时间", "提醒时间", "结束状态", "结束时间", "创建时间"]
    )
    gHistoryLV.ModifyCol(1, 290)
    gHistoryLV.ModifyCol(2, 150)
    gHistoryLV.ModifyCol(3, 150)
    gHistoryLV.ModifyCol(4, 90)
    gHistoryLV.ModifyCol(5, 150)
    gHistoryLV.ModifyCol(6, 150)

    btnRefresh := gHistoryGui.Add("Button", "x812 y436 w86 h30", "刷新")
    btnClose := gHistoryGui.Add("Button", "x910 y436 w86 h30", "关闭")

    btnRefresh.OnEvent("Click", RefreshHistoryFromDisk)
    btnClose.OnEvent("Click", CloseHistory)
    gHistoryGui.OnEvent("Close", CloseHistory)
    gHistoryGui.OnEvent("Escape", CloseHistory)

    RefreshHistoryList()
    gHistoryGui.Show("w1020 h480 Center")
}

RefreshHistoryFromDisk(*) {
    LoadAllData(false)
    RefreshHistoryList()
}

RefreshHistoryList() {
    global gHistoryGui, gHistoryLV, gHistoryRowIds, gEndItems

    if !IsObject(gHistoryGui) || !IsObject(gHistoryLV)
        return

    SortEndItems()
    gHistoryRowIds := []

    gHistoryLV.Opt("-Redraw")
    gHistoryLV.Delete()

    for item in gEndItems {
        gHistoryLV.Add(
            "",
            item["content"],
            item["plannedAt"],
            item["remindAt"],
            item["endStatus"],
            item["endedAt"],
            item["createdAt"]
        )
        gHistoryRowIds.Push(item["id"])
    }

    gHistoryLV.Opt("+Redraw")
}

CloseHistory(*) {
    global gHistoryGui

    if IsObject(gHistoryGui) {
        try gHistoryGui.Destroy()
    }
    gHistoryGui := 0
}

; ------------------------------------------------------------
; 设置
; ------------------------------------------------------------

ShowSettings(*) {
    global gSettingsGui, gSettingsHotkey, gSettingsWinModifier
    global gSettingsHotkeyValue, gSettingsDefaultMinutes, gSettingsAutoStart
    global gSettingsAppHotkeys, gSettingsGlobalHotkeySuspended
    global gMainGui, gConfig, gRegisteredHotkey

    if IsObject(gSettingsGui) {
        try gSettingsGui.Destroy()
    }

    if (!gSettingsGlobalHotkeySuspended && gRegisteredHotkey != "") {
        HotIf()
        try {
            Hotkey(gRegisteredHotkey, "Off")
            gSettingsGlobalHotkeySuspended := true
        }
    }

    gSettingsGui := Gui(
        "+Owner" gMainGui.Hwnd " +AlwaysOnTop -MaximizeBox",
        "toto - 设置"
    )
    gSettingsGui.Opt("+OwnDialogs")
    gSettingsGui.SetFont("s10", "Microsoft YaHei UI")
    gSettingsGui.MarginX := 14
    gSettingsGui.MarginY := 12

    gSettingsGui.Add("Text", "x14 y16 w150 h24", "全局唤醒快捷键：")

    hotkeyValue := gConfig["hotkey"]
    hasWin := InStr(hotkeyValue, "#") ? 1 : 0
    hotkeyValue := StrReplace(hotkeyValue, "#", "")
    gSettingsHotkeyValue := hotkeyValue

    gSettingsHotkey := gSettingsGui.Add(
        "Edit",
        "x170 y12 w210 h28 ReadOnly",
        FormatHotkeyForDisplay(hotkeyValue)
    )
    DisableImeForControl(gSettingsHotkey)
    gSettingsWinModifier := gSettingsGui.Add(
        "CheckBox",
        "x392 y16 w100 h24",
        "包含 Win"
    )
    gSettingsWinModifier.Value := hasWin

    gSettingsGui.Add(
        "Text",
        "x14 y58 w150 h24",
        "默认提前提醒分钟："
    )
    gSettingsDefaultMinutes := gSettingsGui.Add(
        "Edit",
        "x170 y54 w120 h28 Number",
        gConfig["default_remind_minutes"]
    )

    gSettingsAutoStart := gSettingsGui.Add(
        "CheckBox",
        "x14 y98 w260 h24",
        "登录 Windows 后自动启动 toto"
    )
    gSettingsAutoStart.Value := gConfig["auto_start"]

    gSettingsGui.Add(
        "Text",
        "x14 y138 w490 h24",
        "主窗口应用内快捷键（仅主窗口激活时生效）："
    )

    gSettingsAppHotkeys := Map()
    positions := [
        [14, 170, 72, "新增："],
        [274, 170, 332, "历史："],
        [14, 208, 72, "设置："],
        [274, 208, 332, "刷新："],
        [14, 246, 72, "编辑："],
        [274, 246, 332, "完成："],
        [14, 284, 72, "取消："]
    ]

    definitions := GetAppShortcutDefinitions()
    for index, definition in definitions {
        position := positions[index]
        gSettingsGui.Add(
            "Text",
            "x" position[1] " y" (position[2] + 4) " w54 h24",
            position[4]
        )
        gSettingsAppHotkeys[definition["key"]] := gSettingsGui.Add(
            "Hotkey",
            "x" position[3] " y" position[2] " w172 h28 Limit1",
            gConfig[definition["key"]]
        )
    }

    gSettingsGui.Add(
        "Text",
        "x14 y324 w490 h42",
        "说明：全局唤醒快捷键可增加 Win；应用内快捷键支持 Ctrl、Alt、Shift，"
        . "且七项不能重复。`n新设置保存后立即生效，不需要重启。"
    )

    btnSave := gSettingsGui.Add("Button", "x318 y378 w88 h30 Default", "保存")
    btnCancel := gSettingsGui.Add("Button", "x416 y378 w88 h30", "取消")

    btnSave.OnEvent("Click", SaveSettings)
    btnCancel.OnEvent("Click", CloseSettings)
    gSettingsGui.OnEvent("Close", CloseSettings)
    gSettingsGui.OnEvent("Escape", CloseSettings)

    gSettingsGui.Show("w520 h422 Center")
}

SaveSettings(*) {
    global gSettingsHotkey, gSettingsWinModifier
    global gSettingsHotkeyValue, gSettingsDefaultMinutes, gSettingsAutoStart
    global gSettingsAppHotkeys
    global gConfig

    baseHotkey := NormalizeHotkey(gSettingsHotkeyValue)
    if (baseHotkey = "") {
        MsgBox("请选择一个全局唤醒快捷键。", "toto - 设置", "Icon!")
        return
    }

    newHotkey := gSettingsWinModifier.Value ? "#" baseHotkey : baseHotkey
    newHotkey := NormalizeHotkey(newHotkey)

    newAppHotkeys := Map()
    for definition in GetAppShortcutDefinitions() {
        configKey := definition["key"]
        shortcut := NormalizeHotkey(gSettingsAppHotkeys[configKey].Value)
        if (shortcut = "") {
            MsgBox(
                "请选择“" definition["label"] "”的应用内快捷键。",
                "toto - 设置",
                "Icon!"
            )
            return
        }
        newAppHotkeys[configKey] := shortcut
    }

    if !ValidateAppHotkeys(newAppHotkeys, true)
        return

    minutesRaw := Trim(gSettingsDefaultMinutes.Value)
    if !RegExMatch(minutesRaw, "^\d+$") {
        MsgBox("默认提前提醒分钟数必须是非负整数。", "toto - 设置", "Icon!")
        return
    }

    newAutoStart := gSettingsAutoStart.Value ? 1 : 0

    oldHotkey := gConfig["hotkey"]
    oldAutoStart := gConfig["auto_start"]

    ; 先处理开机启动；若快捷键注册失败，恢复原开机启动状态。
    if !ApplyAutoStart(newAutoStart, true)
        return

    if !RegisterGlobalHotkey(newHotkey, true) {
        ApplyAutoStart(oldAutoStart, false)
        return
    }

    if !RegisterAppHotkeys(newAppHotkeys, true) {
        RegisterGlobalHotkey(oldHotkey, false)
        ApplyAutoStart(oldAutoStart, false)
        return
    }

    gConfig["hotkey"] := newHotkey
    for configKey, shortcut in newAppHotkeys
        gConfig[configKey] := shortcut
    gConfig["default_remind_minutes"] := minutesRaw + 0
    gConfig["auto_start"] := newAutoStart
    SaveConfig()

    CloseSettings()
    MsgBox("设置已保存并立即生效。", "toto", "Iconi")
}

CloseSettings(*) {
    global gSettingsGui, gSettingsHotkeyValue, gSettingsAppHotkeys
    global gSettingsGlobalHotkeySuspended, gRegisteredHotkey

    if IsObject(gSettingsGui) {
        try gSettingsGui.Destroy()
    }

    if (gSettingsGlobalHotkeySuspended && gRegisteredHotkey != "") {
        HotIf()
        try Hotkey(gRegisteredHotkey, "On")
    }

    gSettingsGlobalHotkeySuspended := false
    gSettingsGui := 0
    gSettingsHotkeyValue := ""
    gSettingsAppHotkeys := Map()
}

GetAppShortcutDefinitions() {
    return [
        Map(
            "key", "shortcut_add",
            "label", "新增",
            "default", "!a",
            "callback", OnAppShortcutAdd
        ),
        Map(
            "key", "shortcut_history",
            "label", "历史",
            "default", "!q",
            "callback", OnAppShortcutHistory
        ),
        Map(
            "key", "shortcut_settings",
            "label", "设置",
            "default", "!s",
            "callback", OnAppShortcutSettings
        ),
        Map(
            "key", "shortcut_refresh",
            "label", "刷新",
            "default", "!r",
            "callback", OnAppShortcutRefresh
        ),
        Map(
            "key", "shortcut_edit",
            "label", "编辑",
            "default", "!e",
            "callback", OnAppShortcutEdit
        ),
        Map(
            "key", "shortcut_complete",
            "label", "完成",
            "default", "!f",
            "callback", OnAppShortcutComplete
        ),
        Map(
            "key", "shortcut_cancel",
            "label", "取消",
            "default", "!c",
            "callback", OnAppShortcutCancel
        )
    ]
}

GetConfiguredAppHotkeys() {
    global gConfig

    hotkeys := Map()
    for definition in GetAppShortcutDefinitions()
        hotkeys[definition["key"]] := gConfig[definition["key"]]
    return hotkeys
}

GetDefaultAppHotkeys() {
    hotkeys := Map()
    for definition in GetAppShortcutDefinitions()
        hotkeys[definition["key"]] := definition["default"]
    return hotkeys
}

ValidateAppHotkeys(hotkeys, showError := true) {
    seen := Map()

    for definition in GetAppShortcutDefinitions() {
        configKey := definition["key"]
        shortcut := NormalizeHotkey(
            hotkeys.Has(configKey) ? hotkeys[configKey] : ""
        )

        if (shortcut = "") {
            if showError {
                MsgBox(
                    "“" definition["label"] "”的应用内快捷键不能为空。",
                    "toto - 设置",
                    "Icon!"
                )
            }
            return false
        }

        if InStr(shortcut, "#") {
            if showError {
                MsgBox(
                    "应用内快捷键不支持 Win 修饰键："
                    . definition["label"] " = " shortcut,
                    "toto - 设置",
                    "Icon!"
                )
            }
            return false
        }

        canonical := StrLower(shortcut)
        if seen.Has(canonical) {
            if showError {
                MsgBox(
                    "应用内快捷键不能重复：`n"
                    . seen[canonical] " 与 " definition["label"]
                    . " 均设置为 " shortcut,
                    "toto - 设置",
                    "Icon!"
                )
            }
            return false
        }

        seen[canonical] := definition["label"]
    }

    return true
}

HotkeyMapContainsValue(hotkeys, targetHotkey) {
    targetCanonical := StrLower(targetHotkey)

    for _, shortcut in hotkeys {
        if (StrLower(shortcut) = targetCanonical)
            return true
    }

    return false
}

RegisterConfiguredAppHotkeys() {
    global gConfig

    configuredHotkeys := GetConfiguredAppHotkeys()
    if RegisterAppHotkeys(configuredHotkeys, false)
        return true

    defaultHotkeys := GetDefaultAppHotkeys()
    if RegisterAppHotkeys(defaultHotkeys, false) {
        for configKey, shortcut in defaultHotkeys
            gConfig[configKey] := shortcut
        SaveConfig()
        MsgBox(
            "配置的应用内快捷键无效或无法注册，已恢复默认值。",
            "toto",
            "Icon!"
        )
        return true
    }

    return false
}

RegisterAppHotkeys(newHotkeys, showError := true) {
    global gRegisteredAppHotkeys, gMainGui

    if !IsObject(gMainGui) {
        if showError
            MsgBox("主窗口尚未创建，无法注册应用内快捷键。", "toto", "Icon!")
        return false
    }

    normalizedHotkeys := Map()
    for definition in GetAppShortcutDefinitions() {
        configKey := definition["key"]
        normalizedHotkeys[configKey] := NormalizeHotkey(
            newHotkeys.Has(configKey) ? newHotkeys[configKey] : ""
        )
    }

    if !ValidateAppHotkeys(normalizedHotkeys, showError)
        return false

    oldHotkeys := Map()
    for configKey, shortcut in gRegisteredAppHotkeys
        oldHotkeys[configKey] := shortcut

    currentLabel := ""
    currentConfigKey := ""
    HotIfWinActive("ahk_id " gMainGui.Hwnd)

    try {
        for definition in GetAppShortcutDefinitions() {
            currentLabel := definition["label"]
            currentConfigKey := definition["key"]
            configKey := currentConfigKey
            Hotkey(
                normalizedHotkeys[configKey],
                definition["callback"],
                "On"
            )
        }

        for _, oldShortcut in oldHotkeys {
            if !HotkeyMapContainsValue(normalizedHotkeys, oldShortcut)
                Hotkey(oldShortcut, "Off")
        }
    } catch as err {
        ; 尽量恢复原来的七个应用内快捷键。
        for definition in GetAppShortcutDefinitions() {
            configKey := definition["key"]
            if oldHotkeys.Has(configKey) {
                try Hotkey(
                    oldHotkeys[configKey],
                    definition["callback"],
                    "On"
                )
            }
        }

        for _, newShortcut in normalizedHotkeys {
            if !HotkeyMapContainsValue(oldHotkeys, newShortcut) {
                try Hotkey(newShortcut, "Off")
            }
        }

        HotIfWinActive()

        if showError {
            MsgBox(
                "应用内快捷键注册失败："
                . currentLabel " = "
                . (currentConfigKey = ""
                    ? ""
                    : normalizedHotkeys[currentConfigKey])
                . "`n`n" err.Message,
                "toto",
                "Icon!"
            )
        }
        return false
    }

    HotIfWinActive()
    gRegisteredAppHotkeys := normalizedHotkeys
    return true
}

OnAppShortcutAdd(*) {
    ShowItemEditor()
}

OnAppShortcutHistory(*) {
    ShowHistory()
}

OnAppShortcutSettings(*) {
    ShowSettings()
}

OnAppShortcutRefresh(*) {
    RefreshMainFromDisk()
}

OnAppShortcutEdit(*) {
    EditSelectedItem()
}

OnAppShortcutComplete(*) {
    CompleteSelectedItem()
}

OnAppShortcutCancel(*) {
    CancelSelectedItem()
}

RegisterConfiguredHotkey() {
    global gConfig

    configuredHotkey := gConfig["hotkey"]
    if RegisterGlobalHotkey(configuredHotkey, false)
        return true

    defaultHotkey := "^!Space"
    if (configuredHotkey != defaultHotkey
        && RegisterGlobalHotkey(defaultHotkey, false)) {
        gConfig["hotkey"] := defaultHotkey
        SaveConfig()
        MsgBox(
            "配置的全局快捷键无效或无法注册，已回退为 Ctrl+Alt+Space。",
            "toto",
            "Icon!"
        )
        return true
    }

    return false
}

RegisterGlobalHotkey(newHotkey, showError := true) {
    global gRegisteredHotkey

    ; 明确使用全局热键上下文，避免从应用内热键线程继承条件。
    HotIf()

    newHotkey := NormalizeHotkey(newHotkey)
    if (newHotkey = "") {
        if showError
            MsgBox("快捷键不能为空。", "toto", "Icon!")
        return false
    }

    if (newHotkey = gRegisteredHotkey) {
        try {
            Hotkey(newHotkey, "On")
            return true
        } catch as err {
            if showError
                MsgBox("快捷键启用失败：`n" err.Message, "toto", "Icon!")
            return false
        }
    }

    try {
        Hotkey(newHotkey, OnGlobalHotkey, "On")
    } catch as err {
        if showError {
            MsgBox(
                "快捷键无效或无法注册：`n" newHotkey
                . "`n`n" err.Message,
                "toto",
                "Icon!"
            )
        }
        return false
    }

    oldHotkey := gRegisteredHotkey
    if (oldHotkey != "") {
        try {
            Hotkey(oldHotkey, "Off")
        } catch as err {
            try Hotkey(newHotkey, "Off")
            if showError {
                MsgBox(
                    "无法停用旧快捷键，设置未更改：`n"
                    . err.Message,
                    "toto",
                    "Icon!"
                )
            }
            return false
        }
    }

    gRegisteredHotkey := newHotkey
    return true
}

OnGlobalHotkey(*) {
    ShowMain()
}

ApplyAutoStart(enabled, showError := true) {
    global STARTUP_LINK

    try {
        if enabled {
            if A_IsCompiled {
                target := A_ScriptFullPath
                args := ""
            } else {
                target := A_AhkPath
                args := Chr(34) A_ScriptFullPath Chr(34)
            }

            FileCreateShortcut(
                target,
                STARTUP_LINK,
                A_ScriptDir,
                args,
                "toto 待办提醒工具"
            )
        } else if FileExist(STARTUP_LINK) {
            FileDelete(STARTUP_LINK)
        }
        return true
    } catch as err {
        if showError {
            MsgBox(
                "设置开机启动失败：`n" err.Message,
                "toto",
                "Icon!"
            )
        }
        return false
    }
}

; ------------------------------------------------------------
; 提醒调度
; ------------------------------------------------------------

ScheduleNextReminder() {
    global gIngItems, gSessionLocked

    SetTimer(ReminderTimerTick, 0)

    if gSessionLocked
        return

    nextStamp := ""
    for item in gIngItems {
        if (item["remindStatus"] != "未提醒")
            continue
        if (item["remindAt"] = "")
            continue

        stamp := DisplayToStamp(item["remindAt"])
        if (stamp = "")
            continue

        if (nextStamp = "" || stamp < nextStamp)
            nextStamp := stamp
    }

    if (nextStamp = "")
        return

    seconds := DateDiff(nextStamp, A_Now, "Seconds")
    if (seconds <= 0) {
        ; 成功处理后立即计算下一条；持久化失败时一分钟后重试。
        if ProcessDueReminders()
            ScheduleNextReminder()
        else
            SetTimer(ReminderTimerTick, -60000)
        return
    }

    ; 每次最多等待 6 小时，以便低频校准系统时间变化。
    delayMs := Min(seconds * 1000, 21600000)
    delayMs := Max(delayMs, 1000)
    SetTimer(ReminderTimerTick, -delayMs)
}

ReminderTimerTick(*) {
    ProcessDueReminders()
    ScheduleNextReminder()
}

ProcessDueReminders() {
    global gIngItems, gSessionLocked

    ; 返回 true 表示无需重试，false 表示稍后应重试。
    ; 锁屏期间不把事项标记为已提醒，解锁后再补发。
    if gSessionLocked
        return false

    dueItems := []
    nowStamp := A_Now
    nowDisplay := StampToDisplay(nowStamp)

    for item in gIngItems {
        if (item["remindStatus"] != "未提醒")
            continue
        if (item["remindAt"] = "")
            continue

        remindStamp := DisplayToStamp(item["remindAt"])
        if (remindStamp != "" && remindStamp <= nowStamp) {
            item["remindStatus"] := "已提醒"
            item["remindedAt"] := nowDisplay
            dueItems.Push(item)
        }
    }

    if (dueItems.Length = 0)
        return true

    ; 先持久化“已提醒”，再显示窗口，避免异常退出后重复提醒。
    if !SaveIngItems() {
        for item in dueItems {
            item["remindStatus"] := "未提醒"
            item["remindedAt"] := ""
        }
        return false
    }

    RefreshMainList()
    AddReminderQueue(dueItems)
    ShowReminderWindow()
    return true
}

AddReminderQueue(items) {
    global gReminderQueueIds

    existing := Map()
    for id in gReminderQueueIds
        existing[id] := true

    for item in items {
        if !existing.Has(item["id"]) {
            gReminderQueueIds.Push(item["id"])
            existing[item["id"]] := true
        }
    }
}

RemoveReminderQueueId(id) {
    global gReminderQueueIds

    newQueue := []
    for queuedId in gReminderQueueIds {
        if (queuedId != id)
            newQueue.Push(queuedId)
    }
    gReminderQueueIds := newQueue
}

ShowReminderWindow() {
    global gReminderGui, gReminderLV, gReminderRowIds, gReminderQueueIds

    validIds := []
    for id in gReminderQueueIds {
        if IsObject(FindIngItemById(id))
            validIds.Push(id)
    }
    gReminderQueueIds := validIds

    if (gReminderQueueIds.Length = 0) {
        CloseReminderWindow()
        return
    }

    if IsObject(gReminderGui) {
        try gReminderGui.Destroy()
    }

    gReminderGui := Gui(
        "+AlwaysOnTop -MaximizeBox",
        "toto 提醒"
    )
    gReminderGui.Opt("+OwnDialogs")
    gReminderGui.SetFont("s10", "Microsoft YaHei UI")
    gReminderGui.MarginX := 14
    gReminderGui.MarginY := 12

    gReminderGui.Add(
        "Text",
        "x14 y12 w612 h42",
        "以下事项已到达提醒时间。窗口不会自动关闭；关闭窗口不会完成事项。"
    )

    gReminderLV := gReminderGui.Add(
        "ListView",
        "x14 y56 w612 h238 Grid -Multi NoSortHdr",
        ["事项内容", "计划时间", "提醒时间"]
    )
    gReminderLV.ModifyCol(1, 305)
    gReminderLV.ModifyCol(2, 145)
    gReminderLV.ModifyCol(3, 145)

    btnComplete := gReminderGui.Add(
        "Button",
        "x426 y308 w96 h30 Default",
        "完成选中"
    )
    btnClose := gReminderGui.Add("Button", "x530 y308 w96 h30", "关闭")

    btnComplete.OnEvent("Click", CompleteSelectedReminder)
    btnClose.OnEvent("Click", CloseReminderWindow)
    gReminderGui.OnEvent("Close", CloseReminderWindow)
    gReminderGui.OnEvent("Escape", CloseReminderWindow)

    PopulateReminderList()
    gReminderGui.Show("w640 h352 Center")

    try WinActivate("ahk_id " gReminderGui.Hwnd)
    try DllCall("User32\MessageBeep", "UInt", 0x00000030)
    try gReminderGui.Flash(true)
}

PopulateReminderList() {
    global gReminderLV, gReminderRowIds, gReminderQueueIds

    if !IsObject(gReminderLV)
        return

    gReminderRowIds := []
    gReminderLV.Delete()

    for id in gReminderQueueIds {
        item := FindIngItemById(id)
        if !IsObject(item)
            continue

        gReminderLV.Add(
            "",
            item["content"],
            item["plannedAt"],
            item["remindAt"]
        )
        gReminderRowIds.Push(id)
    }

    if (gReminderLV.GetCount() > 0)
        gReminderLV.Modify(1, "Select Focus")
}

CompleteSelectedReminder(*) {
    global gReminderLV, gReminderRowIds

    row := gReminderLV.GetNext()
    if !row {
        MsgBox("请先选择一条提醒事项。", "toto", "Iconi")
        return
    }

    if (row <= gReminderRowIds.Length)
        EndItemById(gReminderRowIds[row], "已完成")
}

RefreshReminderWindow() {
    global gReminderGui, gReminderQueueIds

    if !IsObject(gReminderGui)
        return

    validIds := []
    for id in gReminderQueueIds {
        if IsObject(FindIngItemById(id))
            validIds.Push(id)
    }
    gReminderQueueIds := validIds

    if (gReminderQueueIds.Length = 0) {
        CloseReminderWindow()
        return
    }

    PopulateReminderList()
}

CloseReminderWindow(*) {
    global gReminderGui, gReminderQueueIds, gReminderRowIds

    if IsObject(gReminderGui) {
        try gReminderGui.Destroy()
    }

    gReminderGui := 0
    gReminderQueueIds := []
    gReminderRowIds := []
}

; ------------------------------------------------------------
; Windows 消息：单实例、休眠恢复、时间变化、解锁
; ------------------------------------------------------------

HandleSecondInstance(*) {
    ShowMain()
}

HandlePowerBroadcast(wParam, lParam, msg, hwnd) {
    ; PBT_APMRESUMECRITICAL=6
    ; PBT_APMRESUMESUSPEND=7
    ; PBT_APMRESUMEAUTOMATIC=18
    if (wParam = 6 || wParam = 7 || wParam = 18)
        HandleSystemResume()
}

HandleTimeChange(*) {
    HandleSystemResume()
}

HandleSessionChange(wParam, lParam, msg, hwnd) {
    global WTS_SESSION_LOCK, WTS_SESSION_UNLOCK, gSessionLocked

    if (wParam = WTS_SESSION_LOCK) {
        gSessionLocked := true
        SetTimer(ReminderTimerTick, 0)
        return
    }

    if (wParam = WTS_SESSION_UNLOCK) {
        gSessionLocked := false
        HandleSystemResume()
    }
}

HandleSystemResume() {
    LoadAllData(false)
    ProcessDueReminders()
    RefreshMainList()
    ScheduleNextReminder()
}

; ------------------------------------------------------------
; 托盘与退出
; ------------------------------------------------------------

ConfigureTray() {
    global APP_NAME

    A_IconTip := APP_NAME
    A_TrayMenu.Delete()
    A_TrayMenu.Add("打开 toto", ShowMain)
    A_TrayMenu.Add("新增事项", (*) => ShowItemEditor())
    A_TrayMenu.Add("历史事项", ShowHistory)
    A_TrayMenu.Add("设置", ShowSettings)
    A_TrayMenu.Add()
    A_TrayMenu.Add("退出", ExitToto)
    A_TrayMenu.Default := "打开 toto"
}

ExitToto(*) {
    ExitApp()
}

CleanupBeforeExit(*) {
    global gRegisteredHotkey, gRegisteredAppHotkeys
    global gMainGui, gMutexHandle

    try SetTimer(ReminderTimerTick, 0)

    ; 停用全局唤醒快捷键。
    HotIf()
    if (gRegisteredHotkey != "") {
        try Hotkey(gRegisteredHotkey, "Off")
    }

    ; 停用仅在主窗口中生效的七个应用内快捷键。
    if IsObject(gMainGui) {
        HotIfWinActive("ahk_id " gMainGui.Hwnd)
        for _, shortcut in gRegisteredAppHotkeys {
            try Hotkey(shortcut, "Off")
        }
        HotIfWinActive()
    }

    try DllCall(
        "Wtsapi32\WTSUnRegisterSessionNotification",
        "Ptr",
        A_ScriptHwnd
    )

    if gMutexHandle {
        try DllCall("Kernel32\CloseHandle", "Ptr", gMutexHandle)
        gMutexHandle := 0
    }
}

; ------------------------------------------------------------
; CSV 读取与写入
; ------------------------------------------------------------

LoadIngCsv() {
    global ING_PATH

    items := []
    malformed := 0

    try text := FileRead(ING_PATH, "UTF-8")
    catch {
        return Map("items", items, "malformed", 1)
    }

    lines := StrSplit(text, "`n", "`r")
    firstDataLine := true

    for line in lines {
        if (line = "")
            continue

        if firstDataLine {
            firstDataLine := false
            continue
        }

        fields := ParseCsvLine(line)
        if (fields.Length != 8 && fields.Length != 9) {
            malformed += 1
            continue
        }

        id := Trim(fields[1])
        content := fields[2]
        plannedAt := Trim(fields[3])
        legacyRemindMinutes := (fields.Length = 9) ? Trim(fields[4]) : ""
        remindAtRaw := (fields.Length = 9) ? fields[5] : fields[4]
        createdAt := (fields.Length = 9) ? fields[6] : fields[5]
        createdSeqRaw := Trim((fields.Length = 9) ? fields[7] : fields[6])
        remindStatus := (fields.Length = 9) ? fields[8] : fields[7]
        remindedAt := (fields.Length = 9) ? fields[9] : fields[8]

        if (id = "" || content = "" || !RegExMatch(createdSeqRaw, "^\d+$")) {
            malformed += 1
            continue
        }

        if (plannedAt != "") {
            planStamp := DisplayToStamp(plannedAt)
            if (planStamp = "") {
                malformed += 1
                continue
            }
            plannedAt := StampToDisplay(planStamp)
        }

        remindAtResult := ResolveReminderDisplay(plannedAt, remindAtRaw, legacyRemindMinutes)
        if !remindAtResult["ok"] {
            malformed += 1
            continue
        }

        item := Map(
            "id", id,
            "content", content,
            "plannedAt", plannedAt,
            "remindAt", remindAtResult["value"],
            "createdAt", createdAt,
            "createdSeq", createdSeqRaw + 0,
            "remindStatus", remindStatus,
            "remindedAt", remindedAt
        )

        if (item["remindAt"] = "") {
            item["remindStatus"] := "无提醒"
            item["remindedAt"] := ""
        } else {
            if (item["remindStatus"] != "未提醒"
                && item["remindStatus"] != "已提醒") {
                item["remindStatus"] := "未提醒"
                item["remindedAt"] := ""
            }
        }

        items.Push(item)
    }

    return Map("items", items, "malformed", malformed)
}

LoadEndCsv() {
    global END_PATH

    items := []
    malformed := 0

    try text := FileRead(END_PATH, "UTF-8")
    catch {
        return Map("items", items, "malformed", 1)
    }

    lines := StrSplit(text, "`n", "`r")
    firstDataLine := true

    for line in lines {
        if (line = "")
            continue

        if firstDataLine {
            firstDataLine := false
            continue
        }

        fields := ParseCsvLine(line)
        if (fields.Length != 8 && fields.Length != 9) {
            malformed += 1
            continue
        }

        id := Trim(fields[1])
        content := fields[2]
        plannedAt := Trim(fields[3])
        legacyRemindMinutes := (fields.Length = 9) ? Trim(fields[4]) : ""
        remindAtRaw := (fields.Length = 9) ? fields[5] : fields[4]
        createdAt := (fields.Length = 9) ? fields[6] : fields[5]
        createdSeqRaw := Trim((fields.Length = 9) ? fields[7] : fields[6])
        endStatus := (fields.Length = 9) ? fields[8] : fields[7]
        endedAt := (fields.Length = 9) ? fields[9] : fields[8]

        if (id = "" || content = "" || !RegExMatch(createdSeqRaw, "^\d+$")) {
            malformed += 1
            continue
        }

        if (plannedAt != "") {
            planStamp := DisplayToStamp(plannedAt)
            if (planStamp = "") {
                malformed += 1
                continue
            }
            plannedAt := StampToDisplay(planStamp)
        }

        remindAtResult := ResolveReminderDisplay(plannedAt, remindAtRaw, legacyRemindMinutes)
        if !remindAtResult["ok"] {
            malformed += 1
            continue
        }

        item := Map(
            "id", id,
            "content", content,
            "plannedAt", plannedAt,
            "remindAt", remindAtResult["value"],
            "createdAt", createdAt,
            "createdSeq", createdSeqRaw + 0,
            "endStatus", endStatus,
            "endedAt", endedAt
        )

        if (item["endStatus"] != "已完成"
            && item["endStatus"] != "已取消") {
            malformed += 1
            continue
        }

        items.Push(item)
    }

    return Map("items", items, "malformed", malformed)
}

SaveIngItems(showError := true) {
    global ING_PATH, ING_HEADER, gIngItems

    SortIngItems()
    rows := []

    for item in gIngItems {
        rows.Push([
            item["id"],
            item["content"],
            item["plannedAt"],
            item["remindAt"],
            item["createdAt"],
            item["createdSeq"],
            item["remindStatus"],
            item["remindedAt"]
        ])
    }

    ok := WriteCsvAtomic(ING_PATH, ING_HEADER, rows)
    if (!ok && showError) {
        MsgBox(
            "无法写入进行中事项文件。请确认 CSV 未被 Excel 等程序锁定：`n"
            . ING_PATH,
            "toto",
            "Icon!"
        )
    }
    return ok
}

SaveEndItems(showError := true) {
    global END_PATH, END_HEADER, gEndItems

    SortEndItems()
    rows := []

    for item in gEndItems {
        rows.Push([
            item["id"],
            item["content"],
            item["plannedAt"],
            item["remindAt"],
            item["createdAt"],
            item["createdSeq"],
            item["endStatus"],
            item["endedAt"]
        ])
    }

    ok := WriteCsvAtomic(END_PATH, END_HEADER, rows)
    if (!ok && showError) {
        MsgBox(
            "无法写入历史事项文件。请确认 CSV 未被 Excel 等程序锁定：`n"
            . END_PATH,
            "toto",
            "Icon!"
        )
    }
    return ok
}

WriteCsvAtomic(path, header, rows) {
    tempPath := path ".tmp"

    try {
        if FileExist(tempPath)
            FileDelete(tempPath)

        file := FileOpen(tempPath, "w", "UTF-8")
        file.Write(CsvJoin(header) "`r`n")

        for row in rows
            file.Write(CsvJoin(row) "`r`n")

        file.Close()
        FileMove(tempPath, path, 1)
        return true
    } catch {
        try {
            if IsSet(file)
                file.Close()
        }
        try {
            if FileExist(tempPath)
                FileDelete(tempPath)
        }
        return false
    }
}

CsvJoin(fields) {
    line := ""

    for index, field in fields {
        if (index > 1)
            line .= ","
        line .= CsvEscape(field)
    }

    return line
}

CsvEscape(value) {
    value := value ""
    quote := Chr(34)

    needsQuote := InStr(value, ",")
        || InStr(value, quote)
        || InStr(value, "`r")
        || InStr(value, "`n")

    if InStr(value, quote)
        value := StrReplace(value, quote, quote quote)

    return needsQuote ? quote value quote : value
}

ParseCsvLine(line) {
    fields := []
    current := ""
    inQuotes := false
    quote := Chr(34)
    i := 1
    length := StrLen(line)

    while (i <= length) {
        char := SubStr(line, i, 1)

        if (char = quote) {
            if (inQuotes && i < length && SubStr(line, i + 1, 1) = quote) {
                current .= quote
                i += 2
                continue
            }

            inQuotes := !inQuotes
            i += 1
            continue
        }

        if (char = "," && !inQuotes) {
            fields.Push(current)
            current := ""
            i += 1
            continue
        }

        current .= char
        i += 1
    }

    if inQuotes
        return []

    fields.Push(current)
    return fields
}

; ------------------------------------------------------------
; 排序
; ------------------------------------------------------------

SortIngItems() {
    global gIngItems
    gIngItems := MergeSort(gIngItems, CompareIngItems)
}

SortEndItems() {
    global gEndItems
    gEndItems := MergeSort(gEndItems, CompareEndItems)
}

CompareIngItems(a, b) {
    aNoPlan := (a["plannedAt"] = "")
    bNoPlan := (b["plannedAt"] = "")

    if (aNoPlan && !bNoPlan)
        return 1
    if (!aNoPlan && bNoPlan)
        return -1

    if (!aNoPlan && !bNoPlan) {
        result := StrCompare(a["plannedAt"], b["plannedAt"])
        if (result != 0)
            return result
    }

    if (a["createdSeq"] < b["createdSeq"])
        return -1
    if (a["createdSeq"] > b["createdSeq"])
        return 1

    return 0
}

CompareEndItems(a, b) {
    result := StrCompare(a["endedAt"], b["endedAt"])

    ; 结束时间递减排序，因此反转比较结果
    if (result != 0)
        return -result

    if (a["createdSeq"] > b["createdSeq"])
        return -1
    if (a["createdSeq"] < b["createdSeq"])
        return 1

    return 0
}

MergeSort(items, compareFn) {
    if (items.Length <= 1) {
        copy := []
        for item in items
            copy.Push(item)
        return copy
    }

    middle := Floor(items.Length / 2)
    left := []
    right := []

    Loop middle
        left.Push(items[A_Index])

    Loop items.Length - middle
        right.Push(items[middle + A_Index])

    left := MergeSort(left, compareFn)
    right := MergeSort(right, compareFn)

    return MergeSortedArrays(left, right, compareFn)
}

MergeSortedArrays(left, right, compareFn) {
    result := []
    i := 1
    j := 1

    while (i <= left.Length && j <= right.Length) {
        if (compareFn(left[i], right[j]) <= 0) {
            result.Push(left[i])
            i += 1
        } else {
            result.Push(right[j])
            j += 1
        }
    }

    while (i <= left.Length) {
        result.Push(left[i])
        i += 1
    }

    while (j <= right.Length) {
        result.Push(right[j])
        j += 1
    }

    return result
}

; ------------------------------------------------------------
; 查找、时间和通用工具
; ------------------------------------------------------------

FindIngIndexById(id) {
    global gIngItems

    for index, item in gIngItems {
        if (item["id"] = id)
            return index
    }
    return 0
}

FindIngItemById(id) {
    global gIngItems

    index := FindIngIndexById(id)
    return index ? gIngItems[index] : 0
}

NormalizeHotkey(value) {
    value := Trim(value)
    if (value = "")
        return ""

    hasWin := false
    hasCtrl := false
    hasAlt := false
    hasShift := false
    pos := 1
    length := StrLen(value)

    while (pos <= length) {
        symbol := SubStr(value, pos, 1)
        if (symbol = "#")
            hasWin := true
        else if (symbol = "^")
            hasCtrl := true
        else if (symbol = "!")
            hasAlt := true
        else if (symbol = "+")
            hasShift := true
        else
            break
        pos += 1
    }

    keyName := CanonicalizeHotkeyKeyName(Trim(SubStr(value, pos)))
    if (keyName = "")
        return ""

    prefix := (hasWin ? "#" : "")
        . (hasCtrl ? "^" : "")
        . (hasAlt ? "!" : "")
        . (hasShift ? "+" : "")

    return prefix keyName
}

CanonicalizeHotkeyKeyName(keyName) {
    lowerKey := StrLower(Trim(keyName))

    switch lowerKey {
        case "space", "vk20", "sc039", "vke5":
            return "Space"
    }

    return Trim(keyName)
}

FormatHotkeyForDisplay(hotkeyValue) {
    hotkeyValue := NormalizeHotkey(hotkeyValue)
    if (hotkeyValue = "")
        return ""

    parts := []
    if InStr(hotkeyValue, "#")
        parts.Push("Win")
    if InStr(hotkeyValue, "^")
        parts.Push("Ctrl")
    if InStr(hotkeyValue, "!")
        parts.Push("Alt")
    if InStr(hotkeyValue, "+")
        parts.Push("Shift")

    keyName := CanonicalizeHotkeyKeyName(
        RegExReplace(hotkeyValue, "^[#\^\!\+]+")
    )
    if (keyName != "")
        parts.Push(keyName)

    text := ""
    for index, part in parts {
        if (index > 1)
            text .= " + "
        text .= part
    }
    return text
}

HandleSettingsHotkeyInput(wParam, lParam, msg, hwnd) {
    global gSettingsGui, gSettingsHotkey, gSettingsHotkeyValue

    if !IsObject(gSettingsGui) || !IsObject(gSettingsHotkey)
        return

    focusedHwnd := DllCall("User32\GetFocus", "Ptr")
    if (focusedHwnd != gSettingsHotkey.Hwnd)
        return

    ; 允许常规对话框导航键继续工作。
    if (wParam = 0x09 || wParam = 0x0D || wParam = 0x1B)
        return

    if (wParam = 0x08 || wParam = 0x2E) {
        gSettingsHotkeyValue := ""
        gSettingsHotkey.Value := ""
        return 0
    }

    ; 仅按下修饰键时不提交，避免把半成品写进设置。
    if (wParam = 0x10 || wParam = 0x11 || wParam = 0x12
        || wParam = 0x5B || wParam = 0x5C)
        return 0

    scanCode := (lParam >> 16) & 0xFF
    keyName := GetKeyName(Format("vk{:02X}sc{:03X}", wParam, scanCode))
    if (keyName = "")
        keyName := GetKeyName(Format("vk{:02X}", wParam))

    keyName := CanonicalizeHotkeyKeyName(keyName)
    if (keyName = "")
        return 0

    hotkeyValue := (GetKeyState("Ctrl", "P") ? "^" : "")
        . (GetKeyState("Alt", "P") ? "!" : "")
        . (GetKeyState("Shift", "P") ? "+" : "")
        . keyName

    gSettingsHotkeyValue := NormalizeHotkey(hotkeyValue)
    gSettingsHotkey.Value := FormatHotkeyForDisplay(gSettingsHotkeyValue)
    return 0
}

FocusEditorField(fieldName) {
    global gEditorContentEdit, gEditorPlannedEdit, gEditorReminderEdit

    switch fieldName {
        case "plannedAt":
            FocusControlIfAlive(gEditorPlannedEdit)
        case "remindAt":
            FocusControlIfAlive(gEditorReminderEdit)
        default:
            FocusControlIfAlive(gEditorContentEdit)
    }
}

SetEditorError(message := "") {
    global gEditorErrorText

    if IsObject(gEditorErrorText)
        gEditorErrorText.Text := message
}

DisableImeForControl(ctrl) {
    if !IsObject(ctrl)
        return false

    try {
        DllCall("Imm32\ImmAssociateContext", "Ptr", ctrl.Hwnd, "Ptr", 0, "Ptr")
        return true
    } catch {
        return false
    }
}

FocusControlIfAlive(ctrl) {
    if !IsObject(ctrl)
        return false

    try {
        ctrl.Focus()
        return true
    } catch {
        return false
    }
}

ResolveReminderDisplay(plannedAt, remindAtRaw, legacyRemindMinutes := "") {
    remindAtRaw := Trim(remindAtRaw)
    if (remindAtRaw != "") {
        remindStamp := DisplayToStamp(remindAtRaw)
        if (remindStamp = "")
            return Map("ok", false)
        return Map("ok", true, "value", StampToDisplay(remindStamp))
    }

    legacyRemindMinutes := Trim(legacyRemindMinutes)
    if (legacyRemindMinutes = "")
        return Map("ok", true, "value", "")

    if (plannedAt = "" || !RegExMatch(legacyRemindMinutes, "^\d+$"))
        return Map("ok", false)

    planStamp := DisplayToStamp(plannedAt)
    if (planStamp = "")
        return Map("ok", false)

    try remindStamp := DateAdd(planStamp, -(legacyRemindMinutes + 0), "Minutes")
    catch {
        return Map("ok", false)
    }

    return Map("ok", true, "value", StampToDisplay(remindStamp))
}

NowDisplay() {
    return FormatTime(A_Now, "yyyy-MM-dd HH:mm:ss")
}

StampToDisplay(stamp) {
    if (stamp = "")
        return ""
    return FormatTime(stamp, "yyyy-MM-dd HH:mm:ss")
}

DisplayToStamp(display) {
    if (display = "")
        return ""

    stamp := RegExReplace(display, "\D")
    if (StrLen(stamp) != 14)
        return ""

    year := SubStr(stamp, 1, 4) + 0
    month := SubStr(stamp, 5, 2) + 0
    day := SubStr(stamp, 7, 2) + 0
    hour := SubStr(stamp, 9, 2) + 0
    minute := SubStr(stamp, 11, 2) + 0
    second := SubStr(stamp, 13, 2) + 0

    if !IsValidDateTime(year, month, day, hour, minute)
        return ""
    if (second < 0 || second > 59)
        return ""

    return stamp
}

NewGuid() {
    guidBuffer := Buffer(16, 0)
    stringBuffer := Buffer(78, 0)

    if DllCall("Ole32\CoCreateGuid", "Ptr", guidBuffer.Ptr, "Int") != 0
        return A_Now A_MSec Random(1000, 9999)

    DllCall(
        "Ole32\StringFromGUID2",
        "Ptr",
        guidBuffer.Ptr,
        "Ptr",
        stringBuffer.Ptr,
        "Int",
        39
    )

    guid := StrGet(stringBuffer.Ptr, "UTF-16")
    return Trim(guid, "{}")
}
