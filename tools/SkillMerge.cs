using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

// Merge Nekogame wiki skill pages (cast time, cooldown, ...) into the client skill table.
// args: gameSsp zhSsp jpSsp wikiHtmlDir outCsv
static class SkillMerge
{
    const int RecBase = 0x30000, RecSize = 728;

    // ---------- client side ----------
    class ClientRec { public uint Id; public int Lv; public string Zh, Orig, Jp, DescZh; public int Mp, Sp; }

    static string U16(byte[] b, int off, int maxBytes)
    {
        int n = 0;
        while (n + 1 < maxBytes && (b[off + n] | b[off + n + 1]) != 0) n += 2;
        return Encoding.Unicode.GetString(b, off, n).Trim();
    }

    // ---------- wiki side ----------
    class WikiLevel { public int Lv; public Dictionary<string, string> Cells = new Dictionary<string, string>(); }
    class WikiSkill
    {
        public string Job, Name, Alias, Type, Target, Range, JobLv, MaxLv, Anchor;
        public List<string> Headers = new List<string>();
        public List<WikiLevel> Levels = new List<WikiLevel>();
    }

    static string Clean(string html)
    {
        string s = Regex.Replace(html, "<[^>]+>", "");
        return WebUtility.HtmlDecode(s).Replace(' ', ' ').Trim();
    }

    static string Norm(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var sb = new StringBuilder();
        foreach (char c in s)
            if (!char.IsWhiteSpace(c) && c != '・' && c != '･' && c != '-' && c != '\'' && c != '’' && c != '.' && c != '!' && c != '?' && c != '~' && c != '〜') sb.Append(c);
        return sb.ToString();
    }

    static List<WikiSkill> ParseWiki(string path, string job)
    {
        string html = File.ReadAllText(path, Encoding.UTF8);
        var list = new List<WikiSkill>();
        var heads = Regex.Matches(html, "<h2 id=\"(?<id>[^\"]*)\"[^>]*>(?<h>.*?)</h2>", RegexOptions.Singleline);
        for (int i = 0; i < heads.Count; i++)
        {
            int start = heads[i].Index + heads[i].Length;
            int end = i + 1 < heads.Count ? heads[i + 1].Index : html.Length;
            string body = html.Substring(start, end - start);
            var metaMatches = Regex.Matches(body, "<div class=\"wiki-skill-meta-line\"><strong>(?<k>[^<]*?):?</strong>(?<v>.*?)</div>", RegexOptions.Singleline);
            if (metaMatches.Count == 0) continue; // not a skill section
            string head = Regex.Replace(heads[i].Groups["h"].Value, "<a [^>]*>.*?</a>", "", RegexOptions.Singleline);
            var sk = new WikiSkill { Job = job, Name = Clean(head), Anchor = heads[i].Groups["id"].Value };
            foreach (Match m in metaMatches)
            {
                string k = Clean(m.Groups["k"].Value).TrimEnd(':'), v = Clean(m.Groups["v"].Value);
                switch (k)
                {
                    case "Original alias": sk.Alias = v; break;
                    case "Type": sk.Type = v; break;
                    case "Target": sk.Target = v; break;
                    case "Range": sk.Range = v; break;
                    case "Job Lv": sk.JobLv = v; break;
                    case "Max Lv": sk.MaxLv = v; break;
                }
            }
            var table = Regex.Match(body, "<table>(?<t>.*?)</table>", RegexOptions.Singleline);
            if (table.Success)
            {
                foreach (Match th in Regex.Matches(table.Groups["t"].Value, "<th[^>]*>(?<c>.*?)</th>", RegexOptions.Singleline))
                    sk.Headers.Add(Clean(th.Groups["c"].Value));
                var tbody = Regex.Match(table.Groups["t"].Value, "<tbody>(?<b>.*?)</tbody>", RegexOptions.Singleline);
                int rowNo = 0;
                foreach (Match tr in Regex.Matches(tbody.Groups["b"].Value, "<tr>(?<r>.*?)</tr>", RegexOptions.Singleline))
                {
                    rowNo++;
                    var cells = Regex.Matches(tr.Groups["r"].Value, "<td[^>]*>(?<c>.*?)</td>", RegexOptions.Singleline).Cast<Match>().Select(m => Clean(m.Groups["c"].Value)).ToList();
                    var wl = new WikiLevel();
                    for (int c = 0; c < cells.Count && c < sk.Headers.Count; c++) wl.Cells[sk.Headers[c]] = cells[c];
                    int lv;
                    string lvText;
                    wl.Lv = wl.Cells.TryGetValue("Lv", out lvText) && int.TryParse(lvText, out lv) ? lv : rowNo;
                    sk.Levels.Add(wl);
                }
            }
            list.Add(sk);
        }
        return list;
    }

    static string Get(WikiLevel wl, string key)
    {
        if (wl == null) return "";
        string v;
        return wl.Cells.TryGetValue(key, out v) ? v : "";
    }

