using SkiaSharp;
// usage: WebpEnc <srcDir> <outDir>  -> <outDir>/<name>.webp for each PNG
var (src, outDir) = (args[0], args[1]);
Directory.CreateDirectory(outDir);
foreach (var f in Directory.GetFiles(src, "*.png"))
{
    using var bmp = SKBitmap.Decode(f);
    using var img = SKImage.FromBitmap(bmp);
    using var data = img.Encode(SKEncodedImageFormat.Webp, 92);
    File.WriteAllBytes(Path.Combine(outDir, Path.GetFileNameWithoutExtension(f) + ".webp"), data.ToArray());
}
Console.WriteLine($"encoded {Directory.GetFiles(outDir, "*.webp").Length}");
