<#
Kontrat / Maden techizati / Etkinlik ekranlari -> oyunun kullandigi sprite kiti.

    powershell -NoProfile -ExecutionPolicy Bypass -File Tools/ui/ekran_tasarim_kiti.ps1

Three screens -- ContractUI, MiningGearUI, LiveEventsUI -- are built from pieces of TWO design sets:
"Assets/UI DESIGNS/ui assets general" and "Assets/UI DESIGNS/settings-ui-hud-standard" (the folder
above them carries a Turkish dotted capital I, which Windows PowerShell 5.1 misreads in a BOM-less
script -- hence this file stays ASCII and finds the folders by wildcard). The sheet, the title ribbon,
the green claim capsule and the gem are NOT derived here: Tools/ui/lig_tasarim_kiti.ps1 already makes
them from the leaderboard set, and EkranKit hands those out of LigKit rather than packing a second
copy of each.

Everything is derived the way the other kit scripts derive theirs:

- TRIMMED to the art. PillFit maps a sprite's FULL height onto its box, so a transparent margin
  would draw every capsule thinner than it is.
- DOWNSCALED to about the size the screens draw it.
- ALPHA CLAMPED at 240, so nothing shows through a body; edges keep their anti-aliasing.

FOUR EXPORTS IN THE GENERAL SET ARE NOT USABLE: cash-pill, cash-pill-transparent, progress-bar,
progress-bar-transparent and small-card-panel are fully opaque, with a checkerboard painted into them
where the transparency should be. small-card-panel-transparent is the real one.

EVERY BORDER IS MEASURED, NOT TYPED -- see the header of atolye_tasarim_kiti.ps1 for why. What is
measured differs per shape, because the pieces carry their ornament in different places:

- The orange action capsule's caps run to the edge of its CREAM INLAY: the label belongs on the
  inlay, and an inlay that stretched into the caps would push the rounded orange ends apart unevenly.
- The mining-point pill's LEFT cap holds the pickaxe badge, so it runs to the start of the pale
  field; its right cap is the ordinary semicircle.
- The navy card is a frame around a navy field, measured on its dark run rather than a bright one.
- The equipment slot is drawn aspect-locked, never sliced (its bolted corners would smear); what is
  printed for it is where its dark well sits, as fractions, so the icon can be seated inside it.

Output: Assets/Art/UI/EkranKiti, packed by Resources/UI/Ekran/EkranKiti.spriteatlasv2. Import settings
live in the .meta files and survive a re-run; the borders (L, B, R, T) printed at the end are what
to re-apply if the art changes.
#>
$ErrorActionPreference = 'Stop'

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class EkranKit
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

    // BGRA in memory. kind: 0 = cream/white (all channels high), 1 = navy well (opaque, blue low),
    // 2 = pale blue field (red and green lifted, blue high).
    static bool Match(byte[] px, int i, int kind)
    {
        if (px[i + 3] < 200) return false;
        int bl = px[i], gr = px[i + 1], rd = px[i + 2];
        if (kind == 0) return rd > 200 && gr > 200 && bl > 170;
        if (kind == 1) return bl < 170 && rd < 90;
        return rd > 110 && gr > 150 && bl > 200;
    }

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

    /// <summary>Longest run of matching pixels along the centre row: {start, end}.</summary>
    public static int[] RowRun(Bitmap b, int kind)
    {
        int stride; var px = Read(b, out stride);
        int cy = b.Height / 2, x0 = 0, x1 = -1, run = -1;
        for (int x = 0; x <= b.Width; x++)
        {
            bool ok = x < b.Width && Match(px, cy * stride + x * 4, kind);
            if (ok) { if (run < 0) run = x; }
            else if (run >= 0) { if (x - 1 - run > x1 - x0) { x0 = run; x1 = x - 1; } run = -1; }
        }
        return new int[] { x0, x1 };
    }

    /// <summary>Longest run of matching pixels down the centre column: {start, end}, GDI rows.</summary>
    public static int[] ColumnRun(Bitmap b, int kind)
    {
        int stride; var px = Read(b, out stride);
        int cx = b.Width / 2, y0 = 0, y1 = -1, run = -1;
        for (int y = 0; y <= b.Height; y++)
        {
            bool ok = y < b.Height && Match(px, y * stride + cx * 4, kind);
            if (ok) { if (run < 0) run = y; }
            else if (run >= 0) { if (y - 1 - run > y1 - y0) { y0 = run; y1 = y - 1; } run = -1; }
        }
        return new int[] { y0, y1 };
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
            ia.SetWrapMode(WrapMode.TileFlipXY);
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

    public static void Save(Bitmap b, string path)
    {
        b.Save(path, ImageFormat.Png);
    }
}
'@

