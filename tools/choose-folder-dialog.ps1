# 自动完成"选择文件夹"对话框：填路径 + 点确认按钮
# 用法：choose-folder-dialog.ps1 -ProcessId 1234 -Path "D:\导出测试"
# 说明：先试 UIA 的 ValuePattern 写入路径框，再找确认按钮（名字含"选择/确定/打开"）点一下。

param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [Parameter(Mandatory = $true)][string]$Path
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$P = [System.Windows.Automation.AutomationElement]
$byPid = New-Object System.Windows.Automation.PropertyCondition($P::ProcessIdProperty, $ProcessId)
$byClass = New-Object System.Windows.Automation.PropertyCondition($P::ClassNameProperty, "#32770")
$cond = New-Object System.Windows.Automation.AndCondition($byPid, $byClass)

$dialog = $null
for ($i = 0; $i -lt 30; $i++) {
    $dialog = $P::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
    if ($null -ne $dialog) { break }
    Start-Sleep -Milliseconds 300
}
if ($null -eq $dialog) { throw "没有找到文件夹选择对话框" }
"dialog='$($dialog.Current.Name)'"

$editCond = New-Object System.Windows.Automation.PropertyCondition($P::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
$edit = $dialog.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $editCond)
if ($null -eq $edit) { throw "对话框里没有找到路径输入框" }
$edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Path)
Start-Sleep -Milliseconds 600
"path set: $($edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value)"

$btnCond = New-Object System.Windows.Automation.PropertyCondition($P::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$buttons = $dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
$target = $null
foreach ($b in $buttons) {
    $n = $b.Current.Name
    if ($n -match "选择|确定|打开") { $target = $b; break }
}
if ($null -eq $target) { throw "没有找到确认按钮（候选：" + ($buttons | ForEach-Object { $_.Current.Name }) -join "/" + ")" }
"clicking '$($target.Current.Name)'"
$target.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
