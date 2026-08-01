param(
  [Parameter(Mandatory = $true)][string]$OutputPath,
  [string]$ProcessName = "SLDWORKS"
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$code = @'
using System;
using System.Runtime.InteropServices;
public static class SwShot {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
  [StructLayout(LayoutKind.Sequential)]
  public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
'@
Add-Type -TypeDefinition $code -ErrorAction SilentlyContinue | Out-Null

$proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
  Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } |
  Select-Object -First 1
if (-not $proc) { throw "No $ProcessName window found." }

[void][SwShot]::ShowWindow($proc.MainWindowHandle, 9)
[void][SwShot]::SetForegroundWindow($proc.MainWindowHandle)
Start-Sleep -Milliseconds 500

$rect = New-Object SwShot+RECT
if (-not [SwShot]::GetWindowRect($proc.MainWindowHandle, [ref]$rect)) {
  throw "GetWindowRect failed."
}

$width = [Math]::Max(1, $rect.Right - $rect.Left)
$height = [Math]::Max(1, $rect.Bottom - $rect.Top)
$dir = Split-Path -Parent $OutputPath
if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

$bmp = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bmp)
$graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bmp.Size)
$bmp.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bmp.Dispose()

Write-Output $OutputPath