$repo   = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$assets = Join-Path $repo 'Assets'
$ui     = Get-ChildItem $assets -Directory | Where-Object { $_.Name -like 'UI DES*' } | Select-Object -First 1
if ($null -eq $ui) { throw 'Design root not found under Assets/UI DES*' }
$general  = Join-Path $ui.FullName 'ui assets general'
$settings = Join-Path $ui.FullName 'settings-ui-hud-standard'

$kitDir = Join-Path $assets 'Art\UI\EkranKiti'
New-Item -ItemType Directory -Force -Path $kitDir | Out-Null

$report = New-Object System.Collections.Generic.List[string]
function Note($name, $bmp, $border) {
    $script:report.Add(('{0,-14} {1,4}x{2,-4} border {3}' -f $name, $bmp.Width, $bmp.Height, $border))
}
function Border($l, $b, $r, $t) { return ('({0}, {1}, {2}, {3})' -f [int]$l, [int]$b, [int]$r, [int]$t) }

function Trim([string]$dir, [string]$name) {
    $f = Join-Path $dir "$name.png"
    if (-not (Test-Path -LiteralPath $f)) { throw "Missing design $name.png" }
    $raw = [EkranKit]::Load($f)
    $cut = [EkranKit]::Crop($raw, [EkranKit]::Bounds($raw, 8, 2))
    [EkranKit]::ClampAlpha($cut, 240)
    $raw.Dispose()
    return $cut
}

function ToHeight($bmp, [int]$h) {
    $w = [int][math]::Round($bmp.Width * $h / $bmp.Height)
    return [EkranKit]::Scale($bmp, $w, $h)
}

# A nine-slice cannot have its caps meet: leave at least a few columns to stretch.
function CapLimit($bmp, [int]$l, [int]$r) {
    $room = $bmp.Width - 8
    if ($l + $r -gt $room) { $s = $room / ($l + $r); $l = [int][math]::Floor($l * $s); $r = [int][math]::Floor($r * $s) }
    return @($l, $r)
}

# ---- icons: drawn aspect-locked, so only the long side matters ------------------------------------
# folder, source, kit name, long side in px
$icons = @(
    @($general, 'close-button',     'kapat',         176),
    @($general, 'reward-chest',     'sandik',        220),
    @($general, 'lock-icon',        'kilit',         176),
    @($general, 'live-events-icon', 'etkinlik_ikon', 256)
)
foreach ($p in $icons) {
    $cut = Trim $p[0] $p[1]
    $s = $p[3] / [math]::Max($cut.Width, $cut.Height)
    $bmp = [EkranKit]::Scale($cut, [int][math]::Round($cut.Width * $s), [int][math]::Round($cut.Height * $s))
    [EkranKit]::Save($bmp, (Join-Path $kitDir ($p[2] + '.png')))
    Note $p[2] $bmp '-'
    $cut.Dispose(); $bmp.Dispose()
}

