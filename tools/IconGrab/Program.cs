// 从 PE 文件提取 RT_GROUP_ICON 资源，重建为 .ico；可选复制 ico 并导出 PNG。
// 用法: IconGrab <exe路径> <输出目录> [copyIcoTo] [copyPngTo]
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;

class IconGrab
{
    const uint LOAD_LIBRARY_AS_DATAFILE = 2;
    static readonly IntPtr RT_GROUP_ICON = new(14);
    static readonly IntPtr RT_ICON = new(3);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool FreeLibrary(IntPtr hModule);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool EnumResourceNames(IntPtr hModule, IntPtr type, EnumResNameProc cb, IntPtr param);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr FindResource(IntPtr hModule, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr LockResource(IntPtr hResData);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint SizeofResource(IntPtr hModule, IntPtr hResInfo);

    delegate bool EnumResNameProc(IntPtr hModule, IntPtr type, IntPtr name, IntPtr param);

    static int Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("usage: IconGrab <exe> <outdir>"); return 1; }
        string exe = args[0], outDir = args[1];
        Directory.CreateDirectory(outDir);

        IntPtr h = LoadLibraryEx(exe, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE);
        if (h == IntPtr.Zero) { Console.WriteLine("LoadLibraryEx failed: " + Marshal.GetLastWin32Error()); return 2; }

        var groupNames = new List<IntPtr>();
        EnumResourceNames(h, RT_GROUP_ICON, (m, t, n, p) => { groupNames.Add(n); return true; }, IntPtr.Zero);
        Console.WriteLine($"groups: {groupNames.Count}");

        var produced = new List<(string Path, int W, int H)>();
        for (int gi = 0; gi < groupNames.Count; gi++)
        {
            IntPtr gRes = FindResource(h, groupNames[gi], RT_GROUP_ICON);
            if (gRes == IntPtr.Zero) continue;
            IntPtr gData = LockResource(LoadResource(h, gRes));
            int gSize = (int)SizeofResource(h, gRes);
            if (gData == IntPtr.Zero || gSize < 6) continue;

            // GRPICONDIR: WORD reserved, WORD type, WORD count; then GRPICONDIRENTRY(14B):
            // bWidth,bHeight,bColorCount,bReserved,wPlanes,wBitCount,dwBytesInRes,wId
            short count = Marshal.ReadInt16(gData, 2);
            if (count <= 0 || count > 512) continue;

            var entries = new List<(byte w, byte hh, short planes, short bitCount, int size, short id)>();
            for (int i = 0; i < count; i++)
            {
                int off = 6 + i * 14;
                byte bw = Marshal.ReadByte(gData, off);
                byte bh = Marshal.ReadByte(gData, off + 1);
                short planes = Marshal.ReadInt16(gData, off + 4);
                short bits = Marshal.ReadInt16(gData, off + 6);
                int resSize = Marshal.ReadInt32(gData, off + 8);
                short id = Marshal.ReadInt16(gData, off + 12);
                entries.Add((bw, bh, planes, bits, resSize, id));
            }

            using var ms = new MemoryStream();
            using (var bw2 = new BinaryWriter(ms, System.Text.Encoding.Default, true))
            {
                bw2.Write((short)0); bw2.Write((short)1); bw2.Write((short)count);
                long dataStart = 6 + 16 * count; // ICONDIRENTRY 是 16 字节（含 dwImageOffset）
                long cursor = dataStart;
                foreach (var e in entries)
                {
                    bw2.Write(e.w); bw2.Write(e.hh); bw2.Write((byte)0); bw2.Write((byte)0);
                    bw2.Write(e.planes); bw2.Write(e.bitCount); bw2.Write(e.size); bw2.Write((int)cursor);
                    cursor += e.size;
                }
                foreach (var e in entries)
                {
                    IntPtr iRes = FindResource(h, new IntPtr(e.id), RT_ICON);
                    if (iRes == IntPtr.Zero) { Console.WriteLine($"  icon#{gi} missing id {e.id}"); goto next; }
                    IntPtr data = LockResource(LoadResource(h, iRes));
                    uint size = SizeofResource(h, iRes);
                    byte[] buf = new byte[size];
                    Marshal.Copy(data, buf, 0, (int)size);
                    bw2.Write(buf);
                }
            }
            {
                string path = Path.Combine(outDir, $"icon_{gi}.ico");
                File.WriteAllBytes(path, ms.ToArray());
                int maxW = entries.Max(e => e.w == 0 ? 256 : e.w);
                int maxH = entries.Max(e => e.hh == 0 ? 256 : e.hh);
                produced.Add((path, maxW, maxH));
                Console.WriteLine($"  icon#{gi}: {count} frames, max {maxW}x{maxH} -> {path}");
            }
            next:;
        }
        FreeLibrary(h);

        if (produced.Count == 0) { Console.WriteLine("NO_ICONS"); return 3; }
        var best = produced.OrderByDescending(p => p.W * p.H).First();
        Console.WriteLine("BEST: " + best.Path);

        if (args.Length >= 3 && !string.IsNullOrWhiteSpace(args[2]))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(args[2])!);
            File.Copy(best.Path, args[2], true);
            Console.WriteLine("ICO -> " + args[2]);
        }
        if (args.Length >= 4 && !string.IsNullOrWhiteSpace(args[3]))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(args[3])!);
            bool done = TryExtractPngFrame(best.Path, args[3]);
            if (!done)
            {
                using var ico = new Icon(best.Path);
                using var bmp = ico.ToBitmap();
                bmp.Save(args[3], ImageFormat.Png);
                Console.WriteLine($"PNG -> {args[3]} ({bmp.Width}x{bmp.Height} {bmp.PixelFormat}) [GDI]");
            }
        }
        return 0;
    }

    /// <summary>Vista+ 图标帧常为 PNG 压缩：直接从 ico 目录里抠出 PNG 帧。</summary>
    static bool TryExtractPngFrame(string icoPath, string pngPath)
    {
        byte[] data = File.ReadAllBytes(icoPath);
        if (data.Length < 22) return false;
        short count = BitConverter.ToInt16(data, 2);
        long dataStart = 6 + 14L * count;
        for (int i = 0; i < count; i++)
        {
            int entry = 6 + i * 16; // 标准 .ico 目录项为 16 字节
            int size = BitConverter.ToInt32(data, entry + 8);
            int offset = BitConverter.ToInt32(data, entry + 12);
            if (offset + size <= data.Length && size > 8 &&
                data[offset] == 0x89 && data[offset + 1] == 0x50 &&
                data[offset + 2] == 0x4E && data[offset + 3] == 0x47)
            {
                File.WriteAllBytes(pngPath, data[offset..(offset + size)]);
                Console.WriteLine($"PNG -> {pngPath} ({size} bytes, native PNG frame)");
                return true;
            }
        }
        // 找最大的 BMP 帧也无 PNG 时放弃，交给 GDI 路径
        return false;
    }
}
