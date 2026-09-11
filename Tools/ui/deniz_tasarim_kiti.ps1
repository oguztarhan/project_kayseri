<#
Deniz ekrani tasarim seti -> oyunun kullandigi sprite kiti.

    powershell -NoProfile -ExecutionPolicy Bypass -File Tools/ui/deniz_tasarim_kiti.ps1

The source is the 25-piece set in "Assets/UI DESIGNS/Deniz ekrani tasarim" (the folder name carries a
Turkish dotted capital I, which Windows PowerShell 5.1 misreads in a BOM-less script -- hence this file
stays ASCII and finds the folders by wildcard). The set is left untouched; everything here is derived:

- TRIMMED to the art. The exports carry wide transparent margins, and PillFit maps a sprite's FULL
  height onto its box -- a 765-px button with a 519-px body would draw a third too thin.
- DOWNSCALED to the size the screen actually shows (1-2k px sources for 100-600 px boxes).
- ALPHA CLAMPED. Every body is ~99% opaque (A 250-254) in the export, which lets whatever sits behind
  a card show through it. Alpha >= 240 becomes 255; edges keep their anti-aliasing.
- The sea backdrop is made FULLY OPAQUE -- the export is 72-245 alpha across the whole picture.
- The HP bar is SPLIT into a frame and a fill. The export has its red fill painted in at ~75%; the
  frame gets the empty trough stamped over the red, the fill is keyed out of the red by hue.
- The two ornamented plates (route tab, title plate) are SPLIT into halves at the ornament, so each
  half nine-slices with the ornament inside its inner border and nothing ever stretches it.

Output: kit sprites -> Assets/Art/UI/DenizKiti (packed by Resources/UI/Sea/DenizKiti.spriteatlasv2),
backdrop -> Assets/Resources/UI/Sea/deniz_arka.png (too big to share an atlas page).

