<#
Deniz Dostlari (pet) art -> the six pet portraits and the pet chest.

    powershell -NoProfile -ExecutionPolicy Bypass -File Tools/ui/dost_kiti.ps1

The sources are the seven "Codex Gorseli 17 Eyl 2026 *.png" exports in "Assets/UI DESIGNS" (the folder
carries a Turkish dotted capital I, which Windows PowerShell 5.1 misreads in a BOM-less script -- hence
this file stays ASCII and finds the folder by wildcard). They are real transparent exports with a clean
antialiased outline (no dark glow to knock back), so each one is only:

- trimmed to its opaque bounds (alpha > 8), plus a small transparent pad so bilinear sampling at the
  edge of the sprite never reads a neighbour;
- downscaled so its long side is at most MaxSide, keeping the aspect exactly (never stretched).

The export time stamp is the only thing that tells the files apart, so the map below is by stamp.
The clam with a gem is the pet chest (the user's call, 2026-09-18), not the pearl currency icon.

Output: Assets/Art/UI/DenizDostlari/<id>.png, ids as Game.Core.Pets.Roster plus "istiridye".
#>
$ErrorActionPreference = 'Stop'

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class DostKiti
{
    public static Bitmap Load(string path)
    {
        using (var src = new Bitmap(path))
        {
            var copy = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(copy)) g.DrawImage(src, 0, 0, src.Width, src.Height);
            return copy;
        }
    }

    /// Opaque bounds: the smallest rectangle holding every pixel with alpha above the threshold.
    public static Rectangle Bounds(Bitmap b, int threshold)
    {
        var data = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var px = new int[b.Width * b.Height];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        b.UnlockBits(data);
        int minX = b.Width, minY = b.Height, maxX = -1, maxY = -1;
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
            {
                int a = (px[y * b.Width + x] >> 24) & 0xFF;
                if (a <= threshold) continue;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        return maxX < 0 ? new Rectangle(0, 0, b.Width, b.Height)
                        : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// The trimmed crop scaled into a canvas whose long side is at most maxSide, pad pixels clear all round.
    public static Bitmap Fit(Bitmap src, Rectangle crop, int maxSide, int pad)
    {
        double scale = Math.Min(1.0, (double)(maxSide - 2 * pad) / Math.Max(crop.Width, crop.Height));
        int w = (int)Math.Round(crop.Width * scale), h = (int)Math.Round(crop.Height * scale);
        var dst = new Bitmap(w + 2 * pad, h + 2 * pad, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        using (var attrs = new ImageAttributes())
        {
            g.Clear(Color.Transparent);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            // Without this the bicubic kernel samples past the crop edge and rings a faint line round it.
            attrs.SetWrapMode(WrapMode.TileFlipXY);
            g.DrawImage(src, new Rectangle(pad, pad, w, h), crop.X, crop.Y, crop.Width, crop.Height,
                        GraphicsUnit.Pixel, attrs);
        }
        return dst;
    }
}
'@

$MaxSide = 512
$Pad = 6
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$sourceDir = Get-ChildItem -LiteralPath (Join-Path $root 'Assets') -Directory |
             Where-Object { $_.Name -like 'UI DES*GNS' } | Select-Object -First 1
if ($sourceDir -eq $null) { throw 'Assets/UI DESIGNS not found' }
$outDir = Join-Path $root 'Assets/Art/UI/DenizDostlari'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

$map = [ordered]@{
    '18_10_51' = 'maymun'
    '18_10_56' = 'ahtapot'
    '18_11_03' = 'papagan'
    '18_11_09' = 'gemi_faresi'
    '18_11_15' = 'yengec'
    '18_11_21' = 'kaplumbaga'
    '18_11_27' = 'istiridye'
}

foreach ($stamp in $map.Keys) {
    $file = Get-ChildItem -LiteralPath $sourceDir.FullName -Filter "Codex*$stamp.png" | Select-Object -First 1
    if ($file -eq $null) { throw "no export stamped $stamp" }
    $src = [DostKiti]::Load($file.FullName)
    try {
        $crop = [DostKiti]::Bounds($src, 8)
        $out = [DostKiti]::Fit($src, $crop, $MaxSide, $Pad)
        try {
            $path = Join-Path $outDir ($map[$stamp] + '.png')
            $out.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
            '{0,-12} {1}x{2} -> {3}x{4}' -f $map[$stamp], $crop.Width, $crop.Height, $out.Width, $out.Height
        } finally { $out.Dispose() }
    } finally { $src.Dispose() }
}
