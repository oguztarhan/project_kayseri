<#
Lig (leaderboard) tasarim seti -> oyunun kullandigi sprite kiti.

    powershell -NoProfile -ExecutionPolicy Bypass -File Tools/ui/lig_tasarim_kiti.ps1

The source is the 17-piece set in "Assets/UI DESIGNS/leaderboard ui" (the folder above it carries a
Turkish dotted capital I, which Windows PowerShell 5.1 misreads in a BOM-less script -- hence this
file stays ASCII and finds the folders by wildcard). The set is left untouched; everything here is
derived, the same way Tools/ui/deniz_tasarim_kiti.ps1 derives the sea kit:

- TRIMMED to the art. The exports carry wide transparent margins, and PillFit maps a sprite's FULL
  height onto its box -- a 724-px row with a 430-px body would draw a third too thin.
- DOWNSCALED to the size the screen actually shows (1-2k px sources for 100-600 px boxes).
- ALPHA CLAMPED. Alpha >= 240 becomes 255 so nothing shows through a card body; edges keep their
  anti-aliasing.
- The two FRAMES (board panel, reward popup) have their vertical middle COLLAPSED. Both are
  nine-sliced, so the middle is stretched at draw time no matter how many rows it was authored with;
  keeping 160 of 1370 rows is the same picture for a fraction of the memory.
- The REWARD STRIP is SPLIT into halves at its winged star, so each half nine-slices with the
  ornament inside its inner border and the star is never stretched. Same trick as the sea kit's
  route tab and title plate.

EVERY BORDER IS MEASURED, NOT TYPED. The first draft of this script carried the numbers as constants
read off the exports inside Unity -- and every one of them was wrong, because Unity's importer had
resized the non-power-of-two sources (the ribbon is 2172x724 on disk and 2048x512 once imported, a
different aspect ratio, so even the fractions did not survive). GDI reads the file, so the numbers
are taken here: a frame's borders are the gap between its trimmed edge and its inner field, a
capsule's caps are found from its gold ornaments, and the reward strip's split is found from the
column where its star rises above the bar. Re-export the art at any size and the kit still lands.

WHY THE BOARD PANEL IS NOT SPLIT at its star bow. A nine-slice stretches its top-CENTRE segment
horizontally, and the bow lives in that segment -- so the bow is stretched by exactly the ratio of
the drawn width to the art width, and by nothing else. The panel is exported at its full trimmed
width and drawn into a box of about the same number of units, which makes that ratio 1.0. Splitting
the bow off would buy nothing and cost a seam.

Output: kit sprites -> Assets/Art/UI/LigKiti (packed by Resources/UI/Lig/LigKiti.spriteatlasv2),
board panel -> Assets/Resources/UI/Lig/lig_pano.png (at ~970 px wide it would be most of an atlas page).