Import settings are set once in Unity and live in the .meta files, which survive a re-run: Sprite
(2D and UI), single, no mipmaps, clamp. Nine-slice borders (L, B, R, T) in output pixels are printed
at the end of a run -- re-apply them if a size in PIECES changes.
#>
$ErrorActionPreference = 'Stop'

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class DenizKit
{
    public static Bitmap Load(string path)
    {
        using (var b = new Bitmap(path))
        {
            var copy = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(copy))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(b, new Rectangle(0, 0, b.Width, b.Height));
            }
            return copy;
        }
    }

    static byte[] Read(Bitmap b, out int stride)
    {
        var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        stride = d.Stride;
        var px = new byte[d.Stride * b.Height];
        Marshal.Copy(d.Scan0, px, 0, px.Length);
        b.UnlockBits(d);
        return px;
    }

    static void Write(Bitmap b, byte[] px)
    {
        var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(px, 0, d.Scan0, px.Length);
        b.UnlockBits(d);
    }

    /// <summary>Smallest rect holding every pixel above the alpha threshold, grown by pad.</summary>
    public static Rectangle Bounds(Bitmap b, int threshold, int pad)
    {
        int stride; var px = Read(b, out stride);
        int x0 = b.Width, y0 = b.Height, x1 = -1, y1 = -1;
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
                if (px[y * stride + x * 4 + 3] > threshold)
                {
                    if (x < x0) x0 = x; if (x > x1) x1 = x;
                    if (y < y0) y0 = y; if (y > y1) y1 = y;
                }
        x0 = Math.Max(0, x0 - pad); y0 = Math.Max(0, y0 - pad);
        x1 = Math.Min(b.Width - 1, x1 + pad); y1 = Math.Min(b.Height - 1, y1 + pad);
        return new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    public static Bitmap Crop(Bitmap b, Rectangle r)
    {
        return b.Clone(r, PixelFormat.Format32bppArgb);
    }

    public static Bitmap Scale(Bitmap b, int w, int h)
    {
        var d = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(d))
        using (var ia = new ImageAttributes())
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            ia.SetWrapMode(WrapMode.TileFlipXY);   // no transparent fringe sampled from outside
            g.DrawImage(b, new Rectangle(0, 0, w, h), 0, 0, b.Width, b.Height, GraphicsUnit.Pixel, ia);
        }
        return d;
    }

    public static void ClampAlpha(Bitmap b, int above)
    {
        int stride; var px = Read(b, out stride);
        for (int i = 3; i < px.Length; i += 4) if (px[i] >= above) px[i] = 255;
        Write(b, px);
    }

    public static void Opaque(Bitmap b)
    {
        int stride; var px = Read(b, out stride);
        for (int i = 3; i < px.Length; i += 4) px[i] = 255;
        Write(b, px);
    }

    static int Redness(byte[] px, int i)
    {
        int bl = px[i], gr = px[i + 1], r = px[i + 2];
        return r - Math.Max(gr, bl);
    }

    /// <summary>
    /// The HP frame's empty trough, rebuilt: every column in [x0, x1) is replaced with column refX,
    /// which sits in the empty part of the same bar -- the bar is uniform across its middle. Columns
    /// in [xSoft, x0) are only patched where the pixel is red, because the heart's outline lives there.
    /// </summary>
    public static void StampTrough(Bitmap b, int refX, int xSoft, int x0, int x1, int y0, int y1, int redAbove)
    {
        int stride; var px = Read(b, out stride);
        for (int y = y0; y <= y1; y++)
        {
            int src = y * stride + refX * 4;
            for (int x = xSoft; x < x1; x++)
            {
                int i = y * stride + x * 4;
                if (x < x0 && Redness(px, i) <= redAbove) continue;
                px[i] = px[src]; px[i + 1] = px[src + 1]; px[i + 2] = px[src + 2]; px[i + 3] = px[src + 3];
            }
        }
        Write(b, px);
    }

    /// <summary>Keeps only the red: alpha ramps from 0 at redness lo to full at hi.</summary>
    public static void KeyRed(Bitmap b, int lo, int hi)
    {
        int stride; var px = Read(b, out stride);
        for (int i = 0; i < px.Length; i += 4)
        {
            int k = (Redness(px, i) - lo) * 255 / (hi - lo);
            if (k < 0) k = 0; if (k > 255) k = 255;
            px[i + 3] = (byte)(px[i + 3] * k / 255);
        }
        Write(b, px);
    }

    /// <summary>First x at or right of x0 on row y whose pixel reads as the cream liner.</summary>
    public static int RunToCream(Bitmap b, int x0, int y)
    {
        int stride; var px = Read(b, out stride);
        for (int x = x0; x < b.Width; x++)
        {
            int i = y * stride + x * 4;
            if (px[i + 2] > 200 && px[i + 1] > 190 && px[i] > 150) return x;
        }
        return -1;
    }

    public static void Save(Bitmap b, string path)
    {
        b.Save(path, ImageFormat.Png);
    }
}
'@

$repo   = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$assets = Join-Path $repo 'Assets'
$set    = Get-ChildItem $assets -Directory | Where-Object { $_.Name -like 'UI DES*' } |
          ForEach-Object { Get-ChildItem $_.FullName -Directory } |
          Where-Object { $_.Name -like 'Deniz ekran*' } | Select-Object -First 1
if ($null -eq $set) { throw 'Design set not found under Assets/UI DES*/Deniz ekran*' }

$kitDir = Join-Path $assets 'Art\UI\DenizKiti'
$bgDir  = Join-Path $assets 'Resources\UI\Sea'
New-Item -ItemType Directory -Force -Path $kitDir | Out-Null

function Src([string]$prefix) {
    $f = Get-ChildItem $set.FullName -Filter "$prefix*.png" | Select-Object -First 1
    if ($null -eq $f) { throw "Missing design $prefix" }
    return $f.FullName
}

$report = New-Object System.Collections.Generic.List[string]
function Note($name, $bmp, $border) {
    $script:report.Add(('{0,-16} {1,4}x{2,-4} border {3}' -f $name, $bmp.Width, $bmp.Height, $border))
}

