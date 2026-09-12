<#
Atolye / Bolum / Gorev tasarim seti -> oyunun kullandigi sprite kiti.

    powershell -NoProfile -ExecutionPolicy Bypass -File Tools/ui/atolye_tasarim_kiti.ps1

The source is the 20-piece set in "Assets/UI DESIGNS/Atolye, Bolumler ve Gorevler ekranlari" (the
folder above it carries a Turkish dotted capital I, which Windows PowerShell 5.1 misreads in a
BOM-less script -- hence this file stays ASCII and finds the folders by wildcard). The set is left
untouched; everything here is derived, the same way Tools/ui/lig_tasarim_kiti.ps1 derives the league
kit and Tools/ui/deniz_tasarim_kiti.ps1 the sea one:

- TRIMMED to the art. The exports carry wide transparent margins, and PillFit maps a sprite's FULL
  height onto its box -- a 724-px capsule with a 430-px body would draw a third too thin.
- DOWNSCALED to the size the screen actually shows (1-2k px sources for 100-600 px boxes).
- ALPHA CLAMPED. Alpha >= 240 becomes 255 so nothing shows through a card body; edges keep their
  anti-aliasing.
- The CARD PANEL has its vertical middle COLLAPSED. It is nine-sliced, so the middle is stretched at
  draw time no matter how many rows it was authored with; keeping 140 of ~1400 rows is the same
  picture for a fraction of the memory.

ONE PIECE IS DERIVED, NOT EXPORTED. The set ships a SELECTED chapter tab (gold) and a LOCKED one
(silver, with a lock badge fused into its right cap) and nothing for the third state the screen
needs: owned, but not the tab you are looking at. ChapterUI used to tint for that, which the kit
rule forbids -- the art is pre-coloured, and tinting pre-coloured art only muddies it. So 'sekme' is
the locked tab with its LEFT cap mirrored over the right end, which drops the lock and leaves a
plain silver capsule. The capsule is symmetric about its centre, so the mirror is exact, and the tab
column keeps one silhouette in all three states instead of borrowing a card for the middle one.

EVERY BORDER IS MEASURED, NOT TYPED, for the reason the league script's header records: Unity's
importer resizes non-power-of-two sources, so numbers read off the exports inside Unity describe a
picture that no longer exists. GDI reads the file, so the numbers are taken here -- the card panel's
borders are the gap between its trimmed edge and its inner field, and a capsule's caps are its
semicircular ends. Re-export the art at any size and the kit still lands.

THE RIBBON'S BAND IS MEASURED TOO. CraftingUI, ChapterUI and GoalsUI each carry a RibbonBand
constant saying where along the sprite the flat writable band sits -- the ribbon's tails hang below
it, so a title centred on the rect lands on the tails. That fraction is printed at the end of a run;
put it into the three constants rather than guessing it.

Output: kit sprites -> Assets/Art/UI/AtolyeKiti, packed by Resources/UI/Atolye/AtolyeKiti. Nothing
is kept outside the atlas: the whole kit is about 1.6M pixels against a 2048-page's 4.2M, so unlike
the sea and league kits it has no piece too big to pack.

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

