<#
HUD para ve elmas gostergeleri -> oyunun kullandigi iki sprite.

    powershell -NoProfile -ExecutionPolicy Bypass -File Tools/ui/hud_gosterge_kiti.ps1

The main-menu money and gem counters are the navy pills from "Assets/UI DESIGNS/ui assets general"
(the folder above carries a Turkish dotted capital I, which Windows PowerShell 5.1 misreads in a
BOM-less script -- hence this file stays ASCII and finds the folder by wildcard):

- gem-pill.png is a real transparent export and is only trimmed and downscaled.
- cash-pill-transparent.png is NOT transparent: a grey checkerboard (about #888 and #D0D0D0) is
  painted where the transparency should be. It is cut out here: a flood fill from the image border
  through grey pixels only, which stops at the pill's saturated blue rim and at the sticker border
  round the bills, so the cream strap and the white plus -- both enclosed -- are never reached.
  A few passes then peel the grey-blended fringe off the rim, and the last ring is half alpha so the
  edge keeps some anti-aliasing.

BOTH PILLS ARE PLACED ON ONE CANVAS, BODY ON BODY. The two exports frame their bodies differently
(the cash body is a little taller and further right), and the HUD draws the two pills side by side at
one size. So each is scaled until its navy body is BodyHeight tall and placed with the body's right
end and vertical centre on the same spot -- the two boxes are then interchangeable, and the number
slot lands at the same place in both. The slot's box is printed at the end, as fractions.

Output: Assets/Art/UI/HudGosterge/hud_hap_nakit.png, hud_hap_elmas.png.
#>
$ErrorActionPreference = 'Stop'

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class HudGosterge
{
    public static int[] Pixels(Bitmap b)
    {
        var data = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var px = new int[b.Width * b.Height];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        b.UnlockBits(data);
        return px;
    }

    public static Bitmap FromPixels(int[] px, int w, int h)
    {
        var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(px, 0, data.Scan0, px.Length);
        b.UnlockBits(data);
        return b;
    }

    public static Bitmap Load(string path)
    {
        using (var src = new Bitmap(path))
        {
            var copy = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(copy))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height));
            }
            return copy;
        }
    }

    static int Spread(int c) { int r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255; return Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)); }
    static int Lum(int c) { int r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255; return (r * 299 + g * 587 + b * 114) / 1000; }

    /// <summary>Cuts a painted checkerboard out of an opaque export. See the script header.</summary>
    public static Bitmap CutChecker(Bitmap b)
    {
        int w = b.Width, h = b.Height;
        int[] px = Pixels(b);
        var bg = new bool[w * h];
        var queue = new Queue<int>();
        Func<int, bool> grey = c => Spread(c) <= 28 && Lum(c) >= 100 && Lum(c) <= 235;
        for (int x = 0; x < w; x++) { Seed(px, bg, queue, x, grey); Seed(px, bg, queue, (h - 1) * w + x, grey); }
        for (int y = 0; y < h; y++) { Seed(px, bg, queue, y * w, grey); Seed(px, bg, queue, y * w + w - 1, grey); }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue(); int x = i % w, y = i / w;
            if (x > 0) Seed(px, bg, queue, i - 1, grey);
            if (x < w - 1) Seed(px, bg, queue, i + 1, grey);
            if (y > 0) Seed(px, bg, queue, i - w, grey);
            if (y < h - 1) Seed(px, bg, queue, i + w, grey);
        }

        // Peel the fringe: rim pixels blended with the grey behind them.
        for (int pass = 0; pass < 3; pass++)
        {
            var peel = new List<int>();
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int i = y * w + x;
                    if (bg[i] || !(bg[i - 1] || bg[i + 1] || bg[i - w] || bg[i + w])) continue;
                    int c = px[i];
                    if (Spread(c) <= 70 && Lum(c) >= 90) peel.Add(i);
                }
            foreach (int i in peel) bg[i] = true;
        }

        for (int i = 0; i < px.Length; i++)
        {
            if (bg[i]) { px[i] = 0; continue; }
            int x = i % w, y = i / w;
            bool edge = (x > 0 && bg[i - 1]) || (x < w - 1 && bg[i + 1]) || (y > 0 && bg[i - w]) || (y < h - 1 && bg[i + w]);
            if (edge) px[i] = (px[i] & 0x00FFFFFF) | (128 << 24);
        }
        return FromPixels(px, w, h);
    }

    static void Seed(int[] px, bool[] bg, Queue<int> q, int i, Func<int, bool> grey)
    {
        if (bg[i] || !grey(px[i])) return;
        bg[i] = true; q.Enqueue(i);
    }

    /// <summary>Opaque bounds (alpha over 16).</summary>
    public static Rectangle Bounds(Bitmap b)
    {
        int[] px = Pixels(b); int w = b.Width, h = b.Height;
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            if (((px[y * w + x] >> 24) & 255) > 16) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
        return Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
    }

    /// <summary>The navy body's top, bottom and right end, measured on column <paramref name="col"/> and row mid.</summary>
    public static int[] Body(Bitmap b, int col)
    {
        int[] px = Pixels(b); int w = b.Width, h = b.Height;
        int top = -1, bottom = -1;
        for (int y = 0; y < h; y++) if (((px[y * w + col] >> 24) & 255) > 128) { top = y; break; }
        for (int y = h - 1; y >= 0; y--) if (((px[y * w + col] >> 24) & 255) > 128) { bottom = y; break; }
        int mid = (top + bottom) / 2, right = -1;
        for (int x = w - 1; x >= 0; x--) if (((px[mid * w + x] >> 24) & 255) > 128) { right = x; break; }
        return new[] { top, bottom, right };
    }

    /// <summary>The light number slot on row <paramref name="row"/> and column <paramref name="col"/>.</summary>
    public static int[] Slot(Bitmap b, int col, int row)
    {
        int[] px = Pixels(b); int w = b.Width, h = b.Height;
        Func<int, bool> light = c => ((c >> 24) & 255) > 200 && Lum(c) > 150 && (c & 255) > 180;
        int left = col, right = col, top = row, bottom = row;
        while (left > 0 && light(px[row * w + left - 1])) left--;
        while (right < w - 1 && light(px[row * w + right + 1])) right++;
        while (top > 0 && light(px[(top - 1) * w + col])) top--;
        while (bottom < h - 1 && light(px[(bottom + 1) * w + col])) bottom++;
        return new[] { left, top, right, bottom };
    }

    public static Bitmap Place(Bitmap src, int canvasW, int canvasH, float scale, float dx, float dy)
    {
        var dst = new Bitmap(canvasW, canvasH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            using (var attrs = new ImageAttributes())
            {
                attrs.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(src, new Rectangle((int)Math.Round(dx), (int)Math.Round(dy),
                            (int)Math.Round(src.Width * scale), (int)Math.Round(src.Height * scale)),
                            0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
            }
        }
        return dst;
    }
}
'@

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$design = Get-ChildItem -Path (Join-Path $root 'Assets') -Directory | Where-Object { $_.Name -like 'UI DES*GNS' } | Select-Object -First 1
$general = Join-Path $design.FullName 'ui assets general'
$out = Join-Path $root 'Assets/Art/UI/HudGosterge'
New-Item -ItemType Directory -Force -Path $out | Out-Null

