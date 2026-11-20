using System;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using UiTimer=System.Windows.Forms.Timer;

[assembly: AssemblyTitle("划译 · Windows 划词翻译")]
[assembly: AssemblyProduct("划译")]
[assembly: AssemblyDescription("为鼠标选区提供中英等语言的就地翻译")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: InternalsVisibleTo("HuaYi.Checks")]

namespace HuaYi
{
    internal static class Program
    {
        internal const string GateName=@"Local\HuaYi.SelectionTray.7F6BA4";
        internal const string OpenName=@"Local\HuaYi.OpenDesk.7F6BA4";
        [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint pid);
        [STAThread] private static int Main() { return Launch(GateName,OpenName); }
        internal static int Launch(string mutexName,string eventName)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                using (var gate=new Mutex(false,mutexName))
                {
                    bool own=false;
                    try
                    {
                        try { own=gate.WaitOne(0); } catch(AbandonedMutexException) { own=true; }
                        if (!own)
                        {
                            if (RequestExisting(eventName)) return 0;
                            MessageBox.Show("另一个划译进程仍在运行，但没有可唤回的控制窗口。若是旧版，请在任务管理器中结束旧的「划译」，再打开本窗口版。","划译 · 旧版仍在运行",MessageBoxButtons.OK,MessageBoxIcon.Information);
                            return 2;
                        }
                        ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
                        using (var signal=new EventWaitHandle(false,EventResetMode.AutoReset,eventName))
                        using (var icon=LoadIcon())
                        using (var app=new TrayApp(icon))
                        {
                            app.WatchOpenRequests(signal);
                            Application.Run(app);
                        }
                        return 0;
                    }
                    finally { if(own) gate.ReleaseMutex(); }
                }
            }
            catch(Exception ex)
            {
                MessageBox.Show("控制窗口未能启动。请把以下错误代码发给开发者：\n"+ex.GetType().Name+" / 0x"+ex.HResult.ToString("X8"),"划译 · 启动错误",MessageBoxButtons.OK,MessageBoxIcon.Error);
                return 1;
            }
        }
        private static Icon LoadIcon()
        {
            try { return Icon.ExtractAssociatedIcon(typeof(Program).Assembly.Location) ?? (Icon)SystemIcons.Application.Clone(); }
            catch(ArgumentException) { return (Icon)SystemIcons.Application.Clone(); }
        }
        private static bool RequestExisting(string name)
        {
            // The event survives while the first instance is alive; an early signal is retained.
            for(int i=0;i<12;i++)
            {
                try
                {
                    using(var signal=EventWaitHandle.OpenExisting(name))
                    using(var self=Process.GetCurrentProcess())
                    {
                        foreach(var other in Process.GetProcessesByName(self.ProcessName))
                        using(other) try {
                            if(other.Id!=self.Id && other.SessionId==self.SessionId && other.MainWindowTitle==ControlDesk.Caption)
                                AllowSetForegroundWindow((uint)other.Id);
                        } catch(InvalidOperationException) { } catch(System.ComponentModel.Win32Exception) { }
                        return signal.Set();
                    }
                } catch(WaitHandleCannotBeOpenedException) { Thread.Sleep(75); }
            }
            return false;
        }
    }

    internal sealed class ControlDesk : Form
    {
        internal const string Caption="划译 · 控制窗口";
        internal const string Sample="Good morning.";
        internal event Action<UseMode> ChooseMode;
        internal event Action<int> ChooseDirection;
        internal event Action ClipboardRequested;
        internal event Action<string,Point,bool> SampleRequested;
        private readonly Button autoBtn,manualBtn,pauseBtn,copyBtn,tryBtn;
        private readonly Label state,activity,sampleStatus;
        private readonly TextBox sample;
        private readonly ComboBox language;
        internal ControlDesk(Icon icon)
        {
            Text=Caption; Name="HuaYiControlDesk"; Icon=icon; Font=Style.FontAt(10,false);
            AutoScaleDimensions=new SizeF(96,96); AutoScaleMode=AutoScaleMode.Dpi;
            ClientSize=new Size(530,453); StartPosition=FormStartPosition.CenterScreen;
            FormBorderStyle=FormBorderStyle.FixedSingle; MaximizeBox=false; MinimizeBox=true;
            ShowInTaskbar=true; BackColor=Color.White;
            Controls.Add(new Panel {Bounds=new Rectangle(0,0,530,4),BackColor=Style.Teal});
            Style.LabelAt(this,"划 译    /    WINDOWS",22,12,325,23,9,true,Style.Teal);
            Style.LabelAt(this,"划词翻译",20,38,230,44,21,true,Style.Ink);
            state=Style.LabelAt(this,"",273,44,233,31,11,true,Style.Teal);
            state.Name="modeStatus"; state.TextAlign=ContentAlignment.MiddleRight;
            Style.LabelAt(this,"开启自动翻译后，划选的文字会发送给 MyMemory，服务方可能长期保存。处理私密资料时，请选择手动模式或暂停。",22,88,486,51,10,false,Style.Muted);
            autoBtn=Style.ButtonAt(this,"开启自动翻译",23,148,154,39,true); autoBtn.Name="automatic";
            manualBtn=Style.ButtonAt(this,"仅手动翻译",187,148,152,39,false); manualBtn.Name="manual";
            pauseBtn=Style.ButtonAt(this,"暂停翻译",349,148,157,39,false); pauseBtn.Name="pause";
            autoBtn.Click+=delegate { Change(UseMode.Automatic); }; manualBtn.Click+=delegate { Change(UseMode.Manual); };
            pauseBtn.Click+=delegate { Change(UseMode.Paused); };
            var exercise=new Panel {Bounds=new Rectangle(23,204,483,111),BackColor=Color.FromArgb(234,244,241)};
            Controls.Add(exercise);
            Style.LabelAt(exercise,"在这里试一次",12,6,208,24,10,true,Style.Teal);
            sample=new TextBox { Name="sampleText",Text=Sample,ReadOnly=true,HideSelection=false,
                Bounds=new Rectangle(13,36,263,28),Font=Style.FontAt(11,false),BackColor=Color.White,ForeColor=Style.Ink };
            exercise.Controls.Add(sample);
            sample.MouseUp+=delegate(object sender,MouseEventArgs e) {
                if(e.Button==(SystemInformation.MouseButtonsSwapped ? MouseButtons.Right : MouseButtons.Left))
                    EmitSample(sample.SelectedText,sample.PointToScreen(e.Location),true);
            };
            tryBtn=Style.ButtonAt(exercise,"试译这句英文",294,32,175,35,false); tryBtn.Name="trySample";
            tryBtn.Click+=delegate { EmitSample(Sample,tryBtn.PointToScreen(new Point(8,tryBtn.Height)),false); };
            sampleStatus=Style.LabelAt(exercise,"",12,71,456,31,9,false,Style.Ink); sampleStatus.Name="sampleResult";
            Style.LabelAt(this,"翻译方向",23,328,73,32,9,true,Style.Ink);
            language=new ComboBox { Name="directions",DropDownStyle=ComboBoxStyle.DropDownList,Bounds=new Rectangle(95,330,212,28),Font=Style.FontAt(9,false) };
            language.Items.Add(TextRules.Automatic); foreach(var item in TextRules.Options) language.Items.Add(item.Title);
            language.SelectedIndex=0; Controls.Add(language);
            language.SelectedIndexChanged+=delegate { var f=ChooseDirection;if(f!=null)f(language.SelectedIndex-1); };
            copyBtn=Style.ButtonAt(this,"翻译已复制文字…",320,325,186,35,false); copyBtn.Name="copiedText";
            copyBtn.Click+=delegate { var f=ClipboardRequested; if(f!=null)f(); };
            activity=Style.LabelAt(this,"",23,367,482,41,9,false,Style.Muted); activity.Name="activity";
            Controls.Add(new Panel {Bounds=new Rectangle(23,413,483,1),BackColor=Color.FromArgb(213,226,224)});
            Style.LabelAt(this,"最小化后仍在任务栏。再次双击程序可找回窗口；点 × 完全退出。",23,418,487,29,9,false,Style.Muted);
            Shown+=delegate { BeginInvoke((Action)Reveal); };
        }
        private void Change(UseMode mode) { var f=ChooseMode; if(f!=null) f(mode); }
        internal void SetDirection(int d) { if(language.SelectedIndex!=d+1)language.SelectedIndex=d+1; }
        internal void ShowMode(UseMode mode,bool keyOk)
        {
            bool paused=mode==UseMode.Paused;
            state.Text=paused ? "已暂停 · 未联网" : (mode==UseMode.Automatic ? "自动翻译已开启" : "仅手动翻译");
            state.ForeColor=paused ? Style.Muted : Style.Teal;
            autoBtn.Enabled=mode!=UseMode.Automatic; manualBtn.Enabled=mode!=UseMode.Manual; pauseBtn.Enabled=!paused;
            tryBtn.Enabled=!paused; copyBtn.Enabled=!paused;
            sampleStatus.Text=paused ? "先选一种翻译模式，再试译。未开启时不会读取选区。" : "可点按钮试译；自动模式也可以在上面的英文框里拖选。";
            activity.Text=paused ? "程序正在运行。点「开启自动翻译」后，可到其他软件划选文字。" :
                (!keyOk ? "快捷键被其他软件占用。仍可划词，或使用上方的剪贴板按钮。" :
                (mode==UseMode.Automatic ? "请在支持的软件中拖选文字或双击单词。译文会出现在光标旁。" : "在其他软件选中文字，再按 Ctrl + Alt + T。不会自动发送选区。"));
        }
        internal bool SampleHasFocus { get { return sample.Focused; } }
        private void EmitSample(string text,Point at,bool fromMouse)
        { var f=SampleRequested;if(f!=null && !String.IsNullOrWhiteSpace(text))f(text,at,fromMouse); }
        internal void RequestSelectedSample() { EmitSample(sample.SelectedText,sample.PointToScreen(new Point(4,sample.Height)),false); }
        internal void SetSampleStatus(string s) { sampleStatus.Text=s; }
        internal void SetActivity(string s) { activity.Text=s; }
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr w,int cmd);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr w);
        [DllImport("user32.dll")] private static extern bool FlashWindow(IntPtr w,bool invert);
        internal void Reveal()
        {
            if(IsDisposed || Disposing) return;
            if(WindowState==FormWindowState.Minimized) WindowState=FormWindowState.Normal;
            bool reachable=false;
            foreach(var screen in Screen.AllScreens) {
                Rectangle overlap=Rectangle.Intersect(Bounds,screen.WorkingArea);
                if(overlap.Width>=90 && overlap.Height>=50 && Top>=screen.WorkingArea.Top) { reachable=true; break; }
            }
            if(!reachable) CenterToScreen();
            if(!Visible) Show();
            ShowWindow(Handle,9); // SW_RESTORE; also works when an external launcher initially hid us.
            BringToFront(); Activate(); if(!SetForegroundWindow(Handle)) FlashWindow(Handle,true);
        }
    }

    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        internal event Action Pressed;
        private bool registered;
        internal HotkeyWindow()
        { CreateHandle(new CreateParams { Caption="HuaYi.Hotkey", Parent=new IntPtr(-3) }); }
        internal bool Register()
        { if (!registered) registered=Native.RegisterHotKey(Handle,1,0x4003,(uint)Keys.T); return registered; }
        internal void Unregister()
        { if (registered) Native.UnregisterHotKey(Handle,1); registered=false; }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg==0x312 && m.WParam.ToInt32()==1)
            { var handler=Pressed; if (handler!=null) handler(); }
            base.WndProc(ref m);
        }
        public void Dispose() { Unregister(); DestroyHandle(); }
    }

    internal sealed class TrayApp : ApplicationContext
    {
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu=new ContextMenuStrip();
        private readonly ToolStripMenuItem auto,manual,pause,clip;
        private readonly ToolStripMenuItem[] langs;
        private readonly Bubble bubble=new Bubble();
        private readonly Translator translator;
        private readonly HotkeyWindow hotkey;
        internal readonly ControlDesk Desk;
        private RegisteredWaitHandle openWait;
        private readonly bool desktop;
        private readonly UiTimer timer=new UiTimer();
        private readonly Stopwatch clock=Stopwatch.StartNew();
        private readonly GestureClassifier gesture=new GestureClassifier();
        private readonly int pid=Process.GetCurrentProcess().Id;
        private readonly bool swapped=SystemInformation.MouseButtonsSwapped;
        private UseMode mode=UseMode.Paused;
        private int direction=-1;
        private bool wasDown,tracking,stopping,disposed;
        private Point downAt;
        private IntPtr downWindow,owner;
        private CancellationTokenSource pending;
        // A single in-memory repeat result, not a history; expired or cleared on a mode change.
        private string lastText,lastPair,lastValue;
        private long cacheUntil;

        internal TrayApp(Icon icon) : this(icon,new Translator(),true) { }
        // Offline UI tests supply a fixture translator; the product always uses the desktop constructor.
        internal TrayApp(Icon icon,Translator service,bool useDesktop)
        {
            translator=service; desktop=useDesktop;
            Desk=new ControlDesk(icon);
            MainForm=Desk; // Visible taskbar window, including while translation is paused.
            if(desktop) hotkey=new HotkeyWindow();
            menu.Font=Style.FontAt(9,false);
            var caption=new ToolStripMenuItem("划译  ·  选区翻译") { Enabled=false };
            menu.Items.Add(caption); menu.Items.Add(new ToolStripSeparator());
            Item(menu.Items,"打开控制窗口",delegate { Desk.Reveal(); });
            auto=Item(menu.Items,"自动翻译划选文字",delegate { SelectMode(UseMode.Automatic,true); });
            manual=Item(menu.Items,"仅手动翻译（Ctrl + Alt + T）",delegate { SelectMode(UseMode.Manual,false); });
            pause=Item(menu.Items,"暂停翻译",delegate { SelectMode(UseMode.Paused,false); });
            menu.Items.Add(new ToolStripSeparator());
            var language=new ToolStripMenuItem("翻译方向"); menu.Items.Add(language);
            langs=new ToolStripMenuItem[TextRules.Options.Length+1];
            for (int i=0;i<langs.Length;i++)
            {
                int index=i-1;
                langs[i]=Item(language.DropDownItems,i==0 ? TextRules.Automatic : TextRules.Options[index].Title,
                    delegate { SetDirection(index); });
            }
            UpdateLanguages();
            clip=Item(menu.Items,"翻译已复制文字…",delegate { TranslateClipboard(); });
            Item(menu.Items,"用法与隐私",delegate {
                MessageBox.Show("自动模式：在前台软件中拖选或双击文字。\n手动模式：选中文字后按 Ctrl + Alt + T。\n\n没有反应？可能是软件不提供可读取的选区。手动复制，再从本菜单选择「翻译已复制文字…」，核对预览后再发送。\n\n译文来自 MyMemory，选区会联网发送，服务方可能长期保存。不适用于机密资料。程序不保存历史，不改写原文，也不监视剪贴板。识别为密码框时跳过；不能保证识别所有敏感文字。\n\n自动判断是文字特征推测，猜错时可手工选择翻译方向。","划译 · 用法与隐私",MessageBoxButtons.OK,MessageBoxIcon.Information);
            });
            menu.Items.Add(new ToolStripSeparator());
            Item(menu.Items,"退出划译",delegate { Desk.Close(); });
            menu.Opening+=delegate { InvalidateRequest(); tracking=false; gesture.Reset(); };
            if(desktop)
            {
                try {
                    tray=new NotifyIcon { Icon=icon, ContextMenuStrip=menu, Text="划译" };
                    tray.DoubleClick+=delegate { Desk.Reveal(); };
                    tray.Visible=true;
                } catch(ExternalException) { if(tray!=null) tray.Dispose(); tray=null; }
            }
            if(hotkey!=null) hotkey.Pressed+=delegate {
                Point p=Cursor.Position; IntPtr w=Native.Foreground();
                if(w==Desk.Handle && Desk.SampleHasFocus) { Desk.RequestSelectedSample(); return; }
                if(w!=IntPtr.Zero && !Native.IsOurs(w,pid)) StartSelection(Trigger.Shortcut,w,p,p);
            };
            Desk.ChooseMode+=delegate(UseMode m) { SelectMode(m,false); };
            Desk.ChooseDirection+=SetDirection;
            Desk.ClipboardRequested+=TranslateClipboard;
            Desk.SampleRequested+=StartSample;
            timer.Tick+=Tick;
            SelectMode(UseMode.Paused,false);
        }
        internal void WatchOpenRequests(EventWaitHandle signal)
        {
            IntPtr h=Desk.Handle;
            openWait=ThreadPool.RegisterWaitForSingleObject(signal,delegate(object state,bool expired) {
                try { Desk.BeginInvoke((Action)delegate { if(!stopping && !Desk.IsDisposed) Desk.Reveal(); }); }
                catch(InvalidOperationException) { }
            },null,Timeout.Infinite,false);
        }
        private void SetDirection(int index)
        {
            if(direction==index) return;
            direction=index; ClearCache(); InvalidateRequest(); UpdateLanguages();
        }
        private static ToolStripMenuItem Item(ToolStripItemCollection group,string label,Action action)
        {
            var it=new ToolStripMenuItem(label);
            it.Click+=delegate {action();}; group.Add(it); return it;
        }
        private void UpdateLanguages()
        { for(int i=0;i<langs.Length;i++) langs[i].Checked=(i==direction+1); Desk.SetDirection(direction); }
        private void SelectMode(UseMode target,bool confirmAuto)
        {
            if (target==UseMode.Automatic && mode!=target && confirmAuto &&
                MessageBox.Show("开启后，在支持的软件中划选的文字会自动发送给 MyMemory 在线翻译。处理敏感内容前请先暂停。\n\n现在开启吗？","开启自动翻译",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes) return;
            InvalidateRequest(); ClearCache(); tracking=false; gesture.Reset();
            mode=target;
            wasDown=desktop && mode==UseMode.Automatic && Native.PrimaryDown(swapped);
            auto.Checked=mode==UseMode.Automatic; manual.Checked=mode==UseMode.Manual; pause.Checked=mode==UseMode.Paused;
            clip.Enabled=mode!=UseMode.Paused;
            string label=auto.Checked ? "自动翻译已开启" : (manual.Checked ? "仅手动翻译" : "已暂停");
            if(tray!=null) tray.Text="划译 · "+label;
            timer.Interval=auto.Checked ? 30 : 100;
            timer.Enabled=desktop && !pause.Checked;
            bool keyOk=true;
            if(hotkey!=null) { if(pause.Checked) hotkey.Unregister(); else keyOk=hotkey.Register(); }
            Desk.ShowMode(mode,keyOk);
        }
        private void ClearCache() { lastText=null; lastPair=null; lastValue=null; cacheUntil=0; }
        private void InvalidateRequest()
        {
            var old=pending; pending=null; owner=IntPtr.Zero;
            if (old!=null) old.Cancel();
            bubble.Dismiss();
        }
        private CancellationTokenSource Begin(IntPtr window)
        { InvalidateRequest(); owner=window; pending=new CancellationTokenSource(); return pending; }
        private bool Current(CancellationTokenSource req)
        {
            return !stopping && pending==req && !req.IsCancellationRequested && mode!=UseMode.Paused
                && (owner==IntPtr.Zero || Native.Foreground()==owner);
        }
        private void End(CancellationTokenSource req)
        {
            if (pending==req) { pending=null; if (!bubble.Visible) owner=IntPtr.Zero; }
            req.Dispose();
        }
        private async void StartSample(string text,Point p,bool fromMouse)
        {
            if(!TriggerPolicy.Allows(mode,fromMouse ? Trigger.Drag : Trigger.Shortcut) || String.IsNullOrWhiteSpace(text)
                || ControlDesk.Sample.IndexOf(text,StringComparison.Ordinal)<0) return;
            var req=Begin(IntPtr.Zero);
            Desk.SetSampleStatus("正在联网试译…");
            try
            {
                string ans=await translator.Translate(text,TextRules.Options[0],req.Token);
                if(!Current(req)) return;
                Desk.SetSampleStatus("示例译文："+ans);
                if(fromMouse && Native.Foreground()==Desk.Handle) bubble.Present(p,TextRules.Options[0].Title,ans,ans);
            }
            catch(Exception ex) { if(Current(req)) Desk.SetSampleStatus(FailureText(ex)); }
            finally { End(req); }
        }
        private void Tick(object sender,EventArgs e)
        {
            if (stopping || menu.Visible) return;
            if (cacheUntil!=0 && clock.ElapsedMilliseconds>cacheUntil) ClearCache();
            if (owner!=IntPtr.Zero && Native.Foreground()!=owner) InvalidateRequest();
            if (bubble.Visible && DateTime.UtcNow>bubble.Expires && !bubble.Bounds.Contains(Cursor.Position))
            { bubble.Dismiss(); if (pending==null) owner=IntPtr.Zero; }
            if (mode!=UseMode.Automatic) return;
            bool down=Native.PrimaryDown(swapped);
            if (down==wasDown) return;
            wasDown=down; Point p=Cursor.Position;
            if (down)
            {
                tracking=false;
                if (bubble.Visible && bubble.Bounds.Contains(p)) return;
                InvalidateRequest();
                IntPtr w=Native.Foreground();
                if (w==IntPtr.Zero || Native.IsOurs(w,pid) || Native.At(p)!=w) { gesture.Reset(); return; }
                tracking=true; downAt=p; downWindow=w;
            }
            else if (tracking)
            {
                tracking=false;
                if (Native.Foreground()!=downWindow || Native.At(p)!=downWindow) { gesture.Reset(); return; }
                Trigger? kind=gesture.Release(downWindow,downAt,p,clock.ElapsedMilliseconds,
                    SystemInformation.DragSize,SystemInformation.DoubleClickSize,SystemInformation.DoubleClickTime);
                if (kind.HasValue) StartSelection(kind.Value,downWindow,downAt,p);
            }
        }
        private async void StartSelection(Trigger trigger,IntPtr window,Point a,Point b)
        {
            if (!TriggerPolicy.Allows(mode,trigger)) return;
            bool mouse=trigger==Trigger.Drag || trigger==Trigger.DoubleClick;
            var req=Begin(window);
            try
            {
                if (mouse) await Task.Delay(190,req.Token); // let the selection settle after button-up
                if (!Current(req)) return;
                var read=await SelectionReader.ReadAsync(window,a,b,mouse,req.Token);
                if (!Current(req)) return;
                if (read.State!=ReadState.Found)
                {
                    if(read.State==ReadState.Unavailable || read.State==ReadState.Busy)
                        Desk.SetActivity("当前软件没提供可读取的选区，可先复制，再点「翻译已复制文字」。");
                    if (read.State==ReadState.Protected && mouse) return;
                    if (read.State==ReadState.Multiple) bubble.Present(b,"提示","请一次只选中一段连续的文字。",null);
                    else if (!mouse) bubble.Present(b,"没有读取文字",read.State==ReadState.Protected ? "已跳过密码控件，没有发送。" :
                        "没有可读取的选区。可以先手动复制，再用托盘菜单「翻译已复制文字…」。",null);
                    return;
                }
                await Deliver(req,read.Value,b);
            }
            catch(Exception ex) { ShowFailure(req,b,ex); }
            finally { End(req); }
        }
        private async void TranslateClipboard()
        {
            if (!TriggerPolicy.Allows(mode,Trigger.Clipboard)) return;
            Point p=Cursor.Position;
            string raw;
            try { raw=TextRules.Prepare(Clipboard.GetText(TextDataFormat.UnicodeText)); }
            catch(TipException ex) { bubble.Present(p,"剪贴板",ex.Message,null); return; }
            catch(ExternalException) { bubble.Present(p,"剪贴板","剪贴板正被其他程序使用，请重试。",null); return; }
            if (MessageBox.Show("以下是将发送给 MyMemory 的剪贴板文字。确认联网翻译？\n\n"+raw,
                "划译 · 确认已复制文字",MessageBoxButtons.OKCancel,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.OK) return;
            if (!TriggerPolicy.Allows(mode,Trigger.Clipboard)) return;
            var req=Begin(IntPtr.Zero);
            try { await Deliver(req,raw,p); }
            catch(Exception ex) { ShowFailure(req,p,ex); }
            finally { End(req); }
        }
        private async Task Deliver(CancellationTokenSource req,string raw,Point p)
        {
            if (!Current(req)) return;
            string text=TextRules.Prepare(raw);
            Direction d=TextRules.Choose(text,direction);
            if (clock.ElapsedMilliseconds<cacheUntil && lastText==text && lastPair==d.Pair)
            { bubble.Present(p,d.Title,lastValue,lastValue); return; }
            bubble.Present(p,d.Title,"正在翻译…",null);
            if (!Current(req)) return;
            string translated=await translator.Translate(text,d,req.Token);
            if (!Current(req)) return;
            lastText=text; lastPair=d.Pair; lastValue=translated; cacheUntil=clock.ElapsedMilliseconds+25000;
            bubble.Present(p,d.Title,translated,translated);
        }
        private void ShowFailure(CancellationTokenSource req,Point p,Exception ex)
        {
            if (!Current(req)) return; // replaced, paused, cancelled or no longer foreground
            string tip=FailureText(ex);
            Desk.SetActivity(tip);
            bubble.Present(p,"未翻译",tip,null);
        }
        private static string FailureText(Exception ex)
        {
            return ex is TipException ? ex.Message :
                (ex is OperationCanceledException ? "等待翻译超时，请检查网络后重试。" :
                (ex is HttpRequestException ? "连不上 MyMemory，请检查网络后再试。" : "这次未能完成翻译，请重试。"));
        }
        protected override void ExitThreadCore()
        {
            stopping=true; timer.Stop(); InvalidateRequest(); ClearCache();
            if(tray!=null) tray.Visible=false; base.ExitThreadCore();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed=true; stopping=true;
                if(openWait!=null){openWait.Unregister(null);openWait=null;}
                timer.Dispose(); InvalidateRequest(); ClearCache(); if(hotkey!=null) hotkey.Dispose();
                if(tray!=null){tray.Visible=false;tray.Dispose();} menu.Dispose(); bubble.Dispose(); translator.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
