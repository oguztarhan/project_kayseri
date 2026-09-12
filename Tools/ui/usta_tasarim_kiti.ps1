<#
Ustalar tasarim seti -> the sprite kit ForemanRosterUI and UI_UstaKarti wear.

    powershell -NoProfile -ExecutionPolicy Bypass -File Tools/ui/usta_tasarim_kiti.ps1

The source is the 33-piece set in "Assets/UI DESIGNS/masters-characters-15" (the folder above it
carries a Turkish dotted capital I, which Windows PowerShell 5.1 misreads in a BOM-less script --
hence this file stays ASCII and finds the folder by wildcard, as the other kit scripts do). The set is
left untouched; everything here is derived:

- KNOCKED BACK. Several exports carry a soft dark glow around the piece. Laid on the white card that
  glow draws as a grey smudge, so alpha at or below a low mark is cleared and the band up to a high
  mark is stretched back to full range. The navy outline is opaque and passes through untouched.
- TRIMMED to the art, DOWNSCALED to the size the screen shows, ALPHA CLAMPED (>= 240 becomes 255), as
  in atolye_tasarim_kiti.ps1.
- THE MAIN PANEL IS NINE-SLICED: borders measured from its white field, middle rows collapsed. It goes
  to Art/UI/UstaPanel, OUTSIDE the atlas folder -- at about a thousand pixels wide it would take a
  large share of a 2048 page by itself, which is why the sea kit keeps its backdrop out too.

Nothing else is sliced. Portraits and frames are 2:3 and drawn with preserveAspect in 2:3 slots, and
every frame's crest would smear under a stretch.

