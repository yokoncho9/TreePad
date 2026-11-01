Add-Type -AssemblyName System.Drawing

$outputPath = "D:\Projects\TreePad\src\TreePad\Assets\app.ico"
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$bitmaps = @()

function Create-RoundedRectPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $maxR = [Math]::Min($w / 2.0, $h / 2.0)
    $r = [Math]::Min($r, $maxR)
    if ($r -lt 0.5) { $r = 0.5 }
    $path.AddArc($x, $y, (2 * $r), (2 * $r), 180, 90)
    $path.AddArc(($x + $w - 2 * $r), $y, (2 * $r), (2 * $r), 270, 90)
    $path.AddArc(($x + $w - 2 * $r), ($y + $h - 2 * $r), (2 * $r), (2 * $r), 0, 90)
    $path.AddArc($x, ($y + $h - 2 * $r), (2 * $r), (2 * $r), 90, 90)
    $path.CloseFigure()
    return $path
}

foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $scale = [float]$size / 256.0

    # 1. 그림자
    if ($size -ge 32) {
        $sX = [float](18.0 * $scale)
        $sY = [float](22.0 * $scale)
        $sW = [float](220.0 * $scale)
        $sH = [float](224.0 * $scale)
        $sR = [float](28.0 * $scale)
        $sBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(45, 0, 0, 0))
        $sPath = Create-RoundedRectPath $sX $sY $sW $sH $sR
        $g.FillPath($sBrush, $sPath)
        $sBrush.Dispose(); $sPath.Dispose()
    }

    # 2. 메인 패드 외형 (딥 틸 / 오션 그라데이션)
    $pX = [float](16.0 * $scale)
    $pY = [float](16.0 * $scale)
    $pW = [float](224.0 * $scale)
    $pH = [float](224.0 * $scale)
    $pR = [float](28.0 * $scale)

    $padBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF($pX, $pY)),
        (New-Object System.Drawing.PointF($pX, ($pY + $pH))),
        [System.Drawing.Color]::FromArgb(255, 14, 165, 233),   # Ocean Blue
        [System.Drawing.Color]::FromArgb(255, 13, 115, 110)    # Teal 700
    )
    $padPath = Create-RoundedRectPath $pX $pY $pW $pH $pR
    $g.FillPath($padBrush, $padPath)
    $padBrush.Dispose()

    # 상단 빛 반사 하이라이트
    if ($size -ge 32) {
        $hiH = [float](60.0 * $scale)
        $hiBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            (New-Object System.Drawing.PointF($pX, $pY)),
            (New-Object System.Drawing.PointF($pX, ($pY + $hiH))),
            [System.Drawing.Color]::FromArgb(45, 255, 255, 255),
            [System.Drawing.Color]::FromArgb(0, 255, 255, 255)
        )
        $hiPath = Create-RoundedRectPath $pX $pY $pW $hiH $pR
        $g.FillPath($hiBrush, $hiPath)
        $hiBrush.Dispose(); $hiPath.Dispose()
    }

    # 패드 외곽선
    $bPenW = [Math]::Max([float]1.0, [float](1.5 * $scale))
    $bPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(50, 255, 255, 255), $bPenW)
    $g.DrawPath($bPen, $padPath)
    $bPen.Dispose(); $padPath.Dispose()

    # 3. 좌측 트리 패널 (반투명 다크 배경)
    $tpX = [float](26.0 * $scale)
    $tpY = [float](26.0 * $scale)
    $tpW = [float](80.0 * $scale)
    $tpH = [float](204.0 * $scale)
    $tpR = [float](14.0 * $scale)

    $tpBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(50, 0, 20, 30))
    $tpPath = Create-RoundedRectPath $tpX $tpY $tpW $tpH $tpR
    $g.FillPath($tpBrush, $tpPath)
    $tpBrush.Dispose(); $tpPath.Dispose()

    # 좌측 트리 가지선
    $treeLineW = [Math]::Max([float]1.2, [float](3.5 * $scale))
    $treePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(210, 255, 255, 255), $treeLineW)
    $treePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $treePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    $rootX = [float](46.0 * $scale)
    $topY  = [float](60.0 * $scale)
    $botY  = [float](190.0 * $scale)
    $g.DrawLine($treePen, $rootX, $topY, $rootX, $botY)

    # 3개 가지선
    $b1Y = [float](95.0 * $scale)
    $b2Y = [float](140.0 * $scale)
    $b3Y = [float](185.0 * $scale)
    $branchEndX = [float](58.0 * $scale)

    $g.DrawLine($treePen, $rootX, $b1Y, $branchEndX, $b1Y)
    $g.DrawLine($treePen, $rootX, $b2Y, $branchEndX, $b2Y)
    $g.DrawLine($treePen, $rootX, $b3Y, $branchEndX, $b3Y)
    $treePen.Dispose()

    # 민트 액센트 트리 노드 포인트 (Dots)
    $dotBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 52, 211, 153))

    $rootDotR = [Math]::Max([float]2.0, [float](6.0 * $scale))
    $g.FillEllipse($dotBrush, ($rootX - $rootDotR), ($topY - $rootDotR), (2 * $rootDotR), (2 * $rootDotR))

    $chDotR = [Math]::Max([float]1.5, [float](4.5 * $scale))
    $g.FillEllipse($dotBrush, ($branchEndX - $chDotR), ($b1Y - $chDotR), (2 * $chDotR), (2 * $chDotR))
    $g.FillEllipse($dotBrush, ($branchEndX - $chDotR), ($b2Y - $chDotR), (2 * $chDotR), (2 * $chDotR))
    $g.FillEllipse($dotBrush, ($branchEndX - $chDotR), ($b3Y - $chDotR), (2 * $chDotR), (2 * $chDotR))
    $dotBrush.Dispose()

    # 좌측 미니 바 (해상도 32 이상)
    if ($size -ge 32) {
        $miniBarBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(180, 255, 255, 255))
        $bX = [float]($branchEndX + 6.0 * $scale)
        $bH = [Math]::Max([float]1.5, [float](5.0 * $scale))
        $bRad = [float]($bH / 2.0)

        $bar1W = [float](34.0 * $scale)
        $bar1Y = [float]($b1Y - $bRad)
        $bar1 = Create-RoundedRectPath $bX $bar1Y $bar1W $bH $bRad
        $g.FillPath($miniBarBrush, $bar1); $bar1.Dispose()

        $bar2W = [float](28.0 * $scale)
        $bar2Y = [float]($b2Y - $bRad)
        $bar2 = Create-RoundedRectPath $bX $bar2Y $bar2W $bH $bRad
        $g.FillPath($miniBarBrush, $bar2); $bar2.Dispose()

        $bar3W = [float](32.0 * $scale)
        $bar3Y = [float]($b3Y - $bRad)
        $bar3 = Create-RoundedRectPath $bX $bar3Y $bar3W $bH $bRad
        $g.FillPath($miniBarBrush, $bar3); $bar3.Dispose()
        $miniBarBrush.Dispose()
    }

    # 4. 우측 화이트 메모 패드 영역 (Text Document Sheet)
    $docX = [float](114.0 * $scale)
    $docY = [float](26.0 * $scale)
    $docW = [float](116.0 * $scale)
    $docH = [float](204.0 * $scale)
    $docR = [float](14.0 * $scale)

    $docBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(250, 255, 255, 255))
    $docPath = Create-RoundedRectPath $docX $docY $docW $docH $docR
    $g.FillPath($docBrush, $docPath)
    $docBrush.Dispose(); $docPath.Dispose()

    # 우측 본문 제목 라인
    $titleX = [float]($docX + 12.0 * $scale)
    $titleY = [float]($docY + 22.0 * $scale)
    $titleW = [float](65.0 * $scale)
    $titleH = [Math]::Max([float]2.0, [float](8.0 * $scale))
    $titleR = [float]($titleH / 2.0)

    $titleBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 13, 115, 110))
    $tPath = Create-RoundedRectPath $titleX $titleY $titleW $titleH $titleR
    $g.FillPath($titleBrush, $tPath)
    $titleBrush.Dispose(); $tPath.Dispose()

    # 본문 세부 라인들
    $bodyLineH = [Math]::Max([float]1.2, [float](6.0 * $scale))
    $bodyR = [float]($bodyLineH / 2.0)
    $bodyBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(150, 70, 90, 95))
    $lineSpacing = [float](16.0 * $scale)
    $startY = [float]($docY + 44.0 * $scale)

    $lineWidths = @(88.0, 78.0, 84.0, 64.0, 80.0, 50.0)
    for ($i = 0; $i -lt $lineWidths.Count; $i++) {
        $curY = [float]($startY + ($i * $lineSpacing))
        if (($curY + $bodyLineH) -lt ($docY + $docH - 12.0 * $scale)) {
            $curW = [float]($lineWidths[$i] * $scale)
            $lPath = Create-RoundedRectPath $titleX $curY $curW $bodyLineH $bodyR
            $g.FillPath($bodyBrush, $lPath)
            $lPath.Dispose()
        }
    }
    $bodyBrush.Dispose()

    $g.Dispose()
    $bitmaps += $bmp
}

# ICO 바이너리 포맷 패키징
$fs = [System.IO.File]::Create($outputPath)
$bw = New-Object System.IO.BinaryWriter($fs)

$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$bitmaps.Count)

$pngBytesList = @()
foreach ($b in $bitmaps) {
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytesList += ,$ms.ToArray()
    $ms.Dispose()
}

$offset = 6 + (16 * $bitmaps.Count)
for ($i = 0; $i -lt $bitmaps.Count; $i++) {
    $b = $bitmaps[$i]
    $w = if ($b.Width -ge 256) { 0 } else { [byte]$b.Width }
    $h = if ($b.Height -ge 256) { 0 } else { [byte]$b.Height }
    $pngLen = $pngBytesList[$i].Length

    $bw.Write([byte]$w)
    $bw.Write([byte]$h)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$pngLen)
    $bw.Write([uint32]$offset)

    $offset += $pngLen
}

for ($i = 0; $i -lt $bitmaps.Count; $i++) {
    $bw.Write($pngBytesList[$i])
    $bitmaps[$i].Dispose()
}

$bw.Flush()
$bw.Close()
$fs.Close()

Write-Output "SUCCESS: TreePad app.ico created successfully at $outputPath"
