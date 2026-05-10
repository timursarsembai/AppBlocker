using System;
using System.IO;

class Program {
    static void Main() {
        byte[] pngData = File.ReadAllBytes(@"E:\Development\WindowsApps\AppBlocker\src\AppBlocker.Extension\icons\icon128.png");
        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            // ICONDIR
            bw.Write((short)0); // idReserved
            bw.Write((short)1); // idType (1 = ico)
            bw.Write((short)1); // idCount (1 image)

            // ICONDIRENTRY
            bw.Write((byte)128); // bWidth
            bw.Write((byte)128); // bHeight
            bw.Write((byte)0);   // bColorCount
            bw.Write((byte)0);   // bReserved
            bw.Write((short)1);  // wPlanes
            bw.Write((short)32); // wBitCount
            bw.Write((int)pngData.Length); // dwBytesInRes
            bw.Write((int)22);   // dwImageOffset (22 bytes header)

            // Image Data
            bw.Write(pngData);
            
            File.WriteAllBytes(@"E:\Development\WindowsApps\AppBlocker\src\AppBlocker.UI\app.ico", ms.ToArray());
        }
    }
}
