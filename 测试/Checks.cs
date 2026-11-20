using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using HuaYi;
using UWin=System.Windows.Window;

internal static class Checks
{
    private const string Sample="Good morning.";
    private const string Chinese="早上好。";
    private const string Good="{\"responseStatus\":200,\"quotaFinished\":false,\"responseData\":{\"translatedText\":\"早上好。\"}}";
    private static int passes;
    private sealed class Fake : HttpMessageHandler
    {
        internal int Count; internal Uri Last;
        internal string Json=Good; internal HttpStatusCode Status=HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message,CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Count++; Last=message.RequestUri;
            if(message.Method!=HttpMethod.Get) throw new InvalidOperationException("unexpected HTTP method");
            return Task.FromResult(new HttpResponseMessage(Status) { Content=new StringContent(Json,Encoding.UTF8,"application/json") });
        }
    }
    private static void Check(string name,bool pass)
    {
        if(!pass) throw new Exception("FAILED "+name);
        passes++; Console.WriteLine("PASS "+name);
    }
    private static bool HasTip(Action f,string part)
    { try { f(); } catch(TipException ex) { return ex.Message.Contains(part); } return false; }
    private static int Route()
    {
        var mock=new Fake(); string output="(none)";
        IntPtr w=(IntPtr)7; var g=new GestureClassifier();
        var t=g.Release(w,new Point(20,20),new Point(92,20),200,new Size(4,4),new Size(4,4),500);
        if(t!=Trigger.Drag) throw new Exception("fixture drag not detected");
        bool allow=TriggerPolicy.Allows(UseMode.Automatic,t.Value);
        using(var service=new Translator(mock))
            if(allow) output=service.Translate(Sample,TextRules.Choose(Sample,-1),CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine("input=Automatic / primary drag / Good morning.");
        Console.WriteLine("allowed="+allow.ToString().ToLowerInvariant()+"; mock_requests="+mock.Count+"; translation="+output);
        Console.WriteLine("manual_shortcut="+TriggerPolicy.Allows(UseMode.Manual,Trigger.Shortcut).ToString().ToLowerInvariant());
        return 0;
    }
    private static int Offline()
    {
        Check("automatic drag and double-click",TriggerPolicy.Allows(UseMode.Automatic,Trigger.Drag) && TriggerPolicy.Allows(UseMode.Automatic,Trigger.DoubleClick));
        Check("manual mode never auto-reads",!TriggerPolicy.Allows(UseMode.Manual,Trigger.Drag) && !TriggerPolicy.Allows(UseMode.Manual,Trigger.DoubleClick));
        foreach(Trigger t in Enum.GetValues(typeof(Trigger))) Check("paused rejects "+t,!TriggerPolicy.Allows(UseMode.Paused,t));
        Check("manual shortcut and explicit clipboard",TriggerPolicy.Allows(UseMode.Manual,Trigger.Shortcut) && TriggerPolicy.Allows(UseMode.Manual,Trigger.Clipboard));
        var sz=new Size(4,4); var a=new Point(10,10); var b=new Point(98,10); var w=(IntPtr)8;
        var g=new GestureClassifier();
        Check("ordinary single click ignored",g.Release(w,a,a,100,sz,sz,500)==null);
        Check("same-window double-click",g.Release(w,a,a,210,sz,sz,500)==Trigger.DoubleClick);
        Check("drag on release",g.Release(w,a,b,680,sz,sz,500)==Trigger.Drag);
        Check("no double-click across windows",g.Release(w,a,a,800,sz,sz,500)==null && g.Release((IntPtr)9,a,a,900,sz,sz,500)==null);
        g.Reset(); g.Release(w,a,a,1000,sz,sz,500);
        Check("late second click ignored",g.Release(w,a,a,1600,sz,sz,500)==null);
        Check("trim without changing punctuation",TextRules.Prepare("  C++ & C#?  ")=="C++ & C#?");
        Check("empty ignored",HasTip(delegate {TextRules.Prepare(" ");},"没有"));
        Check("no paragraphs batched",HasTip(delegate {TextRules.Prepare("one\r\ntwo");},"一段"));
        Check("punctuation and digits ignored",HasTip(delegate {TextRules.Prepare("123!?");},"没有"));
        Check("whole URL skipped",HasTip(delegate {TextRules.Prepare("https://example.invalid/private");},"跳过"));
        Check("whole email skipped",HasTip(delegate {TextRules.Prepare("a@example.invalid");},"跳过"));
        Check("exactly 500 ASCII bytes",TextRules.Prepare(new string('a',500)).Length==500);
        Check("501 sentinel not truncated",HasTip(delegate {TextRules.Prepare(new string('a',501));},"太长"));
        Check("UTF-8 byte limit, not character count",HasTip(delegate {TextRules.Prepare(new string('好',167));},"太长") && TextRules.Prepare(new string('好',166)).Length==166);
        Check("EN to zh-CN",TextRules.Choose("Good morning.",-1).Pair=="en|zh-CN");
        Check("ZH to en",TextRules.Choose("你好",-1).Pair=="zh-CN|en");
        Check("kana before Han",TextRules.Choose("今日は晴れ",-1).Pair=="ja|zh-CN");
        Check("Hangul",TextRules.Choose("안녕하세요",-1).Pair=="ko|zh-CN");
        Check("manual language override",TextRules.Choose("Bonjour",4).Pair=="fr|zh-CN");
        Check("HTML entities decoded as plain text",Translator.Decode("{\"responseStatus\":\"200\",\"responseData\":{\"translatedText\":\"a &amp; b &lt;b&gt;\"}}")=="a & b <b>");
        Check("quota inside HTTP 200",HasTip(delegate {Translator.Decode("{\"responseStatus\":200,\"quotaFinished\":true}");},"额度"));
        Check("bad JSON not returned as translation",HasTip(delegate {Translator.Decode("<error>");},"识别"));
        Check("missing response text",HasTip(delegate {Translator.Decode("{\"responseStatus\":200,\"responseData\":{}}");},"识别"));
        Check("service rejection",HasTip(delegate {Translator.Decode("{\"responseStatus\":403}");},"未能"));
        var mock=new Fake();
        using(var api=new Translator(mock))
        {
            string query="C++ & C# / 100% + 好?";
            var translated=api.Translate(query,TextRules.Options[0],CancellationToken.None).GetAwaiter().GetResult();
            var args=System.Web.HttpUtility.ParseQueryString(mock.Last.Query);
            Check("HTTPS GET, encoded text + direction only",mock.Last.Scheme=="https" && mock.Last.Host=="api.mymemory.translated.net" && mock.Last.AbsolutePath=="/get" && args.Count==2 && args["q"]==query && args["langpair"]=="en|zh-CN" && translated==Chinese);
            int before=mock.Count;
            Check("oversize makes zero requests",HasTip(delegate {api.Translate(new string('a',501),TextRules.Options[0],CancellationToken.None).GetAwaiter().GetResult();},"太长") && mock.Count==before);
            bool cancelled=false;
            try{api.Translate(Sample,TextRules.Options[0],new CancellationToken(true)).GetAwaiter().GetResult();} catch(OperationCanceledException){cancelled=true;}
            Check("cancelled request not sent",cancelled && mock.Count==before);
            mock.Status=(HttpStatusCode)429;
            Check("HTTP 429",HasTip(delegate {api.Translate(Sample,TextRules.Options[0],CancellationToken.None).GetAwaiter().GetResult();},"上限"));
            mock.Status=HttpStatusCode.ServiceUnavailable;
            Check("HTTP 503",HasTip(delegate {api.Translate(Sample,TextRules.Options[0],CancellationToken.None).GetAwaiter().GetResult();},"503"));
        }
        var area=new Rectangle(-1920,-120,1920,900); var size=new Size(365,144);
        foreach(var pt in new []{new Point(-1919,-119),new Point(-2,778),new Point(-900,320)})
            Check("popup fits working area at "+pt,area.Contains(new Rectangle(Bubble.Place(pt,area,size,15),size)));
        Console.WriteLine("offline_checks="+passes+"; network=none; clipboard=untouched");
        return 0;
    }

    // A dedicated, invisible sample window only. Never query the user's active application.
    private static int Fixture(bool live)
    {
        Dispatcher dispatcher=null; UWin window=null; TextBox field=null; IntPtr handle=IntPtr.Zero;
        Exception startup=null;
        var ready=new ManualResetEventSlim(false);
        var ui=new Thread(delegate()
        {
            try
            {
                dispatcher=Dispatcher.CurrentDispatcher;
                window=new UWin { Title="HuaYi isolated UIA fixture",Width=450,Height=150,Top=-20000,Left=-20000,
                    ShowInTaskbar=false,ShowActivated=false,Opacity=0,WindowStyle=System.Windows.WindowStyle.None };
                var panel=new StackPanel(); field=new TextBox {Text="outside | "+Sample+" | outside",IsReadOnly=true};
                System.Windows.Automation.AutomationProperties.SetAutomationId(field,"PublicFixture");
                var secret=new PasswordBox();
                System.Windows.Automation.AutomationProperties.SetAutomationId(secret,"ProtectedFixture");
                panel.Children.Add(field); panel.Children.Add(secret); window.Content=panel;
                window.Show(); window.UpdateLayout(); field.Select(10,Sample.Length);
                handle=new WindowInteropHelper(window).Handle; ready.Set(); Dispatcher.Run();
            }
            catch(Exception ex) { startup=ex; ready.Set(); }
        });
        ui.IsBackground=true; ui.SetApartmentState(ApartmentState.STA); ui.Start();
        if(!ready.Wait(8000) || startup!=null || handle==IntPtr.Zero) throw new Exception("fixture failed to open",startup);
        try
        {
            var root=AutomationElement.FromHandle(handle);
            var text=root.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"PublicFixture"));
            var secret=root.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"ProtectedFixture"));
            Check("bounded sample controls found",text!=null && secret!=null);
            Point pt=new Point(-19980,-19985);
            var selection=SelectionReader.ReadBound(handle,text,text,pt,pt,false,CancellationToken.None);
            Check("UIA returns exactly selection, not surrounding text",selection.State==ReadState.Found && selection.Value==Sample);
            Console.WriteLine("uia_selected="+selection.Value);
            var r=((TextPattern)text.GetCurrentPattern(TextPattern.Pattern)).GetSelection()[0].GetBoundingRectangles();
            if(r.Length>0 && !r[0].IsEmpty) pt=new Point((int)(r[0].Left+r[0].Width/2),(int)(r[0].Top+r[0].Height/2));
            var mouse=SelectionReader.ReadBound(handle,text,text,pt,pt,true,CancellationToken.None);
            Check("pointer path reads real selected range",mouse.State==ReadState.Found && mouse.Value==Sample);
            if(live)
            {
                // Explicitly refuse to transmit anything except our public, hard-coded example.
                if(mouse.Value!=Sample) throw new Exception("not the fixed public sample");
                var g=new GestureClassifier();
                var drag=g.Release(handle,new Point(pt.X-60,pt.Y),pt,100,new Size(4,4),new Size(4,4),500);
                Check("recognized drag is permitted in automatic mode",drag.HasValue && TriggerPolicy.Allows(UseMode.Automatic,drag.Value));
                ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
                var d=TextRules.Choose(mouse.Value,-1);
                using(var api=new Translator())
                {
                    string val=api.Translate(mouse.Value,d,CancellationToken.None).GetAwaiter().GetResult();
                    Check("live selection translation is not an echo",!String.IsNullOrWhiteSpace(val) && val!=Sample);
                    dispatcher.Invoke(new Action(delegate {
                        using(var card=new Bubble())
                        {
                            card.ArrangeCard(new Point(35,35),d.Title,val,val);
                            Check("real translation is the bubble body",ContainsLabel(card,val));
                        }
                    }));
                    Console.WriteLine("UIA -> LIVE -> BUBBLE: "+mouse.Value+" => "+val);
                    const string reverse="早上好。";
                    string en=api.Translate(reverse,TextRules.Choose(reverse,-1),CancellationToken.None).GetAwaiter().GetResult();
                    Check("live reverse translation is not an echo",!String.IsNullOrWhiteSpace(en) && en!=reverse);
                    Console.WriteLine("LIVE zh-CN|en: "+reverse+" => "+en);
                }
            }
            var f=new Fake();
            using(var api=new Translator(f))
            {
                if(!TriggerPolicy.Allows(UseMode.Automatic,Trigger.Drag)) throw new Exception("automatic branch missing");
                string val=api.Translate(mouse.Value,TextRules.Choose(mouse.Value,-1),CancellationToken.None).GetAwaiter().GetResult();
                Check("UIA selection -> translation pipeline (mock HTTP)",val==Chinese && f.Count==1);
            }
            var pwd=SelectionReader.ReadBound(handle,text,secret,pt,pt,true,CancellationToken.None);
            Check("protected focus blocks even when pointer is over public text",pwd.State==ReadState.Protected && pwd.Value==null);
            var other=SelectionReader.ReadBound(new IntPtr(-111),text,text,pt,pt,false,CancellationToken.None);
            Check("unrelated window receives no selected text",other.State!=ReadState.Found && other.Value==null);
            dispatcher.Invoke(new Action(delegate { field.Select(0,0); }));
            var empty=SelectionReader.ReadBound(handle,text,text,pt,pt,false,CancellationToken.None);
            Check("insertion caret alone is not a selection",empty.State!=ReadState.Found && empty.Value==null);
            Console.WriteLine("fixture_checks="+passes+"; source=own_invisible_WPF_window; network="+(live ? "fixed_samples_only" : "none"));
            return 0;
        }
        finally
        {
            dispatcher.Invoke(new Action(delegate { window.Close(); dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal); }));
            ui.Join(3000); ready.Dispose();
        }
    }
    private static bool ContainsLabel(System.Windows.Forms.Control parent,string text)
    {
        foreach(System.Windows.Forms.Control child in parent.Controls)
            if((child is System.Windows.Forms.Label && child.Text==text) || ContainsLabel(child,text)) return true;
        return false;
    }
    private static void RealizeChildren(System.Windows.Forms.Control parent)
    {
        var h=parent.Handle; parent.PerformLayout();
        foreach(System.Windows.Forms.Control child in parent.Controls) RealizeChildren(child);
    }
    private static int Views(string destination)
    {
        Exception fault=null;
        var ui=new Thread(delegate()
        {
            try
            {
                System.Windows.Forms.Application.EnableVisualStyles();
                System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                Directory.CreateDirectory(destination);
                using(var icon=Icon.ExtractAssociatedIcon(typeof(Bubble).Assembly.Location))
                using(var welcome=new Welcome(icon))
                using(var card=new Bubble())
                {
                    Check("consent required on launch",welcome.Choice==UseMode.Paused);
                    RealizeChildren(welcome);
                    using(var image=new Bitmap(welcome.Width,welcome.Height))
                    { welcome.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size)); image.Save(Path.Combine(destination,"启动界面.png")); }
                    card.ArrangeCard(new Point(30,30),"英语 → 简体中文","早上好。","早上好。");
                    RealizeChildren(card);
                    using(var image=new Bitmap(card.Width,card.Height))
                    { card.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size)); image.Save(Path.Combine(destination,"译文浮窗.png")); }
                    Check("popup is topmost without a taskbar button",card.TopMost && !card.ShowInTaskbar);
                    Check("fixture never displayed windows",!card.Visible && !welcome.Visible);
                }
            }
            catch(Exception ex) { fault=ex; }
        });
        ui.SetApartmentState(ApartmentState.STA); ui.Start(); ui.Join();
        if(fault!=null) throw new Exception("off-screen render failed",fault);
        Console.WriteLine("rendered=2; disk=process_previews_only");
        return 0;
    }
    private static void ClickFixtureButton(System.Windows.Forms.Button button)
    {
        if(!button.Enabled) throw new Exception("disabled test button: "+button.Name);
        // Raise this fixture's own Click without displaying anything or injecting mouse input.
        typeof(System.Windows.Forms.Button).GetMethod("OnClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
            .Invoke(button,new object[]{EventArgs.Empty});
    }
    private static int PanelFixture(string folder,bool live)
    {
        Exception problem=null;
        var ui=new Thread(delegate()
        {
            try
            {
                System.Windows.Forms.Application.EnableVisualStyles();
                System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                var fake=new Fake();
                ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
                using(var icon=Icon.ExtractAssociatedIcon(typeof(TrayApp).Assembly.Location))
                using(var app=new TrayApp(icon,live ? new Translator() : new Translator(fake),false))
                {
                    var desk=app.Desk; RealizeChildren(desk);
                    Func<string,System.Windows.Forms.Button> button=delegate(string key){return (System.Windows.Forms.Button)desk.Controls.Find(key,true)[0];};
                    var state=desk.Controls.Find("modeStatus",true)[0]; var result=desk.Controls.Find("sampleResult",true)[0];
                    Check("taskbar main form attached at startup",app.MainForm==desk && desk.ShowInTaskbar && desk.MinimizeBox && desk.Text==ControlDesk.Caption);
                    Check("launch is paused; no sample request",state.Text.Contains("已暂停") && !button("trySample").Enabled && fake.Count==0);
                    Directory.CreateDirectory(folder);
                    using(var bmp=new Bitmap(desk.Width,desk.Height)){desk.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save(Path.Combine(folder,"控制窗口.png"));}
                    ClickFixtureButton(button("manual")); Check("manual mode visible in the panel",state.Text=="仅手动翻译" && button("trySample").Enabled);
                    ClickFixtureButton(button("trySample"));
                    var sw=System.Diagnostics.Stopwatch.StartNew();
                    while(result.Text.StartsWith("正在") && sw.ElapsedMilliseconds<12500){System.Windows.Forms.Application.DoEvents();Thread.Sleep(20);}
                    Check("sample response shown inline",result.Text.StartsWith("示例译文：") && result.Text.Length>5);
                    if(!live) Check("one request only, fixed public sample",fake.Count==1 && System.Web.HttpUtility.ParseQueryString(fake.Last.Query)["q"]==ControlDesk.Sample);
                    Console.WriteLine("panel_sample="+result.Text+"; transport="+(live?"live":"fixture"));
                    ClickFixtureButton(button("automatic"));
                    Check("automatic state remains available without tray",app.MainForm==desk && state.Text=="自动翻译已开启" && button("pause").Enabled);
                    ClickFixtureButton(button("pause"));
                    Check("pause disables translation from the panel",state.Text.Contains("已暂停") && !button("trySample").Enabled && !button("copiedText").Enabled);
                    Check("fixture did not display UI or register global capture",!desk.Visible);
                }
            } catch(Exception ex) {problem=ex;}
        });
        ui.SetApartmentState(ApartmentState.STA); ui.Start(); ui.Join();
        if(problem!=null)throw new Exception("panel fixture failed",problem);return 0;
    }
    private static int Live()
    {
        ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
        var fixedSamples=new[]{Sample,"早上好。"};
        using(var api=new Translator()) foreach(string s in fixedSamples)
        {
            var d=TextRules.Choose(s,-1);
            string value=api.Translate(s,d,CancellationToken.None).GetAwaiter().GetResult();
            if(String.IsNullOrWhiteSpace(value) || value==s) throw new Exception("no translation for public example");
            Console.WriteLine("LIVE "+d.Pair+": "+s+" => "+value);
        }
        return 0;
    }
    [MTAThread] private static int Main(string[] args)
    {
        Console.OutputEncoding=new UTF8Encoding(false);
        try
        {
            if(args.Length==2 && (args[0]=="panel" || args[0]=="panel-live")) return PanelFixture(args[1],args[0]=="panel-live");
            if(args.Length==2 && args[0]=="views") return Views(args[1]);
            if(args.Length!=1) return 2;
            switch(args[0]) {case "route":return Route(); case "offline":return Offline(); case "uia":return Fixture(false); case "e2e":return Fixture(true); case "live":return Live();}
            return 2;
        }
        catch(Exception ex) {Console.Error.WriteLine(ex.ToString());return 1;}
    }
}
