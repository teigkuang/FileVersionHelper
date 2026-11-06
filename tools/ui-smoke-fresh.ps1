# 冒烟：全新文件夹在界面里"填说明→首次提交"应自动建库（回归曾经的"L1 容器：未找到 .fvhelper.dat 已禁止提交"）
# 以及"－ 移除项目"只删列表条目不删文件。结果全部写日志文件，避免 stdout 被吞。

$ErrorActionPreference = "Stop"
$log = "$env:TEMP\fv-uismoke.log"
function L($m) { Add-Content -LiteralPath $log -Value $m -Encoding utf8 }
"" | Set-Content -LiteralPath $log -Encoding utf8

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class W {
    [DllImport("user32.dll")] public static extern bool EnumWindows(Func<IntPtr,IntPtr,bool> cb, IntPtr p);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
}
"@

function Find-Dialog([int]$pid, [string]$titlePart) {
    $script:found = $null
    $cb = [Func[IntPtr,IntPtr,bool]]{
        param($h, $l)
        $p = 0
        [void][W]::GetWindowThreadProcessId($h, [ref]$p)
        if ($p -eq $script:tpid -and [W]::IsWindowVisible($h)) {
            $sb = New-Object Text.StringBuilder 256
            [void][W]::GetWindowText($h, $sb, 256)
            $cn = New-Object Text.StringBuilder 256
            [void][W]::GetClassName($h, $cn, 256)
            if ($cn.ToString() -eq "#32770" -and $sb.ToString() -like "*$titlePart*") { $script:found = $h }
        }
        return $true
    }
    $script:tpid = $pid
    [void][W]::EnumWindows($cb, [IntPtr]::Zero)
    return $script:found
}

function Click-DialogButton([IntPtr]$hwnd, [string]$text) {
    # 消息框按钮用 UIA 找（偶数情况下能拿到），拿不到就用键盘
    $el = [Windows.Automation.AutomationElement]::FromHandle($hwnd)
    $cond = New-Object Windows.Automation.AndCondition(
        (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Button)),
        (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, $text)))
    $btn = $el.FindFirst([Windows.Automation.TreeScope]::Descendants, $cond)
    if ($btn -ne $null) {
        $btn.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
        return "uia"
    }
    [W]::PostMessage($hwnd, 0x0111, [IntPtr]0, [IntPtr]0) | Out-Null
    return "fallback"
}

$cfg = Join-Path $env:LOCALAPPDATA "FVHelper"
$projectsFile = Join-Path $cfg "projects.json"
$backup = "$projectsFile.codex-bak"
Copy-Item -LiteralPath $projectsFile -Destination $backup -Force

$fresh = Join-Path $env:TEMP "fv-fresh-ui"
if (Test-Path -LiteralPath $fresh) { Remove-Item -LiteralPath $fresh -Recurse -Force }
New-Item -ItemType Directory -Path $fresh | Out-Null
Set-Content -LiteralPath (Join-Path $fresh "readme.txt") -Value "新项目第一份内容" -Encoding utf8
Set-Content -LiteralPath (Join-Path $fresh "大纲.md") -Value "- 第一章" -Encoding utf8
L "fresh = $fresh"

$entries = @(
    @{ Path = "$env:TEMP\fv-ui-test"; Name = "UI测试-论文" },
    @{ Path = $fresh; Name = "全新项目" }
)
($entries | ConvertTo-Json -Depth 4) | Set-Content -LiteralPath $projectsFile -Encoding utf8
L "projects.json 已写入测试条目"

$exe = "E:\FileEditVersionHelper\app\src\FvHelper.Desktop\bin\Release\net10.0-windows\FvHelper.exe"
$psi = New-Object System.Diagnostics.ProcessStartInfo($exe)
$psi.WorkingDirectory = Split-Path $exe
$psi.UseShellExecute = $false
$psi.EnvironmentVariables["DOTNET_ROOT"] = "D:\IDE\dotnet"
$proc = [System.Diagnostics.Process]::Start($psi)
L "启动 pid=$($proc.Id)"
Start-Sleep -Seconds 4