public static class AtolyeKit
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
    /// bounding box of every bright pixel -- the frame carries white specular highlights on its rim,
    /// and a bounding box would swallow them and report a border of nothing.
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
    /// The first and last column of a plate's writable FIELD -- the pale inlay on the chapter tabs,
    /// gold on the selected one and silver on the locked one: {start, end}.
    ///
    /// This is what sets a tab's nine-slice caps, and it has to be measured rather than taken as a
    /// fraction of the height the way an ordinary capsule's is. The locked tab's padlock sits INSIDE
    /// its right end, a good 180 px of a 722-px plate, so a cap sized to the capsule's semicircle
    /// cuts the lock in half and the slicer then stretches what is left of it. Measured from the
    /// field, each cap holds whatever ornament that end carries -- the studs on one, the padlock on
    /// the other -- and the caps come out different widths, which is correct.
    ///
    /// Taken as the LONGEST bright run along the centre row: the field is the only long bright
    /// stretch there, and the ornaments break it at both ends.
    /// </summary>
    public static int[] FieldRun(Bitmap b, int bright)
    {
        int stride; var px = Read(b, out stride);
        int cy = b.Height / 2;
        int x0 = 0, x1 = -1, run = -1;
        for (int x = 0; x <= b.Width; x++)
        {
            bool ok = false;
            if (x < b.Width)
            {
                int i = cy * stride + x * 4;
                int max = Math.Max(px[i], Math.Max(px[i + 1], px[i + 2]));
                ok = px[i + 3] > 160 && max > bright;
            }
            if (ok) { if (run < 0) run = x; }
            else if (run >= 0) { if (x - 1 - run > x1 - x0) { x0 = run; x1 = x - 1; } run = -1; }
        }
        return new int[] { x0, x1 };
    }

    /// <summary>
    /// Where a ribbon's flat writable band sits, as a fraction of the sprite's height measured from
    /// the BOTTOM -- the fraction the screens' RibbonBand constant wants.
    ///
    /// The band is the tallest part of the ribbon: its tails hang below it and are narrower, so the
    /// centre column's solid run IS the band. Its midpoint is where a title goes.
    /// </summary>
    public static double BandCentre(Bitmap b, int alpha)
    {
        int stride; var px = Read(b, out stride);
        int cx = b.Width / 2;
        int y0 = 0, y1 = -1, run = -1;
        for (int y = 0; y <= b.Height; y++)
        {
            bool ok = y < b.Height && Solid(px, y * stride + cx * 4, alpha);
            if (ok) { if (run < 0) run = y; }
            else if (run >= 0) { if (y - 1 - run > y1 - y0) { y0 = run; y1 = y - 1; } run = -1; }
        }
        if (y1 < y0) return 0.5;
        double mid = (y0 + y1) / 2.0;
        return 1.0 - (mid / (b.Height - 1));   // GDI counts rows downward, UV space upward
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

    /// <summary>
    /// The capsule with its left half mirrored over its right -- see the header for why the
    /// unselected chapter tab is made and not exported.
    ///
    /// THE WHOLE HALF, not just the cap. The lock badge is about as wide as the capsule is tall and
    /// stands well inside the right end, so a cap-sized mirror leaves half a padlock behind (seen on
    /// the first run). Mirroring at the midpoint also costs nothing at the seam: the column drawn
    /// against the last original column is a copy of that same column, so there is no edge to blend,
    /// and the result is symmetric the way a tab should be.
    /// </summary>
    public static Bitmap MirrorRightHalf(Bitmap b)
    {
        int half = b.Width / 2;
        var d = Crop(b, new Rectangle(0, 0, b.Width, b.Height));
        using (var g = Graphics.FromImage(d))
        using (var src = Crop(b, new Rectangle(0, 0, half, b.Height)))
        {
            src.RotateFlip(RotateFlipType.RotateNoneFlipX);
            g.CompositingMode = CompositingMode.SourceCopy;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(src, new Rectangle(b.Width - half, 0, half, b.Height));
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
          Where-Object { $_.Name -like 'At*B*l*mler*G*revler*' } | Select-Object -First 1
if ($null -eq $set) { throw 'Design set not found under Assets/UI DES*/At*Bolumler*Gorevler*' }

$kitDir = Join-Path $assets 'Art\UI\AtolyeKiti'
New-Item -ItemType Directory -Force -Path $kitDir | Out-Null

function Src([string]$name) {
    $f = Join-Path $set.FullName "$name.png"
    if (-not (Test-Path $f)) { throw "Missing design $name.png" }
    return $f
}

$report = New-Object System.Collections.Generic.List[string]
function Note($name, $bmp, $border) {
    $script:report.Add(('{0,-14} {1,4}x{2,-4} border {3}' -f $name, $bmp.Width, $bmp.Height, $border))
}
function Border($l, $b, $r, $t) { return ('({0}, {1}, {2}, {3})' -f [int]$l, [int]$b, [int]$r, [int]$t) }

# Trim to the art and flatten the near-opaque body. Everything downstream works on this.
function Trim([string]$name) {
    $raw = [AtolyeKit]::Load((Src $name))
    $cut = [AtolyeKit]::Crop($raw, [AtolyeKit]::Bounds($raw, 8, 2))
    [AtolyeKit]::ClampAlpha($cut, 240)
    $raw.Dispose()
    return $cut
}

function ToHeight($bmp, [int]$h) {
    $w = [int][math]::Round($bmp.Width * $h / $bmp.Height)
    return [AtolyeKit]::Scale($bmp, $w, $h)
}

# ---- icons: the three openers, the close cross, the two reward tokens -------------------------
# Drawn at their own aspect (preserveAspect), so only the long side matters.
# source name, kit name, long side in px
$icons = @(
    @('workshop_opener_icon',       'atolye_ikon',  256),
    @('chapters_opener_icon',       'bolum_ikon',   256),
    @('goals_opener_icon',          'gorev_ikon',   256),
    @('workshop_close_icon',        'kapat',        176),
    @('workshop_gem_icon',          'elmas',        176),
    @('workshop_craft_points_icon', 'zanaat_puani', 176)
)
foreach ($p in $icons) {
    $cut = Trim $p[0]
    $s = $p[2] / [math]::Max($cut.Width, $cut.Height)
    $bmp = [AtolyeKit]::Scale($cut, [int][math]::Round($cut.Width * $s), [int][math]::Round($cut.Height * $s))
    [AtolyeKit]::Save($bmp, (Join-Path $kitDir ($p[1] + '.png')))
    Note $p[1] $bmp '-'
    $cut.Dispose(); $bmp.Dispose()
}

# ---- capsules: nine-sliced across only, caps are the art's own semicircular ends ---------------
# PillFit rescales the caps with the box at draw time, so what matters is that each cap CONTAINS the
# round end; the exact pixel is cosmetic. 0.52 of the height is that semicircle plus the rim around
# it -- the same fraction the league kit's claim button uses, and for the same shape.
# source name, kit name, drawn height in px
$caps = @(
    @('workshop_action_button_green', 'btn_yesil',    200),
    @('workshop_action_button_blue',  'btn_mavi',     200),
    @('goal_claim_button',            'btn_al',       200),
    @('workshop_chip_pill',           'hap_cip',      150),
    @('workshop_gate_card',           'durak_kart',   180),
    @('workshop_bar_track',           'cubuk_yatak',  120),
    @('workshop_bar_fill_blue',       'cubuk_mavi',   110),
    @('workshop_bar_fill_green',      'cubuk_yesil',  110),
    @('workshop_bar_fill_gold',       'cubuk_altin',  110)
)
foreach ($p in $caps) {
    $cut = Trim $p[0]
    $bmp = ToHeight $cut $p[2]
    $cap = [int][math]::Round($bmp.Height * 0.52)
    # A nine-slice cannot have its caps meet: leave at least a few columns to stretch.
    $max = [int][math]::Floor(($bmp.Width - 8) / 2)
    if ($cap -gt $max) { $cap = $max }
    [AtolyeKit]::Save($bmp, (Join-Path $kitDir ($p[1] + '.png')))
    Note $p[1] $bmp (Border $cap 0 $cap 0)
    $cut.Dispose(); $bmp.Dispose()
}

# ---- the chapter tabs, including the one that is derived ---------------------------------------
# Caps measured from the field, not taken as a fraction of the height -- see FieldRun. The two tabs
# come out with different caps, and the locked one's right cap is much the widest thing in the kit,
# because that is how much plate its padlock takes up.
$pad = 8
foreach ($p in @(@('chapter_tab_selected', 'sekme_secili'), @('chapter_tab_locked', 'sekme_kilitli'))) {
    $cut = Trim $p[0]
    $bmp = ToHeight $cut 175
    $f = [AtolyeKit]::FieldRun($bmp, 190)
    $l = $f[0] + $pad
    $r = $bmp.Width - 1 - $f[1] + $pad
    # A nine-slice cannot have its caps meet: leave at least a few columns to stretch.
    if ($l + $r -gt $bmp.Width - 8) { $r = $bmp.Width - 8 - $l }
    [AtolyeKit]::Save($bmp, (Join-Path $kitDir ($p[1] + '.png')))
    Note $p[1] $bmp (Border $l 0 $r 0)
    $script:report.Add(('  {0} field x {1}..{2} of {3}' -f $p[1], $f[0], $f[1], $bmp.Width))

    # The unselected tab: the locked one with its lock mirrored away. Derived from the SCALED bitmap
    # so it is pixel-for-pixel the same capsule as the state it sits next to in the column -- and its
    # right cap is the LEFT one's width, because the mirror is what is now at that end.
    if ($p[1] -eq 'sekme_kilitli') {
        $plain = [AtolyeKit]::MirrorRightHalf($bmp)
        [AtolyeKit]::Save($plain, (Join-Path $kitDir 'sekme.png'))
        Note 'sekme' $plain (Border $l 0 $l 0)
        $script:report.Add(('  sekme derived from sekme_kilitli, left {0} px mirrored over the right half' -f [int]($bmp.Width / 2)))
        $plain.Dispose()
    }
    $cut.Dispose(); $bmp.Dispose()
}

# ---- the two cards: nine-sliced both ways, borders measured -------------------------------------
# The goal row card is drawn about as wide as it is authored and only a few rows tall, so its middle
# is left alone; the portrait panel is stretched over whole screens, so its middle is collapsed.
# source, kit name, target width, middle rows to keep (0 = leave the middle alone)
foreach ($p in @(@('workshop_card_panel', 'panel_kart', 340, 140),
                 @('goal_task_card',      'gorev_kart', 700,   0))) {
    $cut = Trim $p[0]
    $k = $p[2] / $cut.Width
    $sc = [AtolyeKit]::Scale($cut, $p[2], [int][math]::Round($cut.Height * $k))
    $b4 = [AtolyeKit]::FrameBorders($sc)
    $out = if ($p[3] -gt 0) { [AtolyeKit]::CollapseRows($sc, $b4[3], $b4[1], $p[3]) }
           else             { [AtolyeKit]::Crop($sc, (New-Object System.Drawing.Rectangle 0, 0, $sc.Width, $sc.Height)) }
    [AtolyeKit]::Save($out, (Join-Path $kitDir ($p[1] + '.png')))
    Note $p[1] $out (Border $b4[0] $b4[1] $b4[2] $b4[3])
    $cut.Dispose(); $sc.Dispose(); $out.Dispose()
}

# ---- the title ribbon ---------------------------------------------------------------------------
# Nine-sliced across only: its gold buckles must stay inside the caps, and the tails outside them
# are what hangs below the band. The caps are measured from where the band itself starts and ends,
# which is the widest solid run across the sprite.
$cut = Trim 'workshop_title_ribbon'
$serit = ToHeight $cut 190
$band = [AtolyeKit]::BandCentre($serit, 40)
$cap = [int][math]::Round($serit.Width * 0.30)   # tail + buckle, both of which must not stretch
$max = [int][math]::Floor(($serit.Width - 8) / 2)
if ($cap -gt $max) { $cap = $max }
[AtolyeKit]::Save($serit, (Join-Path $kitDir 'serit_baslik.png'))
Note 'serit_baslik' $serit (Border $cap 0 $cap 0)
$report.Add(('  serit_baslik band centre {0:0.000} of height -> RibbonBand' -f $band))
$cut.Dispose(); $serit.Dispose()

$report | ForEach-Object { $_ }
