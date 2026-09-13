param([switch]$InspectOnly)

# Generate Windows multi-resolution icons from the original artwork using GDI+.
# No external image tools are required. Run from any working directory.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$destination = Join-Path $root 'src\Juju.App\Assets'
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

foreach ($name in @('juju', 'json')) {
    $source = [System.Drawing.Bitmap]::new((Join-Path $root "$name.png"))
    try {
        Write-Output "$name.png: $($source.Width)x$($source.Height), $($source.PixelFormat), corner alpha=$($source.GetPixel(0, 0).A)"
        if ($InspectOnly) { continue }
        [System.IO.Directory]::CreateDirectory($destination) | Out-Null
        $frames = @()
        foreach ($size in $sizes) {
            $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            $stream = [System.IO.MemoryStream]::new()
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $scale = $size / [double][Math]::Max($source.Width, $source.Height)
                $width = [single]($source.Width * $scale)
                $height = [single]($source.Height * $scale)
                $graphics.DrawImage($source, [single](($size - $width) / 2), [single](($size - $height) / 2), $width, $height)
                # DIB frames work with both WPF and System.Drawing.Icon (tray).
                # ICO stores an XOR bitmap plus a bottom-up, 1-bit AND mask.
                $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Bmp)
                $bmp = $stream.ToArray()
                $dib = [byte[]]$bmp[14..($bmp.Length - 1)]
                [BitConverter]::GetBytes([int]($size * 2)).CopyTo($dib, 8)
                $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
                $mask = [byte[]]::new($maskStride * $size)
                for ($y = 0; $y -lt $size; $y++) {
                    for ($x = 0; $x -lt $size; $x++) {
                        if ($bitmap.GetPixel($x, $y).A -eq 0) {
                            $index = ($size - 1 - $y) * $maskStride + [int][Math]::Floor($x / 8.0)
                            $mask[$index] = $mask[$index] -bor (128 -shr ($x % 8))
                        }
                    }
                }
                $frames += ,([byte[]]($dib + $mask))
            }
            finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
        }

        $output = [System.IO.File]::Create((Join-Path $destination "$name.ico"))
        $writer = [System.IO.BinaryWriter]::new($output)
        try {
            $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
            $offset = 6 + 16 * $sizes.Count
            for ($i = 0; $i -lt $sizes.Count; $i++) {
                $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
                $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
                $writer.Write([byte]0); $writer.Write([byte]0)
                $writer.Write([uint16]1); $writer.Write([uint16]32)
                $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
                $offset += $frames[$i].Length
            }
            foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
        }
        finally { $writer.Dispose() }
        Write-Output "Generated Assets/$name.ico: $($sizes -join ', ') px"
    }
    finally { $source.Dispose() }
}
