using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HuaYi
{
    internal static class Style
    {
        internal static readonly Color Ink=Color.FromArgb(28,49,52);
        internal static readonly Color Teal=Color.FromArgb(0,111,105);
        internal static readonly Color Muted=Color.FromArgb(72,97,99);
        internal static Font FontAt(float pt, bool bold)
        { return new Font("Microsoft YaHei UI",pt,bold ? FontStyle.Bold : FontStyle.Regular,GraphicsUnit.Point); }
        internal static Label LabelAt(Control parent,string text,int x,int y,int w,int h,float size,bool bold,Color color)
        {
            var v=new Label { Text=text, AutoSize=false, UseMnemonic=false, ForeColor=color, BackColor=Color.Transparent,
                Font=FontAt(size,bold), Bounds=new Rectangle(x,y,w,h), TextAlign=ContentAlignment.MiddleLeft };
            parent.Controls.Add(v); return v;
        }
        internal static Button ButtonAt(Control parent,string text,int x,int y,int w,int h,bool primary)
        {
            var v=new Button { Text=text, Bounds=new Rectangle(x,y,w,h), FlatStyle=FlatStyle.Flat,
                BackColor=primary ? Teal : Color.White, ForeColor=primary ? Color.White : Ink,
                Font=FontAt(10,true), Cursor=Cursors.Hand, UseVisualStyleBackColor=false };
            v.FlatAppearance.BorderColor=primary ? Teal : Color.FromArgb(182,200,198);
            parent.Controls.Add(v); return v;
        }
    }

    internal sealed class Welcome : Form
    {
        internal UseMode Choice=UseMode.Paused;
        internal Welcome(Icon icon)
        {
            Text="划译 · Windows 划词翻译"; Icon=icon; Font=Style.FontAt(10,false);
            AutoScaleDimensions=new SizeF(96,96); AutoScaleMode=AutoScaleMode.Dpi;
            ClientSize=new Size(474,351); FormBorderStyle=FormBorderStyle.FixedDialog;
            MaximizeBox=false; MinimizeBox=false; StartPosition=FormStartPosition.CenterScreen;
            BackColor=Color.White;
            Controls.Add(new Panel { Bounds=new Rectangle(0,0,474,5),BackColor=Style.Teal });
            Style.LabelAt(this,"划 译    /    WINDOWS",24,19,420,22,9,true,Style.Teal);
            Style.LabelAt(this,"划一下，就能看译文。",22,43,438,49,22,true,Style.Ink);
            Style.LabelAt(this,"用鼠标拖选一句话，或双击一个词。译文会出现在光标旁，\n原文和输入法都保持不变。",24,98,431,49,10,false,Style.Muted);
            var panel=new Panel { Bounds=new Rectangle(24,158,426,97),BackColor=Color.FromArgb(234,244,241) };
            Controls.Add(panel);
            Style.LabelAt(panel,"开启前，请留意联网范围",12,7,401,23,10,true,Style.Teal);
            Style.LabelAt(panel,"自动模式会把你在支持的软件中划选的文字发给 MyMemory。服务方可能长期保存提交的内容。处理私密资料时，请用手动模式或暂停。",12,31,401,60,9,false,Style.Ink);
            var go=Style.ButtonAt(this,"开启自动翻译",24,270,196,41,true);
            var manual=Style.ButtonAt(this,"仅手动翻译",230,270,220,41,false);
            go.Click+=delegate { Choice=UseMode.Automatic; DialogResult=DialogResult.OK; };
            manual.Click+=delegate { Choice=UseMode.Manual; DialogResult=DialogResult.OK; };
            Style.LabelAt(this,"手动翻译：选中后按 Ctrl + Alt + T。关闭此窗口则不运行。",24,316,430,25,9,false,Style.Muted);
            ActiveControl=manual;
        }
    }

    internal sealed class Bubble : Form
    {
        private readonly Label brand,heading,body,foot;
        private readonly Panel strip,scroll;
        private readonly Button copy,close;
        private string value;
        internal DateTime Expires;
        internal Bubble()
        {
            Text="划译 · 译文"; ShowInTaskbar=false; TopMost=true; FormBorderStyle=FormBorderStyle.None;
            StartPosition=FormStartPosition.Manual; AutoScaleMode=AutoScaleMode.None;
            BackColor=Color.White; DoubleBuffered=true; Padding=new Padding(1);
            strip=new Panel { BackColor=Style.Teal }; Controls.Add(strip);
            brand=Style.LabelAt(this,"划译",0,0,40,24,10,true,Style.Teal);
            heading=Style.LabelAt(this,"",0,0,190,24,9,false,Style.Muted);
            close=Style.ButtonAt(this,"×",0,0,30,26,false); close.FlatAppearance.BorderSize=0;
            close.TabStop=false; close.AccessibleName="关闭浮窗"; close.Click+=delegate { Dismiss(); };
            scroll=new Panel { AutoScroll=true,BackColor=Color.White }; Controls.Add(scroll);
            body=Style.LabelAt(scroll,"",0,0,300,40,12,false,Style.Ink);
            body.TextAlign=ContentAlignment.TopLeft;
            foot=Style.LabelAt(this,"",0,0,180,24,8,false,Style.Muted);
            copy=Style.ButtonAt(this,"复制译文",0,0,84,28,false); copy.Font=Style.FontAt(9,false);
            copy.TabStop=false; copy.Click+=delegate {
                if (String.IsNullOrEmpty(value)) return;
                try { Clipboard.SetText(value,TextDataFormat.UnicodeText); copy.Text="已复制"; }
                catch (ExternalException) { copy.Text="请重试"; }
                Expires=DateTime.UtcNow.AddSeconds(12);
            };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        { get { var cp=base.CreateParams; cp.ExStyle|=0x08000000|0x80; return cp; } } // NOACTIVATE | TOOLWINDOW
        protected override void WndProc(ref Message m)
        {
            if (m.Msg==0x21) { m.Result=(IntPtr)3; return; } // MA_NOACTIVATE
            base.WndProc(ref m);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            ControlPaint.DrawBorder(e.Graphics,ClientRectangle,Color.FromArgb(144,175,171),ButtonBorderStyle.Solid);
        }
        internal static Point Place(Point p,Rectangle area, Size sz, int gap)
        {
            int x=p.X+gap,y=p.Y+gap;
            if (x+sz.Width>area.Right) x=p.X-sz.Width-gap;
            if (y+sz.Height>area.Bottom) y=p.Y-sz.Height-gap;
            return new Point(Math.Max(area.Left,Math.Min(x,area.Right-sz.Width)),
                             Math.Max(area.Top,Math.Min(y,area.Bottom-sz.Height)));
        }
        internal void Present(Point p,string title,string message,string translation)
        {
            ArrangeCard(p,title,message,translation);
            if (!Visible) Show();
            Invalidate();
        }
        internal void ArrangeCard(Point p,string title,string message,string translation)
        {
            value=translation;
            var area=Screen.FromPoint(p).WorkingArea;
            // Position first so device metrics follow the target monitor when possible.
            if (!Visible) Location=new Point(Math.Max(area.Left,p.X),Math.Max(area.Top,p.Y));
            float scale;
            using (var g=CreateGraphics()) scale=Math.Max(1f,g.DpiX/96f);
            Func<int,int> s=delegate(int v) {return (int)Math.Round(v*scale);};
            int w=Math.Min(s(365),area.Width),margin=s(15);
            int textW=Math.Max(s(80),w-2*margin-SystemInformation.VerticalScrollBarWidth-s(3));
            var flags=TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix|TextFormatFlags.TextBoxControl;
            int needed=TextRenderer.MeasureText(message,body.Font,new Size(textW,Int32.MaxValue),flags).Height+s(5);
            int viewH=Math.Min(Math.Max(s(35),needed), Math.Max(s(42),Math.Min(s(218),area.Height/2)));
            int top=s(43),footer=top+viewH+s(6),h=footer+s(39);
            SuspendLayout();
            ClientSize=new Size(w,h);
            strip.Bounds=new Rectangle(1,1,w-2,s(3));
            brand.Bounds=new Rectangle(margin,s(9),s(42),s(26));
            heading.Bounds=new Rectangle(margin+s(47),s(10),w-s(121),s(24)); heading.Text=title;
            close.Bounds=new Rectangle(w-s(36),s(6),s(30),s(30));
            scroll.Bounds=new Rectangle(margin,top,w-2*margin,viewH); scroll.AutoScrollPosition=Point.Empty;
            body.SetBounds(0,0,textW,Math.Max(needed,viewH)); body.Text=message;
            foot.Bounds=new Rectangle(margin,footer,w-s(125),s(29));
            foot.Text=translation==null ? "划译  /  选区翻译" : "MyMemory  ·  保留原文";
            copy.Bounds=new Rectangle(w-margin-s(78),footer,s(78),s(29));
            copy.Text="复制译文"; copy.Visible=translation!=null;
            Location=Place(p,area,ClientSize,s(15));
            ResumeLayout(true);
            Expires=DateTime.UtcNow.AddSeconds(15);
        }
        internal void Dismiss()
        {
            if (Visible) Hide();
            value=null; body.Text=String.Empty;
        }
    }
}
