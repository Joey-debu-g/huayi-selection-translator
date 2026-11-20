using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace HuaYi
{
    internal sealed class TipException : Exception
    {
        internal TipException(string tip) : base(tip) { }
    }

    internal sealed class Direction
    {
        internal readonly string From, To, Title;
        internal Direction(string from, string to, string title)
        { From = from; To = to; Title = title; }
        internal string Pair { get { return From + "|" + To; } }
    }

    internal static class TextRules
    {
        internal const int MaxBytes = 500;
        internal const string Automatic = "自动判断（中 / 英 / 日 / 韩）";
        internal static readonly Direction[] Options = {
            new Direction("en","zh-CN","英语 → 简体中文"),
            new Direction("zh-CN","en","中文 → 英语"),
            new Direction("ja","zh-CN","日语 → 简体中文"),
            new Direction("ko","zh-CN","韩语 → 简体中文"),
            new Direction("fr","zh-CN","法语 → 简体中文"),
            new Direction("de","zh-CN","德语 → 简体中文"),
            new Direction("es","zh-CN","西班牙语 → 简体中文")
        };

        internal static string Prepare(string input)
        {
            if (String.IsNullOrWhiteSpace(input)) throw new TipException("没有选中文字。");
            // Never silently truncate, split submissions, or forward a whole document.
            if (input.Length > MaxBytes) throw new TipException("这段文字太长，请缩短选区。每次限 500 个 UTF-8 字节。");
            string s = input.Trim();
            if (Encoding.UTF8.GetByteCount(s) > MaxBytes)
                throw new TipException("这段文字太长，请缩短选区。每次限 500 个 UTF-8 字节。");
            if (s.IndexOfAny(new char[] { '\r','\n','\u2028','\u2029' }) >= 0)
                throw new TipException("请一次只选一段文字，不要跨换行。");
            if (!Regex.IsMatch(s, @"\p{L}")) throw new TipException("选区里没有可翻译的文字。");
            if (Regex.IsMatch(s, @"^(?:https?://|www\.)\S+$|^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.IgnoreCase))
                throw new TipException("已跳过网址或邮箱，没有发送。");
            return s;
        }
        internal static Direction Choose(string text, int fixedIndex)
        {
            if (fixedIndex >= 0 && fixedIndex < Options.Length) return Options[fixedIndex];
            if (Regex.IsMatch(text, @"[\u3041-\u30ff\uff66-\uff9f]")) return Options[2];
            if (Regex.IsMatch(text, @"[\u1100-\u11ff\u3131-\u318e\uac00-\ud7a3]")) return Options[3];
            if (Regex.IsMatch(text, @"[\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff]")) return Options[1];
            return Options[0];
        }
    }

    // The primary mouse button is sampled only in automatic mode. No keyboard hook.
    internal sealed class GestureClassifier
    {
        private IntPtr previous;
        private Point last;
        private long time;
        internal void Reset() { previous = IntPtr.Zero; }
        internal Trigger? Release(IntPtr window, Point start, Point end, long ms, Size drag, Size dbl, int doubleMs)
        {
            if (window == IntPtr.Zero) { Reset(); return null; }
            if (Math.Abs(start.X-end.X) >= Math.Max(2,drag.Width/2) ||
                Math.Abs(start.Y-end.Y) >= Math.Max(2,drag.Height/2))
            { Reset(); return Trigger.Drag; }
            if (window == previous && ms >= time && ms-time <= doubleMs &&
                Math.Abs(last.X-end.X) <= Math.Max(2,dbl.Width/2) &&
                Math.Abs(last.Y-end.Y) <= Math.Max(2,dbl.Height/2))
            { Reset(); return Trigger.DoubleClick; }
            previous=window; time=ms; last=end; return null;
        }
    }

    internal sealed class Translator : IDisposable
    {
        private readonly HttpClient http;
        internal Translator()
        {
            var h = new HttpClientHandler { AllowAutoRedirect=false, UseCookies=false,
                AutomaticDecompression=DecompressionMethods.GZip | DecompressionMethods.Deflate };
            http = Create(h);
        }
        // Also used by the offline tests to exercise the real request + decoder.
        internal Translator(HttpMessageHandler handler) { http=Create(handler); }
        private static HttpClient Create(HttpMessageHandler h)
        {
            var client=new HttpClient(h);
            client.Timeout=TimeSpan.FromSeconds(10);
            client.MaxResponseContentBufferSize=256*1024;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("HuaYi/1.0");
            return client;
        }
        internal static Uri Address(string text, Direction d)
        {
            return new Uri("https://api.mymemory.translated.net/get?q="+
                Uri.EscapeDataString(TextRules.Prepare(text))+"&langpair="+Uri.EscapeDataString(d.Pair));
        }
        internal async Task<string> Translate(string text, Direction d, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            using (var res=await http.GetAsync(Address(text,d), ct).ConfigureAwait(false))
            {
                if ((int)res.StatusCode == 429) throw new TipException("翻译额度或请求频率已达上限，请稍后再试。");
                if (!res.IsSuccessStatusCode) throw new TipException("翻译服务暂时不可用（HTTP "+(int)res.StatusCode+"）。");
                string json=await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                return Decode(json);
            }
        }
        internal static string Decode(string json)
        {
            try
            {
                var j=new JavaScriptSerializer { MaxJsonLength=262144, RecursionLimit=40 };
                var root=j.DeserializeObject(json) as IDictionary<string,object>;
                object st,q,data,raw;
                if (root==null || !root.TryGetValue("responseStatus",out st)) throw new FormatException();
                string status=Convert.ToString(st,CultureInfo.InvariantCulture);
                if (status=="429" || (root.TryGetValue("quotaFinished",out q) &&
                    (String.Equals(Convert.ToString(q),"true",StringComparison.OrdinalIgnoreCase) || Convert.ToString(q)=="1")))
                    throw new TipException("翻译额度已用完，请稍后再试。");
                if (status!="200") throw new TipException("服务未能翻译这次选区，请换一小段文字再试。");
                if (!root.TryGetValue("responseData",out data)) throw new FormatException();
                var map=data as IDictionary<string,object>;
                if (map==null || !map.TryGetValue("translatedText",out raw) || !(raw is string)) throw new FormatException();
                string result=WebUtility.HtmlDecode((string)raw).Trim();
                if (result.Length==0 || result.Length>8192) throw new FormatException();
                return result;
            }
            catch (TipException) { throw; }
            catch (Exception) { throw new TipException("服务返回了无法识别的译文，请稍后重试。"); }
        }
        public void Dispose() { http.Dispose(); }
    }
}
