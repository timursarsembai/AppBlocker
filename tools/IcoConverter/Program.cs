using ImageMagick;
using System;
class Program {
    static void Main() {
        using (var image = new MagickImage(@"E:\Development\WindowsApps\AppBlocker\src\AppBlocker.Extension\icons\icon128.png")) {
            Console.WriteLine($"Width: {image.Width}, Height: {image.Height}");
            image.Resize(256, 256); // Force a specific valid size, e.g. 256x256
            image.Format = MagickFormat.Ico; // Use 'Ico' instead of 'Icon'
            image.Settings.SetDefine(MagickFormat.Ico, "auto-resize", "256,128,64,48,32,16");
            image.Write(@"E:\Development\WindowsApps\AppBlocker\src\AppBlocker.UI\app.ico");
        }
    }
}