# Trim + clamp + scale. Size is either the long side (icons) or the height (strips).
function Piece([string]$prefix, [string]$name, [int]$size, [string]$fit) {
    $raw = [DenizKit]::Load((Src $prefix))
    $cut = [DenizKit]::Crop($raw, [DenizKit]::Bounds($raw, 8, 4))
    [DenizKit]::ClampAlpha($cut, 240)
    if ($fit -eq 'h') { $h = $size; $w = [int][math]::Round($cut.Width * $size / $cut.Height) }
    elseif ($fit -eq 'scale') { $w = [int][math]::Round($cut.Width * $size / 100); $h = [int][math]::Round($cut.Height * $size / 100) }
    else { $s = $size / [math]::Max($cut.Width, $cut.Height); $w = [int][math]::Round($cut.Width * $s); $h = [int][math]::Round($cut.Height * $s) }
    $out = [DenizKit]::Scale($cut, $w, $h)
    $raw.Dispose(); $cut.Dispose()
    return $out
}

function Border($bmp, [double]$l, [double]$b, [double]$r, [double]$t) {
    return ('({0}, {1}, {2}, {3})' -f [int]$l, [int]$b, [int]$r, [int]$t)
}

# ---- icons and simple pieces ------------------------------------------------------------------
$icons = @(
    @('01', 'top', 256), @('02', 'zirh', 256), @('03', 'durbun', 256), @('04', 'tilsim', 256),
    @('05', 'riging', 256), @('08', 'geri', 256), @('09', 'enerji_ekle', 256), @('16', 'yildiz', 128),
    @('17', 'kaptan', 256), @('19', 'oyuncu_gemisi', 640), @('20', 'korsan_gemisi', 640),
    @('22', 'harita', 192), @('23', 'hurda', 192), @('24', 'tehlike', 192), @('25', 'kilit', 192)
)
foreach ($p in $icons) {
    $bmp = Piece $p[0] $p[1] $p[2] 'long'
    [DenizKit]::Save($bmp, (Join-Path $kitDir ($p[1] + '.png')))
    Note $p[1] $bmp '-'
    $bmp.Dispose()
}

# ---- capsules: nine-sliced across only, caps measured as fractions of the height ----------------
# name, source, height, left cap, right cap (x height)
$caps = @(
    @('06', 'ana_buton', 182, 0.87, 0.50),     # compass medallion rides the left cap
    @('07', 'oto_buton', 182, 0.50, 0.50),
    @('11', 'enerji_pili', 180, 0.80, 0.50)    # lightning bolt rides the left cap
)
foreach ($p in $caps) {
    $bmp = Piece $p[0] $p[1] $p[2] 'h'
    [DenizKit]::Save($bmp, (Join-Path $kitDir ($p[1] + '.png')))
    Note $p[1] $bmp (Border $bmp ($bmp.Height * $p[3]) 0 ($bmp.Height * $p[4]) 0)
    $bmp.Dispose()
}

# ---- framed panels: nine-sliced both ways -----------------------------------------------------
# name, source, percent scale, L B R T in TRIMMED SOURCE pixels
$frames = @(
    @('12', 'panel', 54, 80, 80, 80, 150),     # top border holds the swirl ornament
    @('14', 'slot', 30, 185, 327, 185, 185),   # bottom border holds the star strip
    @('15', 'stat', 30, 110, 70, 110, 60)
)
foreach ($p in $frames) {
    $bmp = Piece $p[0] $p[1] $p[2] 'scale'
    $k = $p[2] / 100.0
    [DenizKit]::Save($bmp, (Join-Path $kitDir ($p[1] + '.png')))
    Note $p[1] $bmp (Border $bmp ($p[3] * $k) ($p[4] * $k) ($p[5] * $k) ($p[6] * $k))
    $bmp.Dispose()
}