Import settings are set once in Unity and live in the .meta files, which survive a re-run: Sprite
(2D and UI), single, no mipmaps, clamp, no NPOT rescale. The nine-slice borders (L, B, R, T) in
output pixels are printed at the end of a run -- re-apply them in Unity if the art changes.
#>
$ErrorActionPreference = 'Stop'

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class LigKit
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

    // BGRA in memory.
    static bool Solid(byte[] px, int i, int a) { return px[i + 3] > a; }
    static bool Bright(byte[] px, int i)
    {
        return px[i + 3] > 160 && px[i] > 205 && px[i + 1] > 205 && px[i + 2] > 205;
    }
    static bool Gold(byte[] px, int i)
    {
        int bl = px[i], gr = px[i + 1], r = px[i + 2];
        return px[i + 3] > 160 && r > 200 && gr > 140 && gr < 235 && bl < 110;
    }

    /// <summary>Smallest rect holding every pixel above the alpha threshold, grown by pad.</summary>
    public static Rectangle Bounds(Bitmap b, int threshold, int pad)
    {
        int stride; var px = Read(b, out stride);
        int x0 = b.Width, y0 = b.Height, x1 = -1, y1 = -1;
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
                if (Solid(px, y * stride + x * 4, threshold))
                {
                    if (x < x0) x0 = x; if (x > x1) x1 = x;
                    if (y < y0) y0 = y; if (y > y1) y1 = y;
                }
        x0 = Math.Max(0, x0 - pad); y0 = Math.Max(0, y0 - pad);
        x1 = Math.Min(b.Width - 1, x1 + pad); y1 = Math.Min(b.Height - 1, y1 + pad);
        return new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    /// <summary>
    /// A frame's nine-slice borders (L, B, R, T) in its own pixels: the gap between the trimmed edge
    /// and the inner field the frame surrounds.
    ///
    /// Measured as the LONGEST bright run down the centre column and across the centre row, not as a
    /// bounding box of every bright pixel -- both frames carry white specular highlights on their
    /// ornaments, and a bounding box would swallow the crown and report a top border of nothing.
    /// </summary>
    public static int[] FrameBorders(Bitmap b)
    {
        int stride; var px = Read(b, out stride);
        int cx = b.Width / 2, cy = b.Height / 2;

        int y0 = 0, y1 = -1, run = -1;
        for (int y = 0; y <= b.Height; y++)
        {
            bool ok = y < b.Height && Bright(px, y * stride + cx * 4);
            if (ok) { if (run < 0) run = y; }
            else if (run >= 0) { if (y - 1 - run > y1 - y0) { y0 = run; y1 = y - 1; } run = -1; }
        }
        int x0 = 0, x1 = -1; run = -1;
        for (int x = 0; x <= b.Width; x++)
        {
            bool ok = x < b.Width && Bright(px, cy * stride + x * 4);
            if (ok) { if (run < 0) run = x; }
            else if (run >= 0) { if (x - 1 - run > x1 - x0) { x0 = run; x1 = x - 1; } run = -1; }
        }
        return new int[] { x0, b.Height - 1 - y1, b.Width - 1 - x1, y0 };
    }

    /// <summary>
    /// First and last column carrying gold, and the ends of the outermost gold runs:
    /// {firstRunStart, firstRunEnd, lastRunStart, lastRunEnd}. The ribbon's two buckles and the
    /// player row's star are the ornaments a nine-slice cap has to contain.
    /// </summary>
    public static int[] GoldRuns(Bitmap b)
    {
        int stride; var px = Read(b, out stride);
        int fs = -1, fe = -1, ls = -1, le = -1, run = -1;
        for (int x = 0; x <= b.Width; x++)
        {
            bool ok = false;
            if (x < b.Width)
                for (int y = 0; y < b.Height && !ok; y++) ok = Gold(px, y * stride + x * 4);
            if (ok) { if (run < 0) run = x; }
            else if (run >= 0)
            {
                if (fs < 0) { fs = run; fe = x - 1; }
                ls = run; le = x - 1; run = -1;
            }
        }
        return new int[] { fs, fe, ls, le };
    }

    /// <summary>
    /// The columns where an ornament rises above the bar it decorates: {x0, x1}. The bar's own top is
    /// taken from the left quarter, away from any centred ornament.
    /// </summary>
    public static int[] OrnamentSpan(Bitmap b, int alpha)
    {
        int stride; var px = Read(b, out stride);
        var top = new int[b.Width];
        for (int x = 0; x < b.Width; x++)
        {
            top[x] = b.Height;
            for (int y = 0; y < b.Height; y++) if (Solid(px, y * stride + x * 4, alpha)) { top[x] = y; break; }
        }
        int rail = 0;
        for (int x = b.Width / 8; x < b.Width / 4; x++) if (top[x] > rail && top[x] < b.Height) rail = top[x];
        int o0 = -1, o1 = -1;
        int margin = Math.Max(4, b.Height / 50);
        for (int x = 0; x < b.Width; x++) if (top[x] < rail - margin) { if (o0 < 0) o0 = x; o1 = x; }
        return new int[] { o0, o1 };
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

    /// <summary>
    /// A nine-sliced frame with its stretchable middle thrown away: the top border rows, then keep
    /// rows resampled out of the middle, then the bottom border rows. The slicer stretches whatever
    /// is between the borders, so the rows dropped here were never drawn at their authored height.
    /// </summary>
    public static Bitmap CollapseRows(Bitmap b, int top, int bottom, int keep)
    {
        int middle = b.Height - top - bottom;
        if (middle <= keep) return Crop(b, new Rectangle(0, 0, b.Width, b.Height));

        var d = new Bitmap(b.Width, top + keep + bottom, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(d))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(b, new Rectangle(0, 0, b.Width, top), 0, 0, b.Width, top, GraphicsUnit.Pixel);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(b, new Rectangle(0, top, b.Width, keep), 0, top, b.Width, middle, GraphicsUnit.Pixel);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(b, new Rectangle(0, top + keep, b.Width, bottom),
                           0, b.Height - bottom, b.Width, bottom, GraphicsUnit.Pixel);
        }
        return d;
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
          Where-Object { $_.Name -like 'leaderboard*' } | Select-Object -First 1
if ($null -eq $set) { throw 'Design set not found under Assets/UI DES*/leaderboard*' }

$kitDir = Join-Path $assets 'Art\UI\LigKiti'
$resDir = Join-Path $assets 'Resources\UI\Lig'
New-Item -ItemType Directory -Force -Path $kitDir | Out-Null

function Src([string]$name) {
    $f = Join-Path $set.FullName "leaderboard_$name.png"
    if (-not (Test-Path $f)) { throw "Missing design leaderboard_$name.png" }
    return $f
}

$report = New-Object System.Collections.Generic.List[string]
function Note($name, $bmp, $border) {
    $script:report.Add(('{0,-18} {1,4}x{2,-4} border {3}' -f $name, $bmp.Width, $bmp.Height, $border))
}
function Border($l, $b, $r, $t) { return ('({0}, {1}, {2}, {3})' -f [int]$l, [int]$b, [int]$r, [int]$t) }

# Trim to the art and flatten the near-opaque body. Everything downstream works on this.
function Trim([string]$name) {
    $raw = [LigKit]::Load((Src $name))
    $cut = [LigKit]::Crop($raw, [LigKit]::Bounds($raw, 8, 2))
    [LigKit]::ClampAlpha($cut, 240)
    $raw.Dispose()
    return $cut
}

function ToHeight($bmp, [int]$h) {
    $w = [int][math]::Round($bmp.Width * $h / $bmp.Height)
    return [LigKit]::Scale($bmp, $w, $h)
}

# ---- icons: chests, medals, reward icons, the two button faces ---------------------------------
# source name, kit name, long side in px
$icons = @(
    @('chest_gold',              'sandik_altin',  256),
    @('chest_silver',            'sandik_gumus',  256),
    @('chest_bronze',            'sandik_bronz',  256),
    @('chest_plain',             'sandik_sade',   256),
    @('medal_gold',              'madalya_altin', 224),
    @('medal_silver',            'madalya_gumus', 224),
    @('medal_bronze',            'madalya_bronz', 224),
    @('gem_reward_icon',         'elmas',         176),
    @('master_card_reward_icon', 'usta_kart',     176),
    @('close_icon',              'kapat',         176),
    @('opener_icon',             'lig_ikon',      256)
)
foreach ($p in $icons) {
    $cut = Trim $p[0]
    $s = $p[2] / [math]::Max($cut.Width, $cut.Height)
    $bmp = [LigKit]::Scale($cut, [int][math]::Round($cut.Width * $s), [int][math]::Round($cut.Height * $s))
    [LigKit]::Save($bmp, (Join-Path $kitDir ($p[1] + '.png')))
    Note $p[1] $bmp '-'
    $cut.Dispose(); $bmp.Dispose()
}

# ---- capsules: nine-sliced across only, caps found from the art --------------------------------
# PillFit rescales the caps with the box at draw time, so what matters is that each cap CONTAINS its
# ornament; the exact pixel is cosmetic. The ribbon's caps must hold its flared tail and its gold
# buckle; the player row's left cap must hold its star, its right cap is the pill's round end.
$capsReport = @{}
foreach ($p in @(@('ribbon', 'serit', 190), @('player_row', 'satir_sen', 150), @('claim_button', 'al_butonu', 230))) {
    $cut = Trim $p[0]
    $bmp = ToHeight $cut $p[2]
    $g = [LigKit]::GoldRuns($bmp)
    $round = [int][math]::Round($bmp.Height * 0.52)   # a capsule's semicircular end
    if ($p[1] -eq 'serit')          { $l = $g[1] + 10; $r = $bmp.Width - $g[2] + 10 }
    elseif ($p[1] -eq 'satir_sen')  { $l = $g[1] + 10; $r = $round }
    else                            { $l = $round;     $r = $round }
    # A nine-slice cannot have its caps meet: leave at least a few columns to stretch.
    $max = [int][math]::Floor(($bmp.Width - 8) / 2)
    if ($l -gt $max) { $l = $max }
    if ($r -gt $max) { $r = $max }
    [LigKit]::Save($bmp, (Join-Path $kitDir ($p[1] + '.png')))
    Note $p[1] $bmp (Border $l 0 $r 0)
    $capsReport[$p[1]] = "gold runs $($g[0])..$($g[1]) / $($g[2])..$($g[3])"
    $cut.Dispose(); $bmp.Dispose()
}

# ---- the reward strip, split at its winged star -------------------------------------------------
$cut = Trim 'reward_strip'
$strip = ToHeight $cut 210
$orn = [LigKit]::OrnamentSpan($strip, 140)
$mid = [int][math]::Round(($orn[0] + $orn[1]) / 2.0)
$sL = [LigKit]::Crop($strip, (New-Object System.Drawing.Rectangle 0, 0, $mid, $strip.Height))
$sR = [LigKit]::Crop($strip, (New-Object System.Drawing.Rectangle $mid, 0, ($strip.Width - $mid), $strip.Height))
[LigKit]::Save($sL, (Join-Path $kitDir 'odul_serit_sol.png'))
[LigKit]::Save($sR, (Join-Path $kitDir 'odul_serit_sag.png'))
# Inner border = from the split back out to where the ornament starts; outer = the bar's round end.
$ornHalf = $mid - $orn[0] + 8
$endCap  = [int][math]::Round($strip.Height * 0.42)
Note 'odul_serit_sol' $sL (Border $endCap 0 $ornHalf 0)
Note 'odul_serit_sag' $sR (Border ($orn[1] - $mid + 8) 0 $endCap 0)
$report.Add("  reward strip ornament x $($orn[0])..$($orn[1]) of $($strip.Width), split at $mid")
$cut.Dispose(); $strip.Dispose(); $sL.Dispose(); $sR.Dispose()

# ---- frames: nine-sliced both ways, borders measured, vertical middle collapsed -----------------
# source, kit name, target width (0 = keep trimmed size), middle rows to keep, destination
foreach ($p in @(@('reward_popup', 'odul_pano', 660, 120, $kitDir),
                 @('panel',        'lig_pano',    0, 160, $resDir))) {
    $cut = Trim $p[0]
    $target = $p[2]; if ($target -le 0) { $target = $cut.Width }
    $k = $target / $cut.Width
    $sc = [LigKit]::Scale($cut, $target, [int][math]::Round($cut.Height * $k))
    $b4 = [LigKit]::FrameBorders($sc)
    $out = [LigKit]::CollapseRows($sc, $b4[3], $b4[1], $p[3])
    [LigKit]::Save($out, (Join-Path $p[4] ($p[1] + '.png')))
    Note $p[1] $out (Border $b4[0] $b4[1] $b4[2] $b4[3])
    $cut.Dispose(); $sc.Dispose(); $out.Dispose()
}

$report | ForEach-Object { $_ }
$capsReport.GetEnumerator() | ForEach-Object { '  {0}: {1}' -f $_.Key, $_.Value }
