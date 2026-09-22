Add-Type -AssemblyName System.Drawing

$outPath = Join-Path (Get-Location) "ui-style-showcase.png"
$width = 1920
$height = 1480

$bmp = New-Object System.Drawing.Bitmap $width, $height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$g.Clear([System.Drawing.Color]::FromArgb(245, 247, 250))

function Brush($hex) {
    return New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($hex))
}

function Font($size, $style = [System.Drawing.FontStyle]::Regular) {
    return New-Object System.Drawing.Font("Microsoft YaHei UI", $size, $style, [System.Drawing.GraphicsUnit]::Pixel)
}

function RoundRectPath($x, $y, $w, $h, $r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function FillRound($x, $y, $w, $h, $r, $color) {
    $path = RoundRectPath $x $y $w $h $r
    $g.FillPath((Brush $color), $path)
}

function StrokeRound($x, $y, $w, $h, $r, $color, $sw = 1) {
    $path = RoundRectPath $x $y $w $h $r
    $p = New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml($color)), $sw
    $g.DrawPath($p, $path)
}

function Text($s, $x, $y, $size, $color, $style = [System.Drawing.FontStyle]::Regular) {
    $g.DrawString($s, (Font $size $style), (Brush $color), [single]$x, [single]$y)
}

function DrawIcon($x, $y, $color) {
    $p = New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml($color)), 3
    $g.DrawLine($p, $x + 8, $y + 18, $x + 25, $y + 8)
    $g.DrawLine($p, $x + 25, $y + 8, $x + 42, $y + 18)
    $g.DrawLine($p, $x + 14, $y + 18, $x + 14, $y + 38)
    $g.DrawLine($p, $x + 36, $y + 18, $x + 36, $y + 38)
    $g.DrawLine($p, $x + 14, $y + 38, $x + 36, $y + 38)
}

function DrawPanelBase($x, $y, $title, $subtitle, $bg, $fg) {
    FillRound $x $y 560 390 26 $bg
    StrokeRound $x $y 560 390 26 "#d8dee9" 1
    Text $title ($x + 26) ($y + 22) 30 $fg ([System.Drawing.FontStyle]::Bold)
    Text $subtitle ($x + 28) ($y + 62) 18 "#7b8493"
}

function DrawFluent($x, $y) {
    DrawPanelBase $x $y "Fluent 设计" "Windows 11：圆角、侧栏、云母、提示条" "#f7fbff" "#1d2430"
    FillRound ($x + 28) ($y + 92) 64 248 18 "#202933"
    foreach ($i in 0..4) { FillRound ($x + 46) ($y + 118 + $i * 42) 28 28 8 "#364453" }
    FillRound ($x + 46) ($y + 118) 28 28 8 "#4d9df7"
    FillRound ($x + 120) ($y + 96) 384 112 20 "#4d9df7"
    Text "AI 启动器" ($x + 146) ($y + 122) 20 "#ffffff"
    Text "启动器界面" ($x + 146) ($y + 148) 34 "#ffffff" ([System.Drawing.FontStyle]::Bold)
    Text "模型 / 同步 / 一键启动" ($x + 146) ($y + 186) 17 "#eef7ff"
    FillRound ($x + 120) ($y + 282) 180 58 14 "#ffffff"
    DrawIcon ($x + 134) ($y + 288) "#4d9df7"
    Text "版本" ($x + 180) ($y + 298) 19 "#1d2430"
    Text "最新版" ($x + 180) ($y + 322) 14 "#8892a0"
    FillRound ($x + 320) ($y + 282) 96 58 14 "#ffffff"
    Text "公告" ($x + 348) ($y + 299) 20 "#1d2430"
    FillRound ($x + 432) ($y + 282) 80 58 20 "#4d9df7"
    Text "启动" ($x + 448) ($y + 300) 22 "#ffffff" ([System.Drawing.FontStyle]::Bold)
}

