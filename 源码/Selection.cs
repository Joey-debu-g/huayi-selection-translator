using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using Point = System.Drawing.Point;
using UPoint = System.Windows.Point;

namespace HuaYi
{
    internal static class Native
    {
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point pt);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flag);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll",SetLastError=true)] internal static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window,int id);
        internal static IntPtr Root(IntPtr h) { return h==IntPtr.Zero ? h : GetAncestor(h,2); } // GA_ROOT
        internal static IntPtr Foreground() { return Root(GetForegroundWindow()); }
        internal static IntPtr At(Point p) { return Root(WindowFromPoint(p)); }
        internal static bool PrimaryDown(bool swapped) { return (GetAsyncKeyState(swapped ? 2 : 1) & 0x8000)!=0; }
        internal static bool IsOurs(IntPtr h, int pid)
        { uint p; return h!=IntPtr.Zero && GetWindowThreadProcessId(h,out p)!=0 && p==(uint)pid; }
    }

    internal enum ReadState { Found, Empty, Protected, Multiple, Unavailable, Busy }
    internal sealed class ReadResult
    {
        internal readonly ReadState State;
        internal readonly string Value;
        internal ReadResult(ReadState state, string value) { State=state; Value=value; }
        internal static ReadResult Of(ReadState state) { return new ReadResult(state,null); }
    }

    internal static class SelectionReader
    {
        private static readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);

        // UIA providers live in other processes; never wait for one on the UI thread.
        // If a provider hangs we leave at most ONE bounded worker outstanding.
        internal static async Task<ReadResult> ReadAsync(IntPtr window, Point start, Point end, bool mouse, CancellationToken ct)
        {
            if (!await gate.WaitAsync(280,ct).ConfigureAwait(false)) return ReadResult.Of(ReadState.Busy);
            var job=Task.Run(delegate {
                try
                {
                    if (ct.IsCancellationRequested || Native.Foreground()!=window) return ReadResult.Of(ReadState.Empty);
                    AutomationElement point=null;
                    if (Native.At(end)==window) point=AutomationElement.FromPoint(new UPoint(end.X,end.Y));
                    var focus=AutomationElement.FocusedElement;
                    if (Native.Foreground()!=window) return ReadResult.Of(ReadState.Empty);
                    return ReadBound(window,point,focus,start,end,mouse,ct);
                }
                catch (Exception) { return ReadResult.Of(ReadState.Unavailable); }
                finally { gate.Release(); }
            });
            var winner=await Task.WhenAny(job,Task.Delay(2300,ct)).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (winner!=job) return ReadResult.Of(ReadState.Busy);
            return await job.ConfigureAwait(false);
        }

        // Only walk upwards, never enumerate surrounding controls or read document/value text.
        private static List<AutomationElement> PathToWindow(AutomationElement e, IntPtr window, CancellationToken ct)
        {
            var path=new List<AutomationElement>(); bool belongs=false;
            for(int i=0;e!=null && i<60;++i)
            {
                ct.ThrowIfCancellationRequested();
                IntPtr native=new IntPtr(e.Current.NativeWindowHandle);
                if (native!=IntPtr.Zero)
                {
                    if (Native.Root(native)!=window) break;
                    belongs=true;
                }
                path.Add(e);
                if (native==window) break;
                e=TreeWalker.RawViewWalker.GetParent(e);
            }
            if (!belongs) path.Clear();
            return path;
        }
        private static bool IsProtected(List<AutomationElement> path)
        {
            foreach(var el in path) if (el.Current.IsPassword) return true;
            return false;
        }
        private static bool NearRange(TextPatternRange r,Point a,Point b, bool hitPath)
        {
            try
            {
                var rects=r.GetBoundingRectangles();
                if (rects.Length==0) return hitPath;
                foreach(var rect in rects)
                {
                    if (rect.IsEmpty) continue;
                    var near=rect; near.Inflate(12,12);
                    if (near.Contains(a.X,a.Y) || near.Contains(b.X,b.Y)) return true;
                }
                return false;
            }
            catch (NotSupportedException) { return hitPath; }
        }
        private static ReadResult Selected(List<AutomationElement> path, bool mouse, bool hitPath,Point a,Point b,CancellationToken ct)
        {
            bool supported=false;
            foreach(var el in path)
            {
                ct.ThrowIfCancellationRequested();
                object obj;
                if (!el.TryGetCurrentPattern(TextPattern.Pattern,out obj)) continue;
                var pattern=(TextPattern)obj;
                if (pattern.SupportedTextSelection==SupportedTextSelection.None) continue;
                supported=true;
                TextPatternRange[] ranges=pattern.GetSelection();
                if (ranges==null || ranges.Length==0) continue;
                if (ranges.Length!=1) return ReadResult.Of(ReadState.Multiple);
                var r=ranges[0];
                if (r.CompareEndpoints(TextPatternRangeEndpoint.Start,r,TextPatternRangeEndpoint.End)==0) continue;
                if (mouse && !NearRange(r,a,b,hitPath)) continue;
                // 501 is a sentinel: TextRules rejects it instead of sending a clipped prefix.
                string text=r.GetText(TextRules.MaxBytes+1);
                if (!String.IsNullOrWhiteSpace(text)) return new ReadResult(ReadState.Found,text);
            }
            return ReadResult.Of(supported ? ReadState.Empty : ReadState.Unavailable);
        }

        // An explicit window boundary is shared by the real reader and isolated UIA fixture.
        internal static ReadResult ReadBound(IntPtr window, AutomationElement point, AutomationElement focus,
            Point a, Point b, bool mouse, CancellationToken ct)
        {
            if (window==IntPtr.Zero) return ReadResult.Of(ReadState.Unavailable);
            var hit=PathToWindow(point,window,ct);
            var focused=PathToWindow(focus,window,ct);
            if (IsProtected(hit) || IsProtected(focused)) return ReadResult.Of(ReadState.Protected);
            var first=Selected(mouse ? hit : focused,mouse,mouse,a,b,ct);
            if (first.State!=ReadState.Empty && first.State!=ReadState.Unavailable) return first;
            var second=Selected(mouse ? focused : hit,mouse,false,a,b,ct);
            if (second.State==ReadState.Unavailable && first.State==ReadState.Empty) return first;
            return second;
        }
    }
}