# ---- the orange action capsule: caps run to the cream inlay ---------------------------------------
$pad = 6
$cut = Trim $general 'primary-action-button'
$bmp = ToHeight $cut 200
$f = [EkranKit]::RowRun($bmp, 0)
$lr = CapLimit $bmp ($f[0] + $pad) ($bmp.Width - 1 - $f[1] + $pad)
[EkranKit]::Save($bmp, (Join-Path $kitDir 'btn_turuncu.png'))
Note 'btn_turuncu' $bmp (Border $lr[0] 0 $lr[1] 0)
$inlay = [EkranKit]::ColumnRun($bmp, 0)
$report.Add(('  btn_turuncu inlay x {0}..{1} of {2}, y(from bottom) {3:0.000}..{4:0.000}' -f $f[0], $f[1], $bmp.Width,
    (1.0 - $inlay[1] / ($bmp.Height - 1)), (1.0 - $inlay[0] / ($bmp.Height - 1))))
$cut.Dispose(); $bmp.Dispose()

# ---- the pale capsule: the dead face of every button on the three screens -------------------------
$cut = Trim $settings 'settings-empty-button'
$bmp = ToHeight $cut 160
$cap = [int][math]::Round($bmp.Height * 0.52)
$lr = CapLimit $bmp $cap $cap
[EkranKit]::Save($bmp, (Join-Path $kitDir 'btn_bos.png'))
Note 'btn_bos' $bmp (Border $lr[0] 0 $lr[1] 0)
$cut.Dispose(); $bmp.Dispose()

# ---- the mining-point pill: the badge lives in the left cap ---------------------------------------
$cut = Trim $general 'mining-point-pill'
$bmp = ToHeight $cut 150
$f = [EkranKit]::RowRun($bmp, 2)
$lr = CapLimit $bmp ($f[0] + $pad) ([int][math]::Round($bmp.Height * 0.52))
[EkranKit]::Save($bmp, (Join-Path $kitDir 'hap_puan.png'))
Note 'hap_puan' $bmp (Border $lr[0] 0 $lr[1] 0)
$report.Add(('  hap_puan field x {0}..{1} of {2}' -f $f[0], $f[1], $bmp.Width))
$cut.Dispose(); $bmp.Dispose()

# ---- the navy card: a frame around a navy well, both ways ----------------------------------------
$cut = Trim $general 'small-card-panel-transparent'
$bmp = ToHeight $cut 240
$fx = [EkranKit]::RowRun($bmp, 1)
$fy = [EkranKit]::ColumnRun($bmp, 1)
# The well's own rounded corner has to stay inside the border too, or the slicer squares it off.
$round = [int][math]::Round(($fy[1] - $fy[0]) * 0.22)
$l = $fx[0] + $round; $r = $bmp.Width - 1 - $fx[1] + $round
$t = $fy[0] + $round; $b = $bmp.Height - 1 - $fy[1] + $round
[EkranKit]::Save($bmp, (Join-Path $kitDir 'kart_lacivert.png'))
Note 'kart_lacivert' $bmp (Border $l $b $r $t)
$cut.Dispose(); $bmp.Dispose()

# ---- the equipment slot: aspect-locked; print where its well sits ---------------------------------
$cut = Trim $general 'equipment-slot'
$s = 300 / [math]::Max($cut.Width, $cut.Height)
$bmp = [EkranKit]::Scale($cut, [int][math]::Round($cut.Width * $s), [int][math]::Round($cut.Height * $s))
$fx = [EkranKit]::RowRun($bmp, 1)
$fy = [EkranKit]::ColumnRun($bmp, 1)
[EkranKit]::Save($bmp, (Join-Path $kitDir 'yuva.png'))
Note 'yuva' $bmp '-'
$report.Add(('  yuva well x {0:0.000}..{1:0.000}, y(from bottom) {2:0.000}..{3:0.000}' -f
    ($fx[0] / ($bmp.Width - 1)), ($fx[1] / ($bmp.Width - 1)),
    (1.0 - $fy[1] / ($bmp.Height - 1)), (1.0 - $fy[0] / ($bmp.Height - 1))))
$cut.Dispose(); $bmp.Dispose()

$report | ForEach-Object { $_ }