# ---- ornamented plates, split at the ornament -------------------------------------------------
# name, source, height, outer cap, ornament half-width (x height)
$plates = @(
    @('10', 'rota', 112, 0.42, 0.19),
    @('13', 'plaka', 180, 0.44, 0.40)
)
foreach ($p in $plates) {
    $bmp = Piece $p[0] $p[1] $p[2] 'h'
    $half = [int][math]::Floor($bmp.Width / 2)
    $left = [DenizKit]::Crop($bmp, (New-Object System.Drawing.Rectangle 0, 0, $half, $bmp.Height))
    $right = [DenizKit]::Crop($bmp, (New-Object System.Drawing.Rectangle $half, 0, ($bmp.Width - $half), $bmp.Height))
    [DenizKit]::Save($left, (Join-Path $kitDir ($p[1] + '_sol.png')))
    [DenizKit]::Save($right, (Join-Path $kitDir ($p[1] + '_sag.png')))
    $cap = $bmp.Height * $p[3]; $orn = $bmp.Height * $p[4]
    Note ($p[1] + '_sol') $left (Border $left $cap 0 $orn 0)
    Note ($p[1] + '_sag') $right (Border $right $orn 0 $cap 0)
    $bmp.Dispose(); $left.Dispose(); $right.Dispose()
}

# ---- HP bar: frame with an empty trough, and a keyed fill ---------------------------------------
# Absolute source coordinates in 21_can_bari.png (2172x724), measured off the export.
$hpSrc   = [DenizKit]::Load((Src '21'))
$hpFill  = [DenizKit]::Crop($hpSrc, (New-Object System.Drawing.Rectangle 407, 280, 1206, 152))
$hpRect  = [DenizKit]::Bounds($hpSrc, 8, 4)
$troughEnd = [DenizKit]::RunToCream($hpSrc, 1800, 356)
[DenizKit]::StampTrough($hpSrc, 1800, 355, 415, 1950, $hpRect.Top, $hpRect.Bottom - 1, 40)
$frameCut = [DenizKit]::Crop($hpSrc, $hpRect)
[DenizKit]::ClampAlpha($frameCut, 240)
$k = 0.4
$frame = [DenizKit]::Scale($frameCut, [int][math]::Round($frameCut.Width * $k), [int][math]::Round($frameCut.Height * $k))
[DenizKit]::Save($frame, (Join-Path $kitDir 'can_cerceve.png'))
Note 'can_cerceve' $frame '-'

[DenizKit]::KeyRed($hpFill, 20, 70)
$fillBox = [DenizKit]::Bounds($hpFill, 8, 2)
$fillCut = [DenizKit]::Crop($hpFill, $fillBox)
$fill = [DenizKit]::Scale($fillCut, [int][math]::Round($fillCut.Width * $k), [int][math]::Round($fillCut.Height * $k))
[DenizKit]::Save($fill, (Join-Path $kitDir 'can_dolgu.png'))
Note 'can_dolgu' $fill (Border $fill 2 0 ($fill.Height * 0.5) 0)

# Where the fill lives inside the frame, as anchors: left = the heart's outline, right = the trough's
# cream liner, bottom/top = the keyed red's own rows.
$fx0 = (407 + $fillBox.Left - $hpRect.Left) / $hpRect.Width
$fx1 = ($troughEnd - $hpRect.Left) / $hpRect.Width
$fyTop = (280 + $fillBox.Top - $hpRect.Top) / $hpRect.Height
$fyBot = (280 + $fillBox.Bottom - $hpRect.Top) / $hpRect.Height
$report.Add(('can fill anchors: x {0:0.000}..{1:0.000}  y {2:0.000}..{3:0.000} (from bottom)' -f $fx0, $fx1, (1 - $fyBot), (1 - $fyTop)))
$hpSrc.Dispose(); $hpFill.Dispose(); $frameCut.Dispose(); $fillCut.Dispose(); $frame.Dispose(); $fill.Dispose()

# ---- the sea backdrop -----------------------------------------------------------------------
$bg = [DenizKit]::Load((Src '18'))
[DenizKit]::Opaque($bg)
[DenizKit]::Save($bg, (Join-Path $bgDir 'deniz_arka.png'))
Note 'deniz_arka' $bg '-'
$bg.Dispose()

$report | ForEach-Object { $_ }
