# Turns white-on-black raw icons into pure white glyphs on transparent, 1024x1024, glyph centred with 10% padding.
param([string]$In = 'C:\Users\chadh\Pictures\Razer App Controller 2D\raw', [string]$Out = 'C:\Users\chadh\Pictures\Razer App Controller 2D')
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System; using System.Drawing; using System.Drawing.Imaging; using System.Runtime.InteropServices;
public static class Glyph {
  public static Bitmap ToWhite(Bitmap src) {
    var b = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(b)) g.DrawImage(src, 0, 0, src.Width, src.Height);
    var r = new Rectangle(0,0,b.Width,b.Height);
    var d = b.LockBits(r, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
    var px = new byte[d.Stride*b.Height]; Marshal.Copy(d.Scan0, px, 0, px.Length);
    for (int i=0;i<px.Length;i+=4){
      int a0 = px[i+3];
      int lum = (px[i]*114 + px[i+1]*587 + px[i+2]*299)/1000;
      // black -> 0, white -> 255, with a little contrast so near-black JPEG noise vanishes
      int a = (lum-24)*255/(232-24); if (a<0) a=0; if (a>255) a=255;
      a = a*a0/255;
      px[i]=255; px[i+1]=255; px[i+2]=255; px[i+3]=(byte)a;
    }
    Marshal.Copy(px,0,d.Scan0,px.Length); b.UnlockBits(d); return b;
  }
  public static Rectangle Bounds(Bitmap b) {
    int x0=b.Width,y0=b.Height,x1=-1,y1=-1;
    var d=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
    var px=new byte[d.Stride*b.Height]; Marshal.Copy(d.Scan0,px,0,px.Length); b.UnlockBits(d);
    for(int y=0;y<b.Height;y++)for(int x=0;x<b.Width;x++){ if(px[y*d.Stride+x*4+3]>40){ if(x<x0)x0=x; if(x>x1)x1=x; if(y<y0)y0=y; if(y>y1)y1=y; } }
    return x1<0? new Rectangle(0,0,b.Width,b.Height) : Rectangle.FromLTRB(x0,y0,x1+1,y1+1);
  }
}
'@
New-Item -ItemType Directory -Force $Out | Out-Null
foreach ($f in Get-ChildItem $In -Filter *.png) {
  $src = [System.Drawing.Bitmap]::FromFile($f.FullName)
  $w = [Glyph]::ToWhite($src); $src.Dispose()
  $bb = [Glyph]::Bounds($w)
  $size = 1024; $box = $size * 0.8
  $scale = [math]::Min($box / $bb.Width, $box / $bb.Height)
  $dw = $bb.Width * $scale; $dh = $bb.Height * $scale
  $dst = New-Object System.Drawing.Bitmap $size, $size
  $g = [System.Drawing.Graphics]::FromImage($dst)
  $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
  $g.DrawImage($w, (New-Object System.Drawing.RectangleF (($size-$dw)/2), (($size-$dh)/2), $dw, $dh), $bb, 'Pixel')
  $g.Dispose(); $w.Dispose()
  $dst.Save((Join-Path $Out $f.Name), [System.Drawing.Imaging.ImageFormat]::Png); $dst.Dispose()
  "converted $($f.Name)"
}
