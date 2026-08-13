param(
    [Parameter(Mandatory = $true)][string]$InputPng,
    [Parameter(Mandatory = $true)][string]$OutputIco
)

Add-Type -AssemblyName PresentationCore

$sourcePath = (Resolve-Path -LiteralPath $InputPng).Path
$stream = [System.IO.File]::OpenRead($sourcePath)
try {
    $decoder = [System.Windows.Media.Imaging.PngBitmapDecoder]::new(
        $stream,
        [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
        [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    $source = $decoder.Frames[0]
}
finally {
    $stream.Dispose()
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$images = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    $scaleX = $size / $source.PixelWidth
    $scaleY = $size / $source.PixelHeight
    $scaled = [System.Windows.Media.Imaging.TransformedBitmap]::new(
        $source,
        [System.Windows.Media.ScaleTransform]::new($scaleX, $scaleY))
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($scaled))
    $memory = [System.IO.MemoryStream]::new()
    $encoder.Save($memory)
    $images.Add($memory.ToArray())
    $memory.Dispose()
}

$outputPath = [System.IO.Path]::GetFullPath($OutputIco)
$outputDirectory = [System.IO.Path]::GetDirectoryName($outputPath)
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$file = [System.IO.File]::Create($outputPath)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$images.Count)
    $offset = 6 + (16 * $images.Count)
    for ($index = 0; $index -lt $images.Count; $index++) {
        $size = $sizes[$index]
        $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $images[$index].Length
    }
    foreach ($image in $images) { $writer.Write($image) }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

Write-Output $outputPath
