# 截取指定窗口的内容（不依赖前台焦点，被其它窗口遮挡也能正确采集）
# 用法：capture-window.ps1 -ProcessId 1234 -Out "D:\shot.png"
# 原理：PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT=2)，而不是 CopyFromScreen
#      （CopyFromScreen 按屏幕坐标裁剪，前台被别的窗口遮住时会抓到别人的画面）

param(
    [int]$ProcessId,
    [long]$Hwnd = 0,
    [Parameter(Mandatory = $true)][string]$Out,
    [int]$TimeoutSeconds = 15
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32Cap {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$hwnd = if ($Hwnd -ne 0) { [IntPtr]$Hwnd } else { [IntPtr]::Zero }
if ($hwnd -eq [IntPtr]::Zero -and $ProcessId -eq 0) { throw "必须给 -ProcessId 或 -Hwnd" }
while ($hwnd -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline) {
    $proc = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($null -eq $proc) { throw "进程 $ProcessId 已退出" }
    if ($proc.MainWindowHandle -ne 0) { $hwnd = [IntPtr]$proc.MainWindowHandle; break }
    Start-Sleep -Milliseconds 300
}
if ($hwnd -eq [IntPtr]::Zero) { throw "等不到窗口句柄（窗口可能还没建好）" }

$rect = New-Object Win32Cap+RECT
[void][Win32Cap]::GetWindowRect($hwnd, [ref]$rect)
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) { throw "窗口尺寸异常：${width}x${height}" }

$bmp = New-Object System.Drawing.Bitmap($width, $height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [Win32Cap]::PrintWindow($hwnd, $hdc, 2)
$g.ReleaseHdc($hdc)
$g.Dispose()
if (-not $ok) { throw "PrintWindow 失败" }

$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

"OK $Out ${width}x${height}"