Output: Assets/Art/UI/UstaKiti (packed by Resources/UI/Usta/UstaKiti) and Assets/Art/UI/UstaPanel.
Import settings live in the .meta files and survive a re-run: Sprite (2D and UI), single, no mipmaps,
clamp. panel_ana's nine-slice border (L, B, R, T) in output pixels is printed at the end -- re-apply
it in Unity if the art changes.
#>
$ErrorActionPreference = 'Stop'

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class UstaKit
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
    static bool Bright(byte[] px, int i)
    {
        return px[i + 3] > 160 && px[i] > 205 && px[i + 1] > 205 && px[i + 2] > 205;
    }

    /// <summary>Alpha at or below lo is cleared; lo..hi is stretched to 0..255; above hi is kept.</summary>
    public static void Knock(Bitmap b, int lo, int hi)
    {
        int stride; var px = Read(b, out stride);
        for (int i = 3; i < px.Length; i += 4)
        {
            int a = px[i];
            if (a <= lo) px[i] = 0;
            else if (a < hi) px[i] = (byte)((a - lo) * 255 / (hi - lo));
        }
        Write(b, px);
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

    /// <summary>
    /// A panel's nine-slice borders (L, B, R, T): the gap between its edge and its white field, taken
    /// as the LONGEST bright run down the centre column and across the centre row. The compass and the
    /// bottom tab break the column at both ends, so the longest run is the field itself.
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

    /// <summary>A nine-sliced panel with its stretchable middle resampled down to keep rows.</summary>
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
          Where-Object { $_.Name -like 'masters-characters*' } | Select-Object -First 1
if ($null -eq $set) { throw 'Design set not found under Assets/UI DES*/masters-characters*' }

$kitDir   = Join-Path $assets 'Art\UI\UstaKiti'
$panelDir = Join-Path $assets 'Art\UI\UstaPanel'
New-Item -ItemType Directory -Force -Path $kitDir, $panelDir | Out-Null

function Src([string]$name) {
    $f = Join-Path $set.FullName "$name.png"
    if (-not (Test-Path $f)) { throw "Missing design $name.png" }
    return $f
}

$report = New-Object System.Collections.Generic.List[string]
function Note($name, $bmp, $border) {
    $script:report.Add(('{0,-18} {1,4}x{2,-4} border {3}' -f $name, $bmp.Width, $bmp.Height, $border))
}

# Knock the glow back, trim to the art, flatten the near-opaque body.
function Trim([string]$name, [int]$lo, [int]$hi) {
    $raw = [UstaKit]::Load((Src $name))
    if ($hi -gt $lo) { [UstaKit]::Knock($raw, $lo, $hi) }
    $cut = [UstaKit]::Crop($raw, [UstaKit]::Bounds($raw, 8, 2))
    [UstaKit]::ClampAlpha($cut, 240)
    $raw.Dispose()
    return $cut
}

function LongSide($bmp, [int]$side) {
    $s = $side / [math]::Max($bmp.Width, $bmp.Height)
    return [UstaKit]::Scale($bmp, [int][math]::Round($bmp.Width * $s), [int][math]::Round($bmp.Height * $s))
}

# source name, kit name, long side in px, knock low, knock high (equal = no knock)
$pieces = @(
    # Portraits, in Foremen.Roster order -- the file numbers already are that order.
    @('01-ceki-hasan',    'portre_hasan',  448, 60, 140),
    @('02-ocak-sukru',    'portre_sukru',  448, 60, 140),
    @('03-damar-nazmi',   'portre_nazmi',  448, 60, 140),
    @('04-sayim-bekir',   'portre_bekir',  448, 60, 140),
    @('05-raf-necla',     'portre_necla',  448, 60, 140),
    @('06-kilit-rasim',   'portre_rasim',  448, 60, 140),
    @('07-koz-fikri',     'portre_fikri',  448, 60, 140),
    @('08-pota-sema',     'portre_sema',   448, 60, 140),
    @('09-firin-zeki',    'portre_zeki',   448, 60, 140),
    @('10-rihtim-cemil',  'portre_cemil',  448, 60, 140),
    @('11-vinc-sedef',    'portre_sedef',  448, 60, 140),
    @('12-fener-mahmut',  'portre_mahmut', 448, 60, 140),
    @('13-kantar-riza',   'portre_riza',   448, 60, 140),
    @('14-pesin-leyla',   'portre_leyla',  448, 60, 140),
    @('15-borsa-hikmet',  'portre_hikmet', 448, 60, 140),

    # Frames. Common / Rare / Legendary are the three rarities; Epic and Mythic are worn at four and
    # five stars; the plain one is the master you have not found yet.
    @('02-master-card-frame',      'cerceve_kilitli',  448, 60, 140),
    @('03-common-rarity-frame',    'cerceve_siradan',  448, 60, 140),
    @('04-rare-rarity-frame',      'cerceve_nadir',    448, 60, 140),
    @('05-epic-rarity-frame',      'cerceve_destansi', 448, 60, 140),
    @('06-legendary-rarity-frame', 'cerceve_efsanevi', 448, 60, 140),
    @('07-mythic-rarity-frame',    'cerceve_mitik',    448, 60, 140),

    # Badges, icons and the rest. Clean cut-outs with no glow, so only a light knock.
    @('08-status-equipped-badge',     'rozet_gorevde', 160, 8, 40),
    @('09-status-locked-badge',       'rozet_kilit',   160, 8, 40),
    @('10-status-new-badge',          'rozet_yeni',    160, 8, 40),
    @('11-status-upgrade-badge',      'rozet_yukselt', 160, 8, 40),
    @('12-filter-icon',               'ikon_filtre',   160, 8, 40),
    @('13-sort-icon',                 'ikon_sirala',   160, 8, 40),
    @('14-chest-opening-reveal',      'sandik_acilis', 512, 20, 60),
    @('15-single-reveal-tile',        'sandik_kutu',   320, 8, 40),
    @('16-empty-list-illustration',   'bos_liste',     384, 8, 40),
    @('17-notification-dot',          'nokta',          96, 8, 40),
    @('18-masters-close-button',      'kapat',         176, 8, 40)
)
foreach ($p in $pieces) {
    $cut = Trim $p[0] $p[3] $p[4]
    $bmp = LongSide $cut $p[2]
    [UstaKit]::Save($bmp, (Join-Path $kitDir ($p[1] + '.png')))
    Note $p[1] $bmp '-'
    $cut.Dispose(); $bmp.Dispose()
}

# ---- the main panel: nine-sliced both ways, borders measured, middle collapsed ------------------
$cut = Trim '01-main-masters-panel' 8 40
$k = 1040 / $cut.Width
$sc = [UstaKit]::Scale($cut, 1040, [int][math]::Round($cut.Height * $k))
$b4 = [UstaKit]::FrameBorders($sc)
$out = [UstaKit]::CollapseRows($sc, $b4[3], $b4[1], 140)
[UstaKit]::Save($out, (Join-Path $panelDir 'panel_ana.png'))
Note 'panel_ana' $out ('({0}, {1}, {2}, {3})' -f $b4[0], $b4[1], $b4[2], $b4[3])
$cut.Dispose(); $sc.Dispose(); $out.Dispose()

$report | ForEach-Object { $_ }
