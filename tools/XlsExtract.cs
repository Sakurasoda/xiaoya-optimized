using System;
using System.IO;
using System.Text;

namespace XiaoYaOpt
{
    /// <summary>
    /// 把 ECO 客戶端的 .hed/.dat 封包解開到資料夾。
    /// 用法：XlsExtract.exe &lt;封包.hed&gt; &lt;輸出資料夾&gt; [只解這個檔名]
    /// </summary>
    internal static class XlsExtract
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length < 2)
            {
                Console.WriteLine("用法：XlsExtract.exe <封包.hed> <輸出資料夾> [只解這個檔名]");
                return 1;
            }
            string hed = args[0], dat = Path.ChangeExtension(hed, ".dat"), outDir = args[1];
            string only = args.Length > 2 ? args[2] : null;
            var entries = EcoArchive.Open(hed, dat);
            Directory.CreateDirectory(outDir);
            int ok = 0, fail = 0;
            using (var fs = File.OpenRead(dat))
            {
                foreach (var e in entries)
                {
                    string name = e.Index == 0 ? "_index.bin" : (e.Name ?? string.Format("entry_{0:D3}.bin", e.Index));
                    if (only != null && !string.Equals(name, only, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        byte[] data = EcoArchive.ReadEntry(fs, e);
                        string safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
                        File.WriteAllBytes(Path.Combine(outDir, safe), data);
                        ok++;
                    }
                    catch (Exception ex)
                    {
                        fail++;
                        Console.WriteLine("FAIL #{0} {1}: {2}", e.Index, name, ex.Message);
                    }
                }
            }
            Console.WriteLine("entries={0} extracted={1} failed={2}", entries.Count, ok, fail);
            return fail == 0 ? 0 : 2;
        }
    }
}
