using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace XiaoYaOpt
{
    /// <summary>
    /// ECO 客戶端 .hed/.dat 封包讀取。
    /// .hed：每筆 12 bytes = (位移, 壓縮後大小 | 0x80000000 壓縮旗標, 原始大小)。
    /// 第 0 筆是檔名索引：u32 檔案數 + 以 \0 分隔的檔名，依序對應第 1..N 筆。
    /// 壓縮格式由 eco.exe 的 FUN_009d5ef0（及 9d5e50/9d5da0/9d6150/9d5cd0/9d5c80/9d5c30）移植而來：
    /// 位元串流（MSB 先），每個單位先讀 1 位元旗標，0 = 8 位元字元，1 = (目前碼長) 位元 + 256 的字典編號；
    /// 字典最多 0x2000 項，以樹狀保存，滿了用 LRU 淘汰；新字串 = 前一段 + 這一段的前綴（每段最長 100）。
    /// </summary>
    internal static class EcoArchive
    {
        public sealed class Entry
        {
            public int Index;
            public string Name;
            public uint Offset;
            public uint StoredSize;
            public uint RawSize;
            public bool Compressed;
        }

        public static List<Entry> ReadIndex(string hedPath)
        {
            byte[] hed = File.ReadAllBytes(hedPath);
            var list = new List<Entry>();
            for (int i = 0; i + 12 <= hed.Length; i += 12)
            {
                uint size = BitConverter.ToUInt32(hed, i + 4);
                list.Add(new Entry
                {
                    Index = i / 12,
                    Offset = BitConverter.ToUInt32(hed, i),
                    StoredSize = size & 0x7fffffff,
                    Compressed = (size & 0x80000000) != 0,
                    RawSize = BitConverter.ToUInt32(hed, i + 8)
                });
            }
            return list;
        }

        public static byte[] ReadEntry(FileStream dat, Entry e)
        {
            byte[] stored = new byte[e.StoredSize];
            dat.Position = e.Offset;
            int read = 0;
            while (read < stored.Length)
            {
                int n = dat.Read(stored, read, stored.Length - read);
                if (n <= 0) throw new EndOfStreamException();
                read += n;
            }
            return e.Compressed ? Decompress(stored, (int)e.RawSize) : stored;
        }

        /// <summary>開啟封包並依第 0 筆的檔名索引替各筆命名。</summary>
        public static List<Entry> Open(string hedPath, string datPath)
        {
            var entries = ReadIndex(hedPath);
            if (entries.Count == 0) return entries;
            using (var fs = File.OpenRead(datPath))
            {
                byte[] idx = ReadEntry(fs, entries[0]);
                entries[0].Name = "(index)";
                int count = BitConverter.ToInt32(idx, 0), pos = 4;
                for (int i = 1; i <= count && i < entries.Count && pos < idx.Length; i++)
                {
                    int end = Array.IndexOf(idx, (byte)0, pos);
                    if (end < 0) end = idx.Length;
                    entries[i].Name = Encoding.ASCII.GetString(idx, pos, end - pos);
                    pos = end + 1;
                }
            }
            return entries;
        }

        // ------------------------------------------------------------------
        // 解壓縮（FUN_009d5ef0）
        // ------------------------------------------------------------------

        private const int NIL = 0x2000;
        private const int MaxCodes = 0x2000;

        private sealed class State
        {
            public readonly byte[] Chr = new byte[NIL + 1];
            public readonly int[] Parent = new int[NIL + 1];
            public readonly int[] FirstChild = new int[NIL + 1];
            public readonly int[] NextSib = new int[NIL + 1];
            public readonly int[] PrevSib = new int[NIL + 1];
            public readonly int[] LruNext = new int[NIL + 1];   // 往「較新」方向
            public readonly int[] LruPrev = new int[NIL + 1];   // 往「較舊」方向
            public int NextCode = 0x100, Head = NIL, Tail = NIL, CodeBits = 1, Threshold = 2;
            public byte[] Src; public int SrcPos; public uint Cur; public int BitsLeft;

            public State(byte[] src)
            {
                Src = src;
                for (int i = 0; i < 0x100; i++)
                {
                    Chr[i] = (byte)i;
                    Parent[i] = NIL; FirstChild[i] = NIL; PrevSib[i] = NIL; NextSib[i] = NIL;
                }
            }

            private uint NextByte()
            {
                return SrcPos < Src.Length ? Src[SrcPos++] : 0xffffffffu;
            }

            // FUN_009d5da0
            public uint ReadBits(int n)
            {
                uint acc = 0;
                if (BitsLeft < n)
                {
                    do
                    {
                        n -= BitsLeft;
                        acc |= ((1u << BitsLeft) - 1u & Cur) << n;
                        Cur = NextByte();
                        BitsLeft = 8;
                    } while (8 < n);
                }
                BitsLeft -= n;
                return (((1u << n) - 1u) & (Cur >> BitsLeft)) | acc;
            }

            public uint ReadFlag()
            {
                BitsLeft--;
                if (BitsLeft < 0)
                {
                    Cur = NextByte();
                    BitsLeft = 7;
                    return Cur >> 7;
                }
                return Cur >> BitsLeft;
            }

            // FUN_009d5cd0：把 node 插到 after 之後（after = Head 時成為最新）
            public void LruInsert(int node, int after)
            {
                if (Head == NIL) { LruNext[node] = NIL; LruPrev[node] = NIL; Tail = node; Head = node; return; }
                if (after == NIL) { LruPrev[node] = NIL; LruNext[node] = Tail; LruPrev[Tail] = node; Tail = node; return; }
                if (after == Head) { LruPrev[node] = Head; LruNext[node] = NIL; LruNext[Head] = node; Head = node; return; }
                LruPrev[node] = after;
                int n = LruNext[after];
                LruNext[node] = n;
                LruPrev[n] = node;
                LruNext[after] = node;
            }

            // FUN_009d5c80
            public void LruRemove(int node)
            {
                if (node == Tail) { int n = LruNext[node]; Tail = n; LruPrev[n] = NIL; return; }
                int p = LruPrev[node], nx = LruNext[node];
                LruNext[p] = nx; LruPrev[nx] = p;
            }

            // FUN_009d5c30
            public void TreeRemove(int node)
            {
                int p = PrevSib[node], nx = NextSib[node];
                if (p == NIL) FirstChild[Parent[node]] = nx; else NextSib[p] = nx;
                if (nx != NIL) PrevSib[nx] = p;
            }

            // FUN_009d6150
            public void AddStrings(byte[] str, int n, int prevCode, int prevLen)
            {
                if (prevCode == NIL) return;
                int cur = prevCode;
                for (int k = 0; k < n; k++)
                {
                    prevLen++;
                    if (prevLen > 100) return;
                    byte ch = str[k];
                    int c = FirstChild[cur];
                    while (c != NIL && Chr[c] != ch) c = NextSib[c];
                    if (c == NIL)
                    {
                        int code = NextCode;
                        if (code < MaxCodes) NextCode++;
                        else
                        {
                            code = Tail;
                            if (cur == code) return;
                            LruRemove(code);
                            TreeRemove(code);
                        }
                        Chr[code] = ch;
                        Parent[code] = cur;
                        PrevSib[code] = NIL;
                        FirstChild[code] = NIL;
                        int s = FirstChild[cur];
                        NextSib[code] = s;
                        if (s != NIL) PrevSib[s] = code;
                        FirstChild[cur] = code;
                        LruInsert(code, cur < 0x100 ? Head : LruPrev[cur]);
                        c = code;
                    }
                    cur = c;
                }
            }
        }

        public static byte[] Decompress(byte[] src, int rawSize)
        {
            var st = new State(src);
            var dst = new byte[rawSize];
            int dstPos = 0, prevCode = NIL, prevLen = 0;
            var stack = new byte[MaxCodes + 1];
            var str = new byte[MaxCodes + 1];
            if (rawSize == 0) return dst;
            while (true)
            {
                if (st.Threshold <= st.NextCode - 0x100) { st.CodeBits++; st.Threshold *= 2; }
                uint flag = st.ReadFlag();
                int code = (flag & 1) == 0 ? (int)st.ReadBits(8) : (int)st.ReadBits(st.CodeBits) + 0x100;
                if (code >= st.NextCode) throw new InvalidDataException("bad code " + code + " at output " + dstPos);

                // 由 code 往上走到根，取出字串（同時把走過的節點移到 LRU 最新端）
                int n = 0, c = code;
                while (c != NIL)
                {
                    if (c > 0xff && c != st.Head)
                    {
                        int nx = st.LruNext[c];
                        if (c == st.Tail) { st.Tail = nx; st.LruPrev[nx] = NIL; }
                        else { int p = st.LruPrev[c]; st.LruNext[p] = nx; st.LruPrev[nx] = p; }
                        st.LruInsert(c, st.Head);
                    }
                    stack[n++] = st.Chr[c];
                    c = st.Parent[c];
                }
                for (int i = 0; i < n; i++) str[i] = stack[n - 1 - i];
                for (int i = 0; i < n && dstPos < rawSize; i++) dst[dstPos++] = str[i];

                st.AddStrings(str, n, prevCode, prevLen);
                if (dstPos >= rawSize) return dst;
                prevLen = n;
                prevCode = code;
            }
        }
    }
}