    // "3 sec" -> "3"; anything else returned as-is
    static string Seconds(string v)
    {
        var m = Regex.Match(v ?? "", @"^\s*([0-9]+(?:\.[0-9]+)?)\s*sec\s*$");
        return m.Success ? m.Groups[1].Value : (v ?? "");
    }

    static string Csv(string s)
    {
        s = (s ?? "").Replace("$R", " ").Replace("\r", " ").Replace("\n", " ");
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    static bool NumEq(string wikiVal, int clientVal)
    {
        int w;
        return int.TryParse((wikiVal ?? "").Trim(), out w) && w == clientVal;
    }

    static void Main(string[] args)
    {
        byte[] g = File.ReadAllBytes(args[0]), zh = File.ReadAllBytes(args[1]), jp = File.ReadAllBytes(args[2]);
        string wikiDir = args[3], outCsv = args[4];

        // client records
        var recs = new List<ClientRec>();
        int count = (g.Length - RecBase) / RecSize;
        for (int r = 0; r < count; r++)
        {
            int o = RecBase + r * RecSize;
            recs.Add(new ClientRec
            {
                Id = BitConverter.ToUInt32(g, o), Lv = g[o + 0x286],
                Zh = U16(zh, o + 4, 128), Orig = U16(g, o + 4, 128), Jp = U16(jp, o + 4, 128),
                DescZh = U16(zh, o + 0x84, 512),
                Mp = BitConverter.ToUInt16(g, o + 0x288), Sp = BitConverter.ToUInt16(g, o + 0x28a)
            });
        }
        var byId = recs.GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.ToList());
        var byJp = recs.Where(x => x.Jp.Length > 0).GroupBy(x => Norm(x.Jp)).ToDictionary(x => x.Key, x => x.Select(y => y.Id).Distinct().ToList());
        var byEn = recs.Where(x => x.Orig.Length > 0).GroupBy(x => Norm(x.Orig)).ToDictionary(x => x.Key, x => x.Select(y => y.Id).Distinct().ToList());

        // wiki skills
        var wiki = new List<WikiSkill>();
        foreach (string f in Directory.GetFiles(wikiDir, "*.html").OrderBy(x => x))
            wiki.AddRange(ParseWiki(f, Path.GetFileNameWithoutExtension(f)));

        // match wiki skill -> client id
        var matchOf = new Dictionary<WikiSkill, uint>();
        var howOf = new Dictionary<WikiSkill, string>();
        int byJpCount = 0, byEnCount = 0, ambiguous = 0;
        foreach (var w in wiki)
        {
            List<uint> cands = null; string how = null;
            if (byJp.TryGetValue(Norm(w.Alias), out cands)) how = "日文名";
            else if (byEn.TryGetValue(Norm(w.Name), out cands)) how = "英文名";
            if (cands == null) continue;
            int maxLv; int.TryParse(w.MaxLv, out maxLv);
            // score candidates
            uint best = 0; int bestScore = int.MinValue;
            foreach (uint id in cands)
            {
                var lvls = byId[id];
                int score = 0;
                if (Norm(lvls[0].Orig) == Norm(w.Name)) score += 100;
                if (maxLv > 0 && lvls.Max(x => x.Lv) == maxLv) score += 50;
                foreach (var wl in w.Levels)
                {
                    var cr = lvls.FirstOrDefault(x => x.Lv == wl.Lv);
                    if (cr == null) continue;
                    if (NumEq(Get(wl, "MP"), cr.Mp)) score += 10;
                    if (NumEq(Get(wl, "SP"), cr.Sp)) score += 10;
                }
                if (id < 10000) score += 5; // player skills use low ids
                if (score > bestScore || (score == bestScore && id < best)) { bestScore = score; best = id; }
            }
            if (cands.Count > 1) { ambiguous++; how += "（同名" + cands.Count + "個，已依英文名/等級/MP/SP擇一）"; }
            matchOf[w] = best; howOf[w] = how;
            if (how.StartsWith("日文名")) byJpCount++; else byEnCount++;
        }

        // MP/SP verification over matched levels
        int cmp = 0, agree = 0;
        foreach (var kv in matchOf)
            foreach (var wl in kv.Key.Levels)
            {
                var cr = byId[kv.Value].FirstOrDefault(x => x.Lv == wl.Lv);
                if (cr == null) continue;
                foreach (var pair in new[] { Tuple.Create("MP", cr.Mp), Tuple.Create("SP", cr.Sp) })
                {
                    string wv = Get(wl, pair.Item1);
                    if (wv.Length == 0) continue;
                    cmp++;
                    if (NumEq(wv, pair.Item2)) agree++;
                    else Console.WriteLine("  MISMATCH [{0}] {1} ({2}) id={3} lv={4} {5}: wiki={6} client={7}",
                        kv.Key.Job, kv.Key.Name, kv.Key.Alias, kv.Value, wl.Lv, pair.Item1, wv, pair.Item2);
                }
            }

        // group wiki skills by matched id (a skill may appear in several jobs)
        var wikiById = matchOf.GroupBy(kv => kv.Value).ToDictionary(x => x.Key, x => x.Select(y => y.Key).ToList());

        string[] mainCols = { "Lv", "MP", "SP", "Cast time", "Cooldown", "Duration" };
        using (var wr = new StreamWriter(outCsv, false, new UTF8Encoding(true)))
        {
            wr.WriteLine("技能ID,等級,中文名稱,原文名稱,日文名稱,MP消耗(客戶端),SP消耗(客戶端),官網MP,官網SP,詠唱時間(秒),冷卻時間(秒),持續時間(秒),職業,官網英文名,類型,目標,射程,習得職業等級,官網其他數值,官網對應方式,說明");
            foreach (var cr in recs)
            {
                if (cr.Zh.Length == 0 && cr.Orig.Length == 0) continue;
                List<WikiSkill> ws;
                WikiSkill w = null; WikiLevel wl = null;
                if (wikiById.TryGetValue(cr.Id, out ws))
                {
                    w = ws[0];
                    wl = w.Levels.FirstOrDefault(x => x.Lv == cr.Lv) ?? (w.Levels.Count == 1 ? w.Levels[0] : null);
                }
                string jobs = ws == null ? "" : string.Join("、", ws.Select(x => x.Job).Distinct());
                string other = wl == null ? "" : string.Join("; ", wl.Cells.Where(c => !mainCols.Contains(c.Key)).Select(c => c.Key + "=" + c.Value));
                wr.WriteLine(string.Join(",", new[] {
                    cr.Id.ToString(), cr.Lv.ToString(), Csv(cr.Zh), Csv(cr.Orig), Csv(cr.Jp),
                    cr.Mp.ToString(), cr.Sp.ToString(), Csv(Get(wl, "MP")), Csv(Get(wl, "SP")),
                    Csv(Seconds(Get(wl, "Cast time"))), Csv(Seconds(Get(wl, "Cooldown"))), Csv(Seconds(Get(wl, "Duration"))),
                    Csv(jobs), Csv(w == null ? "" : w.Name), Csv(w == null ? "" : w.Type), Csv(w == null ? "" : w.Target),
                    Csv(w == null ? "" : w.Range), Csv(w == null ? "" : w.JobLv), Csv(other),
                    Csv(w == null ? "" : howOf[w]), Csv(cr.DescZh) }));
            }
            // wiki skills with no client match: append so nothing is lost
            foreach (var w in wiki.Where(x => !matchOf.ContainsKey(x)))
            {
                var levels = w.Levels.Count > 0 ? w.Levels : new List<WikiLevel> { null };
                foreach (var wl in levels)
                {
                    string other = wl == null ? "" : string.Join("; ", wl.Cells.Where(c => !mainCols.Contains(c.Key)).Select(c => c.Key + "=" + c.Value));
                    wr.WriteLine(string.Join(",", new[] {
                        "", wl == null ? "" : wl.Lv.ToString(), "\"\"", "\"\"", Csv(w.Alias),
                        "\"\"", "\"\"", Csv(Get(wl, "MP")), Csv(Get(wl, "SP")),
                        Csv(Seconds(Get(wl, "Cast time"))), Csv(Seconds(Get(wl, "Cooldown"))), Csv(Seconds(Get(wl, "Duration"))),
                        Csv(w.Job), Csv(w.Name), Csv(w.Type), Csv(w.Target), Csv(w.Range), Csv(w.JobLv), Csv(other),
                        Csv("客戶端找不到對應技能"), "\"\"" }));
                }
            }
        }

        int withCast = wiki.Count(w => w.Headers.Contains("Cast time"));
        int withCd = wiki.Count(w => w.Headers.Contains("Cooldown"));
        Console.WriteLine("wiki skills parsed: {0} (with Cast time: {1}, with Cooldown: {2})", wiki.Count, withCast, withCd);
        Console.WriteLine("matched to client: {0} (by JP name {1}, by EN name {2}); ambiguous name groups resolved: {3}; unmatched: {4}",
            matchOf.Count, byJpCount, byEnCount, ambiguous, wiki.Count - matchOf.Count);
        Console.WriteLine("distinct client skill ids enriched: {0}", wikiById.Count);
        Console.WriteLine("MP/SP cross-check on matched levels: {0}/{1} agree ({2:P1})", agree, cmp, cmp == 0 ? 0 : (double)agree / cmp);
        foreach (var kv in wikiById)
        {
            var names = kv.Value.Select(x => Norm(x.Name)).Distinct().ToList();
            if (names.Count > 1)
                Console.WriteLine("  CONFLICT id={0}: {1}", kv.Key, string.Join(" | ", kv.Value.Select(x => "[" + x.Job + "] " + x.Name + " / " + x.Alias)));
        }
        Console.WriteLine("shared ids (same skill on several job pages): {0}", wikiById.Count(kv => kv.Value.Count > 1));
        Console.WriteLine("unmatched wiki skills:");
        foreach (var w in wiki.Where(x => !matchOf.ContainsKey(x)))
            Console.WriteLine("  [{0}] {1} / {2}", w.Job, w.Name, w.Alias);
    }
}