$main = [Windows.Automation.AutomationElement]::RootElement
$cond = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$win = $main.FindFirst([Windows.Automation.TreeScope]::Children, $cond)
if ($win -eq $null) { L "FAIL: 没找到主窗口"; exit 1 }
L "主窗口: $($win.Current.Name)"

# 选"全新项目"
function Find-ByName($root, $type, $name) {
    $c = New-Object Windows.Automation.AndCondition(
        (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, $type)),
        (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, $name)))
    return $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $c)
}

function ById($root, $id) {
    $c = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    return $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $c)
}

$item = Find-ByName $win ([Windows.Automation.ControlType]::ListItem) "全新项目"
if ($item -eq $null) {
    # 名字可能带后缀，退回扫全部列表项
    $c = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::ListItem)
    $items = $win.FindAll([Windows.Automation.TreeScope]::Descendants, $c)
    L "列表项共 $($items.Count) 个："
    foreach ($i in $items) { L "  - $($i.Current.Name)" }
    $item = $items | Where-Object { $_.Current.Name -like "*全新*" } | Select-Object -First 1
}
if ($item -eq $null) { L "FAIL: 没找到新项目条目"; exit 1 }
$item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
L "已选中: $($item.Current.Name)"
Start-Sleep -Seconds 3

# 状态行与提示条
foreach ($id in @("ScanStatus", "ChangedText")) {
    $e = ById $win $id
    if ($e -ne $null) { L "$id = $($e.Current.Name)" }
}

$box = ById $win "CommitMessageBox"
if ($box -eq $null) { L "FAIL: 找不到说明框"; exit 1 }
$box.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue("第一次提交（自动建库）")
Start-Sleep -Milliseconds 500

$btn = ById $win "CommitButton"
if ($btn -eq $null) { L "FAIL: 找不到提交按钮"; exit 1 }
L "CommitButton.IsEnabled = $($btn.Current.IsEnabled)"
if (-not $btn.Current.IsEnabled) { L "FAIL: 提交按钮仍禁用"; exit 1 }
$btn.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
L "已点提交"
Start-Sleep -Seconds 3

$dlg = Find-Dialog $proc.Id "FVHelper"
L "消息框 hwnd = $dlg"
$dlgNot = $dlg -eq $null -or $dlg -eq [IntPtr]::Zero
if ($dlgNot) { L "FAIL: 没等到提示框（可能是失败弹窗标题不同）" } else {
    & "E:\FileEditVersionHelper\tools\capture-window.ps1" -Hwnd ([int]$dlg.ToInt64()) -Out "$env:TEMP\fv-uismoke-dlg.png" | Out-Null
    $txtEl = [Windows.Automation.AutomationElement]::FromHandle($dlg)
    $c = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Text)
    $texts = $txtEl.FindAll([Windows.Automation.TreeScope]::Descendants, $c)
    foreach ($t in $texts) { L "提示框文字: $($t.Current.Name)" }
    $mode = Click-DialogButton $dlg "确定"
    L "关闭提示框: $mode"
}
Start-Sleep -Seconds 2

# 版本库是否建好
$dat = Join-Path $fresh ".fvhelper.dat"
L "dat exists = $(Test-Path -LiteralPath $dat)"
& "E:\FileEditVersionHelper\tools\capture-window.ps1" -ProcessId $proc.Id -Out "$env:TEMP\fv-uismoke-main.png" | Out-Null

# 移除项目（只删列表条目）
$rm = ById $win "RemoveProjectButton"
if ($rm -ne $null) {
    $rm.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 2
    $dlg2 = Find-Dialog $proc.Id "移除项目"
    if ($dlg2 -ne $null -and $dlg2 -ne [IntPtr]::Zero) {
        L "移除确认框 found"
        $mode2 = Click-DialogButton $dlg2 "是"
        L "点了是: $mode2"
        Start-Sleep -Seconds 2
        L "移除后 projects.json ="
        L (Get-Content -LiteralPath $projectsFile -Raw)
    } else {
        L "FAIL: 没等到移除确认框"
    }
} else { L "NOTE: 没找到 RemoveProjectButton" }

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 1
Copy-Item -LiteralPath $backup -Destination $projectsFile -Force
Remove-Item -LiteralPath $backup -Force
L "已恢复 projects.json"
L "DONE"