function DrawMaterial($x, $y) {
    DrawPanelBase $x $y "Material 设计" "Google：层级、阴影、悬浮按钮、动效秩序" "#ffffff" "#202124"
    FillRound ($x + 28) ($y + 92) 504 248 12 "#fef7ff"
    StrokeRound ($x + 28) ($y + 92) 504 248 12 "#e7dff3" 1
    FillRound ($x + 54) ($y + 118) 452 82 10 "#6750a4"
    Text "启动器界面" ($x + 80) ($y + 139) 30 "#ffffff" ([System.Drawing.FontStyle]::Bold)
    Text "Stable Diffusion WebUI" ($x + 80) ($y + 174) 17 "#eadfff"
    foreach ($i in 0..2) {
        FillRound ($x + 54 + $i * 151) ($y + 224) 132 62 12 "#ffffff"
        StrokeRound ($x + 54 + $i * 151) ($y + 224) 132 62 12 "#eee7f5" 1
        Text @("模型", "插件", "设置")[$i] ($x + 84 + $i * 151) ($y + 246) 18 "#202124"
    }
    FillRound ($x + 414) ($y + 286) 82 82 41 "#e91e63"
    Text ">" ($x + 445) ($y + 304) 36 "#ffffff" ([System.Drawing.FontStyle]::Bold)
}

function DrawApple($x, $y) {
    DrawPanelBase $x $y "Apple HIG" "macOS/iOS：留白、玻璃感、克制动效" "#fbfbfd" "#1d1d1f"
    FillRound ($x + 34) ($y + 98) 492 236 28 "#ffffff"
    StrokeRound ($x + 34) ($y + 98) 492 236 28 "#e5e5ea" 1
    Text "启动器" ($x + 70) ($y + 130) 38 "#1d1d1f" ([System.Drawing.FontStyle]::Bold)
    Text "创意 AI 工作区" ($x + 72) ($y + 174) 20 "#6e6e73"
    FillRound ($x + 72) ($y + 220) 132 44 22 "#f2f2f7"
    FillRound ($x + 218) ($y + 220) 132 44 22 "#f2f2f7"
    Text "模型" ($x + 105) ($y + 232) 18 "#1d1d1f"
    Text "更新" ($x + 244) ($y + 232) 18 "#1d1d1f"
    FillRound ($x + 362) ($y + 216) 126 54 27 "#007aff"
    Text "打开" ($x + 402) ($y + 231) 21 "#ffffff" ([System.Drawing.FontStyle]::Bold)
}

function DrawGame($x, $y) {
    DrawPanelBase $x $y "游戏启动器" "海报优先：公告、启动按钮、强媒体" "#121820" "#f5f7fb"
    FillRound ($x + 26) ($y + 88) 508 252 16 "#1f2834"
    $rect = New-Object System.Drawing.RectangleF(($x + 26), ($y + 88), 508, 160)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.ColorTranslator]::FromHtml("#2a6fdb"), [System.Drawing.ColorTranslator]::FromHtml("#7c3aed"), 20)
    $g.FillPath($grad, (RoundRectPath ($x + 26) ($y + 88) 508 160 16))
    Text "新版本上线" ($x + 56) ($y + 122) 22 "#ffffff"
    Text "启动器界面" ($x + 56) ($y + 154) 36 "#ffffff" ([System.Drawing.FontStyle]::Bold)
    Text "公告 / 资源 / 模型管理" ($x + 56) ($y + 198) 18 "#dbeafe"
    FillRound ($x + 52) ($y + 270) 302 48 10 "#263241"
    Text "镜像已更新，启动前同步" ($x + 72) ($y + 283) 17 "#dce6f2"
    FillRound ($x + 376) ($y + 260) 132 68 18 "#54c8ff"
    Text "启动" ($x + 416) ($y + 280) 24 "#082030" ([System.Drawing.FontStyle]::Bold)
}