$cash = [HudGosterge]::CutChecker([HudGosterge]::Load((Join-Path $general 'cash-pill-transparent.png')))
$gem = [HudGosterge]::Load((Join-Path $general 'gem-pill.png'))

# Measured on the source column 1300 -- inside the body, clear of both icons.
$cashBody = [HudGosterge]::Body($cash, 1300)
$gemBody = [HudGosterge]::Body($gem, 1300)
"cash body top/bottom/right: $($cashBody -join ', ')"
"gem body top/bottom/right:  $($gemBody -join ', ')"

$BodyHeight = 300.0
$cashScale = $BodyHeight / ($cashBody[1] - $cashBody[0])
$gemScale = $BodyHeight / ($gemBody[1] - $gemBody[0])

# One canvas both fit on, with the body's right end at RightPad from the edge and its centre at mid height.
$RightPad = 8.0
$cashBounds = [HudGosterge]::Bounds($cash)
$gemBounds = [HudGosterge]::Bounds($gem)
$reachLeft = [Math]::Max(($cashBody[2] - $cashBounds.Left) * $cashScale, ($gemBody[2] - $gemBounds.Left) * $gemScale)
$cashMid = ($cashBody[0] + $cashBody[1]) / 2.0
$gemMid = ($gemBody[0] + $gemBody[1]) / 2.0
$reachUp = [Math]::Max(($cashMid - $cashBounds.Top) * $cashScale, ($gemMid - $gemBounds.Top) * $gemScale)
$reachDown = [Math]::Max(($cashBounds.Bottom - $cashMid) * $cashScale, ($gemBounds.Bottom - $gemMid) * $gemScale)
$W = [int][Math]::Ceiling($reachLeft + $RightPad + 4)
$H = [int][Math]::Ceiling([Math]::Max($reachUp, $reachDown) * 2 + 4)
$anchorX = $W - $RightPad
$anchorY = $H / 2.0

$cashOut = [HudGosterge]::Place($cash, $W, $H, $cashScale, $anchorX - $cashBody[2] * $cashScale, $anchorY - $cashMid * $cashScale)
$gemOut = [HudGosterge]::Place($gem, $W, $H, $gemScale, $anchorX - $gemBody[2] * $gemScale, $anchorY - $gemMid * $gemScale)
$cashOut.Save((Join-Path $out 'hud_hap_nakit.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$gemOut.Save((Join-Path $out 'hud_hap_elmas.png'), [System.Drawing.Imaging.ImageFormat]::Png)
"canvas: ${W}x${H}"

# The number slot, as fractions of the canvas (x from the left, y from the BOTTOM, Unity's way).
foreach ($pair in @(@('nakit', $cashOut), @('elmas', $gemOut)))
{
    $s = [HudGosterge]::Slot($pair[1], [int]($W * 0.70), [int]($H / 2))
    "{0} slot: x {1:0.000}-{2:0.000}  y {3:0.000}-{4:0.000}" -f $pair[0], ($s[0] / $W), (($s[2] + 1) / $W), (1 - ($s[3] + 1) / $H), (1 - $s[1] / $H)
}
