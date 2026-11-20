param(
    [string]$PolicyFile = (Join-Path $PSScriptRoot '源码\TriggerPolicy.cs'),
    [string]$OutputDir  = (Join-Path $PSScriptRoot '构建输出\正式'),
    [switch]$WithChecks
)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$OutputEncoding=[Console]::OutputEncoding
$fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath (Join-Path $fx 'csc.exe'))) { $fx=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319' }
$csc=Join-Path $fx 'csc.exe'
$src=Join-Path $PSScriptRoot '源码'
$null=[IO.Directory]::CreateDirectory($OutputDir)
$OutputDir=(Resolve-Path -LiteralPath $OutputDir).Path
$ico=Join-Path $OutputDir 'HuaYi.ico'
# Build our own small vector mark at native tray, taskbar and Explorer sizes.
Add-Type -AssemblyName System.Drawing
$images=New-Object System.Collections.ArrayList
foreach ($n in @(16,24,32,48,64,128)) {
    $bmp=[Drawing.Bitmap]::new($n,$n)
    $g=[Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([Drawing.Color]::Transparent)
    $g.ScaleTransform([single]($n/64),[single]($n/64))
    $path=[Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc(2,2,16,16,180,90); $path.AddArc(46,2,16,16,270,90)
    $path.AddArc(46,46,16,16,0,90); $path.AddArc(2,46,16,16,90,90); $path.CloseFigure()
    $bg=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(0,111,105))
    $g.FillPath($bg,$path)
    $pen=[Drawing.Pen]::new([Drawing.Color]::White,4.2)
    $pen.StartCap=$pen.EndCap=[Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($pen,29,16,33,20); $g.DrawLine($pen,15,25,48,25)
    $g.DrawBezier($pen,40,26,36,37,28,44,16,49)
    $g.DrawBezier($pen,23,28,26,36,35,44,48,49)
    $dot=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255,206,119))
    $g.FillEllipse($dot,46,10,8,8)
    $ms=[IO.MemoryStream]::new(); $bmp.Save($ms,[Drawing.Imaging.ImageFormat]::Png)
    $null=$images.Add([pscustomobject]@{ Size=$n; Data=$ms.ToArray() })
    $ms.Dispose(); $dot.Dispose(); $pen.Dispose(); $bg.Dispose(); $path.Dispose(); $g.Dispose(); $bmp.Dispose()
}
$stream=[IO.File]::Create($ico); $wr=[IO.BinaryWriter]::new($stream)
try {
    $wr.Write([uint16]0); $wr.Write([uint16]1); $wr.Write([uint16]$images.Count)
    $offset=6+16*$images.Count
    foreach ($im in $images) {
        $wr.Write([byte]$im.Size); $wr.Write([byte]$im.Size); $wr.Write([byte]0); $wr.Write([byte]0)
        $wr.Write([uint16]1); $wr.Write([uint16]32); $wr.Write([uint32]$im.Data.Length); $wr.Write([uint32]$offset)
        $offset+=$im.Data.Length
    }
    foreach ($im in $images) { $wr.Write([byte[]]$im.Data) }
} finally { $wr.Dispose(); $stream.Dispose() }
$refs=@('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Net.Http.dll','System.Web.Extensions.dll','WPF\WindowsBase.dll','WPF\UIAutomationClient.dll','WPF\UIAutomationTypes.dll') |
  ForEach-Object { '/reference:'+(Join-Path $fx $_) }
$common=@('/nologo','/optimize+','/warn:4','/warnaserror+','/codepage:65001','/utf8output','/platform:anycpu')
$app=Join-Path $OutputDir '划译.exe'
$files=@('Core.cs','Selection.cs','Views.cs','Program.cs') | ForEach-Object { Join-Path $src $_ }
& $csc @common '/target:winexe' ('/out:'+$app) ('/win32icon:'+$ico) ('/win32manifest:'+(Join-Path $src 'app.manifest')) @refs @files $PolicyFile
if ($LASTEXITCODE -ne 0) { throw "应用编译失败：$LASTEXITCODE" }
if ($WithChecks) {
    $trefs=@('WPF\PresentationCore.dll','WPF\PresentationFramework.dll','System.Xaml.dll','System.Web.dll') | ForEach-Object { '/reference:'+(Join-Path $fx $_) }
    & $csc @common '/target:exe' ('/out:'+(Join-Path $OutputDir 'HuaYi.Checks.exe')) @refs @trefs ('/reference:'+$app) (Join-Path $PSScriptRoot '测试\Checks.cs')
    if ($LASTEXITCODE -ne 0) { throw "验证程序编译失败：$LASTEXITCODE" }
}
Write-Output ('已编译：'+$app)