function DrawAnt($x, $y) {
    DrawPanelBase $x $y "Ant Design 企业风" "后台系统：表单、表格、高密度信息" "#ffffff" "#1f2937"
    FillRound ($x + 28) ($y + 92) 504 248 6 "#f5f7fa"
    FillRound ($x + 28) ($y + 92) 504 44 6 "#ffffff"
    Text "启动器控制台" ($x + 48) ($y + 104) 20 "#1f2937" ([System.Drawing.FontStyle]::Bold)
    foreach ($i in 0..3) {
        FillRound ($x + 48 + $i * 118) ($y + 154) 100 54 4 "#ffffff"
        StrokeRound ($x + 48 + $i * 118) ($y + 154) 100 54 4 "#d9d9d9" 1
        Text @("模型", "插件", "任务", "日志")[$i] ($x + 64 + $i * 118) ($y + 169) 18 "#1f2937"
    }
    FillRound ($x + 48) ($y + 228) 338 84 4 "#ffffff"
    StrokeRound ($x + 48) ($y + 228) 338 84 4 "#d9d9d9" 1
    foreach ($i in 0..2) { Text @("版本      2.9.1", "状态      已同步", "后端      A1111")[$i] ($x + 68) ($y + 240 + $i * 24) 16 "#4b5563" }
    FillRound ($x + 406) ($y + 250) 96 42 4 "#1677ff"
    Text "启动" ($x + 434) ($y + 260) 19 "#ffffff"
}

function DrawGlass($x, $y) {
    DrawPanelBase $x $y "玻璃拟态" "半透明卡片、光晕、模糊层次" "#101423" "#ffffff"
    $rect = New-Object System.Drawing.RectangleF(($x + 28), ($y + 92), 504, 248)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.ColorTranslator]::FromHtml("#2563eb"), [System.Drawing.ColorTranslator]::FromHtml("#ec4899"), 35)
    $g.FillPath($grad, (RoundRectPath ($x + 28) ($y + 92) 504 248 24))
    $overlay = [System.Drawing.Color]::FromArgb(80, 255, 255, 255)
    $g.FillPath((New-Object System.Drawing.SolidBrush $overlay), (RoundRectPath ($x + 64) ($y + 128) 432 176 22))
    StrokeRound ($x + 64) ($y + 128) 432 176 22 "#ffffff" 2
    Text "AI 创意坞" ($x + 92) ($y + 154) 20 "#ffffff"
    Text "启动器界面" ($x + 92) ($y + 186) 35 "#ffffff" ([System.Drawing.FontStyle]::Bold)
    FillRound ($x + 92) ($y + 242) 122 42 21 "#ffffff"
    Text "启动" ($x + 132) ($y + 252) 19 "#7c3aed" ([System.Drawing.FontStyle]::Bold)
}

Text "典型 UI 设计风格" 60 36 46 "#111827" ([System.Drawing.FontStyle]::Bold)
Text "同一套启动器信息架构，用不同视觉语言呈现" 62 94 24 "#5b6472"

DrawFluent 60 150
DrawMaterial 680 150
DrawApple 1300 150
DrawGame 60 590
DrawAnt 680 590
DrawGlass 1300 590

FillRound 60 1050 1800 340 28 "#ffffff"
StrokeRound 60 1050 1800 340 28 "#dbe3ee" 1
Text "如何阅读这张图" 98 1088 30 "#111827" ([System.Drawing.FontStyle]::Bold)
Text "Fluent：适合 Windows 桌面工具、启动器、设置面板与系统集成。" 100 1140 22 "#374151"
Text "Material：强调层级、阴影和动效秩序，常见于跨平台网页和 Android 应用。" 100 1180 22 "#374151"
Text "Apple HIG：更重视留白、克制和精致感，适合轻量、消费级、偏高级感的工具。" 100 1220 22 "#374151"
Text "游戏启动器：海报和启动动作优先，适合游戏、AI WebUI 启动器、模型包管理。" 100 1260 22 "#374151"
Text "Ant Design：密度高、实用性强，适合后台、配置工具、运维面板。" 100 1300 22 "#374151"
Text "玻璃拟态：视觉吸引力强但可读性风险更高，更适合概念页或重点展示区。" 100 1340 22 "#374151"

$bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()
Write-Output $outPath
