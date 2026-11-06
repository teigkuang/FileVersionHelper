# 按标题找到本进程的模态弹窗：可选截图 + 可选点某个 AutomationId 的按钮
# 用法：dialog-tool.ps1 -ProcessId 1234 -Title "设置" -Out "d:\dlg.png" -ClickId "CancelButton"

param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [Parameter(Mandatory = $true)][string]$Title,
    [string]$Out,
    [string]$ClickId
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$P = [System.Windows.Automation.AutomationElement]
$byPid = New-Object System.Windows.Automation.PropertyCondition($P::ProcessIdProperty, $ProcessId)
$byName = New-Object System.Windows.Automation.PropertyCondition($P::NameProperty, $Title)
$isWindow = New-Object System.Windows.Automation.PropertyCondition($P::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
$cond = New-Object System.Windows.Automation.AndCondition($byPid, $byName, $isWindow)

$dialog = $P::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
if ($null -eq $dialog) { throw "找不到标题为 '$Title' 的窗口" }

"dialog hwnd=$($dialog.Current.NativeWindowHandle) rect=$($dialog.Current.BoundingRectangle)"

if ($Out) {
    & "$PSScriptRoot\capture-window.ps1" -Hwnd $dialog.Current.NativeWindowHandle -Out $Out
}

if ($ClickId) {
    $btnCond = New-Object System.Windows.Automation.PropertyCondition($P::AutomationIdProperty, $ClickId)
    $btn = $dialog.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
    if ($null -eq $btn) { throw "弹窗里找不到 $ClickId" }
    $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    "clicked $ClickId"
}
