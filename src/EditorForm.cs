using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.IO;
using System.Windows.Forms;

namespace MongCapture
{
    enum Tool { Pen, Highlighter, Rectangle, Ellipse, Line, Arrow, Text, Number, Mosaic, Crop }

    // 캡처 편집기. 메뉴(파일·편집·보기·도구·도움말)에 있는 명령은 도구 모음에 다시 두지 않는다 (공통 규칙 9).
    // 도구 모음에는 그리기 도구, 굵기, 색만 둔다.
    sealed class EditorForm : Form, IMessageFilter
    {
        const int MaxUndo = 30;
        static readonly float[] ZoomSteps = { 0.1f, 0.25f, 0.5f, 0.75f, 1f, 1.5f, 2f, 3f, 4f, 6f, 8f };

        Bitmap image;
        string savedPath;               // 마지막으로 저장한 파일 (없으면 null)
        bool dirty;                     // 저장하지 않은 변경이 있음
        float zoom = 1f;
        Tool tool;
        Color penColor;
        int penWidth;

        readonly List<Bitmap> undo = new List<Bitmap>();
        readonly List<Bitmap> redo = new List<Bitmap>();

        // 그리는 중인 도형 (이미지 좌표)
        bool drawing;
        PointF startPt, curPt;
        readonly List<PointF> strokePts = new List<PointF>();
        bool hasCrop;
        Rectangle cropRect;
        TextBox textBox;
        PointF textPt;
        int numberCounter = 1;          // 번호 도장의 다음 번호

        MenuStrip menu;
        ToolStrip toolbar;
        StatusStrip status;
        ToolStripStatusLabel stInfo, stZoom, stVer;
        Panel scroller;
        Canvas canvas;
        ToolStripButton colorButton;
        ToolStripComboBox widthBox;
        Dictionary<Tool, ToolStripButton> toolButtons;
        ToolStripMenuItem miUndo, miRedo, miApplyCrop, miCancelCrop;

        public EditorForm(Bitmap bmp, string path)
        {
            image = bmp;
            savedPath = path;
            dirty = path == null;
            penColor = Color.FromArgb(Settings.GetInt("PenColor", Color.FromArgb(255, 64, 64).ToArgb()));
            penWidth = Math.Max(1, Math.Min(12, Settings.GetInt("PenWidth", 3)));
            tool = ParseTool(Settings.Get("Tool", "Pen"));

            KeyPreview = true;
            Font = SystemFonts.MessageBoxFont;
            Icon = Util.AppIcon;
            BuildUi();
            MinimumSize = new Size(820, 360);
            UpdateTitle();
            UpdateMenus();
            UpdateInfo();

            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            Rectangle saved = Settings.GetRect("EditorBounds");
            if (Program.HasPos)
            {
                StartPosition = FormStartPosition.Manual;
                Size = new Size(Math.Min(work.Width, Math.Max(860, image.Width + 40)), Math.Min((int)(work.Height * 0.6), image.Height + 180));
                Location = Program.Pos;
            }
            else if (!saved.IsEmpty && OnScreen(saved))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = saved;
            }
            else
            {
                StartPosition = FormStartPosition.CenterScreen;
                Size = new Size(Math.Min((int)(work.Width * 0.9), Math.Max(860, image.Width + 40)), Math.Min((int)(work.Height * 0.9), image.Height + 180));
            }
            if (Settings.GetBool("EditorMaximized", false)) WindowState = FormWindowState.Maximized;

            Move += delegate { SaveBounds(); };
            Resize += delegate { SaveBounds(); };
            FormClosing += OnFormClosing;
            FormClosed += delegate { Application.RemoveMessageFilter(this); };
            Application.AddMessageFilter(this);
        }

        // ───────── 화면 구성 ─────────

        void BuildUi()
        {
            Text = "몽캡쳐 편집기";
            scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(60, 60, 60) };
            canvas = new Canvas(this);
            scroller.Controls.Add(canvas);

            stInfo = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            stZoom = new ToolStripStatusLabel { Width = 60 };
            stVer = new ToolStripStatusLabel(Util.VersionText);
            status = new StatusStrip();
            status.Items.AddRange(new ToolStripItem[] { stInfo, stZoom, stVer });

            toolbar = BuildToolbar();
            menu = BuildMenu();
            MainMenuStrip = menu;

            // 도킹 순서: 본문(Fill) → 하단 → 도구 모음 → 메뉴 (맨 위가 메뉴)
            Controls.Add(scroller);
            Controls.Add(status);
            Controls.Add(toolbar);
            Controls.Add(menu);
            ResizeCanvas();
        }

        ToolStrip BuildToolbar()
        {
            ToolStrip ts = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
            toolButtons = new Dictionary<Tool, ToolStripButton>();
            foreach (Tool t in (Tool[])Enum.GetValues(typeof(Tool)))
            {
                Tool captured = t;
                // 단추에는 짧은 이름만 쓰고, 단축키는 말풍선(툴팁)으로 알려 준다 (좁은 창에서도 굵기·색이 보이게)
                string full = ToolName(t);
                ToolStripButton b = new ToolStripButton(full.Substring(0, full.IndexOf(" ("))) { DisplayStyle = ToolStripItemDisplayStyle.Text, Tag = t, ToolTipText = full };
                b.Click += delegate { SetTool(captured); };
                toolButtons[t] = b;
                ts.Items.Add(b);
            }

            ts.Items.Add(new ToolStripSeparator());
            ts.Items.Add(new ToolStripLabel("굵기"));
            widthBox = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 56 };
            for (int i = 1; i <= 12; i++) widthBox.Items.Add(i);
            widthBox.SelectedItem = penWidth;
            widthBox.SelectedIndexChanged += delegate
            {
                if (!(widthBox.SelectedItem is int)) return;
                penWidth = (int)widthBox.SelectedItem;
                Settings.SetInt("PenWidth", penWidth);
            };
            ts.Items.Add(widthBox);

            colorButton = new ToolStripButton("색", Swatch(penColor)) { DisplayStyle = ToolStripItemDisplayStyle.ImageAndText };
            colorButton.Click += delegate { PickColor(); };
            ts.Items.Add(colorButton);

            // 이 그림을 탐색기·메신저·문서 등으로 끌어다 놓는다 (메뉴에 없는 기능)
            ToolStripLabel dragOut = new ToolStripLabel("끌어서 내보내기")
            {
                Alignment = ToolStripItemAlignment.Right,
                ToolTipText = "이 글자를 누른 채 끌어서 탐색기나 다른 프로그램에 놓으면 그림이 파일로 들어갑니다",
                ForeColor = Color.FromArgb(0, 102, 204),
            };
            dragOut.MouseDown += delegate(object sender, MouseEventArgs ev)
            {
                if (ev.Button == MouseButtons.Left) DoDragDrop(BuildDragData(), DragDropEffects.Copy);
            };
            ts.Items.Add(dragOut);
            return ts;
        }

        MenuStrip BuildMenu()
        {
            MenuStrip ms = new MenuStrip();

            ToolStripMenuItem file = new ToolStripMenuItem("파일(&F)");
            file.DropDownItems.Add(Item("저장(&S)", Keys.Control | Keys.S, delegate { Save(); }));
            file.DropDownItems.Add(Item("다른 이름으로 저장(&A)...", Keys.Control | Keys.Shift | Keys.S, delegate { SaveAs(); }));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("인쇄(&P)...", Keys.Control | Keys.P, delegate { Print(); }));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("닫기(&C)", Keys.Control | Keys.W, delegate { Close(); }));

            ToolStripMenuItem edit = new ToolStripMenuItem("편집(&E)");
            miUndo = Item("실행 취소(&U)", Keys.Control | Keys.Z, delegate { Undo(); });
            miRedo = Item("다시 실행(&R)", Keys.Control | Keys.Y, delegate { Redo(); });
            edit.DropDownItems.Add(miUndo);
            edit.DropDownItems.Add(miRedo);
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item("클립보드로 복사(&C)", Keys.Control | Keys.C, delegate { CopyImage(); }));

            ToolStripMenuItem view = new ToolStripMenuItem("보기(&V)");
            view.DropDownItems.Add(Item("확대(&I)", Keys.Control | Keys.Oemplus, delegate { ZoomStep(true); }));
            view.DropDownItems.Add(Item("축소(&O)", Keys.Control | Keys.OemMinus, delegate { ZoomStep(false); }));
            view.DropDownItems.Add(Item("실제 크기(&N)", Keys.Control | Keys.D0, delegate { SetZoom(1f); }));
            view.DropDownItems.Add(Item("창에 맞춤(&F)", Keys.Control | Keys.D9, delegate { ZoomFit(); }));

            ToolStripMenuItem tools = new ToolStripMenuItem("도구(&T)");
            miApplyCrop = Item("자르기 적용 (Enter)", Keys.None, delegate { ApplyCrop(); });
            miCancelCrop = Item("자르기 취소 (Esc)", Keys.None, delegate { CancelCrop(); });
            tools.DropDownItems.Add(miApplyCrop);
            tools.DropDownItems.Add(miCancelCrop);

            ToolStripMenuItem help = new ToolStripMenuItem("도움말(&H)");
            help.DropDownItems.Add(Item("단축키 목록 (F1)", Keys.F1, delegate { ShowShortcuts(); }));
            help.DropDownItems.Add(Item("업데이트 확인(&U)", Keys.None, delegate { MongUpdate.UpdateUi.CheckNow(this, "mongcapture", "몽캡쳐", Util.VersionNumber, null); }));
            help.DropDownItems.Add(new ToolStripSeparator());
            help.DropDownItems.Add(Item("버전 정보(&A)", Keys.None, delegate { ShowAbout(); }));

            ms.Items.AddRange(new ToolStripItem[] { file, edit, view, tools, help });
            return ms;
        }

        static ToolStripMenuItem Item(string text, Keys keys, EventHandler handler)
        {
            ToolStripMenuItem mi = new ToolStripMenuItem(text, null, handler);
            if (keys != Keys.None) mi.ShortcutKeys = keys;
            return mi;
        }

        static string ToolName(Tool t)
        {
            switch (t)
            {
                case Tool.Pen: return "펜 (P)";
                case Tool.Highlighter: return "형광펜 (H)";
                case Tool.Rectangle: return "사각형 (R)";
                case Tool.Ellipse: return "타원 (O)";
                case Tool.Line: return "직선 (L)";
                case Tool.Arrow: return "화살표 (A)";
                case Tool.Text: return "텍스트 (T)";
                case Tool.Number: return "번호 (N)";
                case Tool.Mosaic: return "모자이크 (M)";
                default: return "자르기 (C)";
            }
        }

        static Tool ParseTool(string s)
        {
            Tool t;
            return Enum.TryParse<Tool>(s, out t) ? t : Tool.Pen;
        }

        static Bitmap Swatch(Color c)
        {
            Bitmap b = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(c);
                g.DrawRectangle(Pens.Black, 0, 0, 15, 15);
            }
            return b;
        }

        void UpdateTitle()
        {
            string name = savedPath != null ? Path.GetFileName(savedPath) : "새 캡처";
            Text = "몽캡쳐 편집기 - " + name + (dirty ? " *" : "");
        }

        void UpdateMenus()
        {
            miUndo.Enabled = undo.Count > 0;
            miRedo.Enabled = redo.Count > 0;
            miApplyCrop.Enabled = hasCrop;
            miCancelCrop.Enabled = hasCrop;
        }

        void UpdateInfo()
        {
            string text = image.Width + " x " + image.Height + " px   |   도구: " + ToolName(tool);
            if (hasCrop) text += "   |   Enter: 자르기 적용, Esc: 취소";
            if (tool == Tool.Number) text += "   |   클릭: 번호 찍기 (다음 " + numberCounter + "), 오른쪽 클릭: 1부터 다시";
            stInfo.Text = text;
            stZoom.Text = Math.Round(zoom * 100) + "%";
            if (toolButtons != null)
            {
                foreach (KeyValuePair<Tool, ToolStripButton> kv in toolButtons) kv.Value.Checked = kv.Key == tool;
            }
        }

        void SetStatus(string message)
        {
            stInfo.Text = message;
        }

        void SaveBounds()
        {
            if (!IsHandleCreated || !Visible) return;
            if (WindowState == FormWindowState.Normal) Settings.SetRect("EditorBounds", Bounds);
            Settings.SetBool("EditorMaximized", WindowState == FormWindowState.Maximized);
        }

        static bool OnScreen(Rectangle r)
        {
            foreach (Screen s in Screen.AllScreens)
            {
                if (s.WorkingArea.IntersectsWith(r)) return true;
            }
            return false;
        }

        // ───────── 보기(확대·축소) ─────────

        void ResizeCanvas()
        {
            int w = Math.Max(1, (int)Math.Round(image.Width * zoom));
            int h = Math.Max(1, (int)Math.Round(image.Height * zoom));
            canvas.Location = new Point(10, 10);
            canvas.Size = new Size(w, h);
            scroller.AutoScrollMinSize = new Size(w + 20, h + 20);
        }

        void SetZoom(float z)
        {
            zoom = Math.Max(ZoomSteps[0], Math.Min(ZoomSteps[ZoomSteps.Length - 1], z));
            ResizeCanvas();
            canvas.Invalidate();
            UpdateInfo();
        }

        void ZoomStep(bool zoomIn)
        {
            float target = zoom;
            if (zoomIn)
            {
                foreach (float z in ZoomSteps)
                {
                    if (z > zoom + 0.001f) { target = z; break; }
                }
            }
            else
            {
                for (int i = ZoomSteps.Length - 1; i >= 0; i--)
                {
                    if (ZoomSteps[i] < zoom - 0.001f) { target = ZoomSteps[i]; break; }
                }
            }
            SetZoom(target);
        }

        void ZoomFit()
        {
            float zw = (float)(scroller.ClientSize.Width - 24) / image.Width;
            float zh = (float)(scroller.ClientSize.Height - 24) / image.Height;
            SetZoom(Math.Min(zw, zh));
        }

        // Ctrl + 마우스 휠로 확대·축소 (휠 메시지는 포커스와 상관없이 편집기 본문 위에서 받는다)
        public bool PreFilterMessage(ref Message m)
        {
            const int WM_MOUSEWHEEL = 0x020A;
            if (m.Msg != WM_MOUSEWHEEL || !Visible || Control.ModifierKeys != Keys.Control) return false;
            if (!scroller.ClientRectangle.Contains(scroller.PointToClient(Cursor.Position))) return false;
            int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
            ZoomStep(delta > 0);
            return true;
        }

        // ───────── 도구 ─────────

        void SetTool(Tool t)
        {
            CancelText();
            if (t != tool) hasCrop = false;
            tool = t;
            Settings.Set("Tool", t.ToString());
            canvas.Cursor = t == Tool.Text ? Cursors.IBeam : Cursors.Cross;
            UpdateMenus();
            UpdateInfo();
            canvas.Invalidate();
        }

        void PickColor()
        {
            using (ColorDialog dlg = new ColorDialog { Color = penColor, FullOpen = true })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                penColor = dlg.Color;
                Settings.SetInt("PenColor", penColor.ToArgb());
                colorButton.Image = Swatch(penColor);
            }
        }

        int TextSize()
        {
            return penWidth * 4 + 6;
        }

        // 이미지 좌표 → 화면(캔버스) 좌표
        PointF ImagePoint(Point p)
        {
            float x = Math.Max(0, Math.Min(image.Width, p.X / zoom));
            float y = Math.Max(0, Math.Min(image.Height, p.Y / zoom));
            return new PointF(x, y);
        }

        static Rectangle RectOf(PointF a, PointF b)
        {
            return Rectangle.FromLTRB((int)Math.Min(a.X, b.X), (int)Math.Min(a.Y, b.Y), (int)Math.Ceiling(Math.Max(a.X, b.X)), (int)Math.Ceiling(Math.Max(a.Y, b.Y)));
        }

        static RectangleF RectFOf(PointF a, PointF b)
        {
            return RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        // 도형 그리기 (이미지 좌표). 미리보기와 확정에 같이 쓴다.
        static void DrawShape(Graphics g, Tool t, PointF a, PointF b, List<PointF> pts, Color color, float w)
        {
            switch (t)
            {
                case Tool.Pen:
                case Tool.Highlighter:
                    {
                        float width = t == Tool.Highlighter ? w * 4 : w;
                        Color c = t == Tool.Highlighter ? Color.FromArgb(90, color) : color;
                        using (Pen pen = new Pen(c, width))
                        {
                            pen.StartCap = LineCap.Round;
                            pen.EndCap = LineCap.Round;
                            pen.LineJoin = LineJoin.Round;
                            if (pts.Count >= 2)
                            {
                                g.DrawLines(pen, pts.ToArray());
                            }
                            else
                            {
                                using (SolidBrush br = new SolidBrush(c))
                                    g.FillEllipse(br, a.X - width / 2, a.Y - width / 2, width, width);
                            }
                        }
                        break;
                    }
                case Tool.Rectangle:
                    using (Pen pen = new Pen(color, w))
                    {
                        RectangleF r = RectFOf(a, b);
                        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                    }
                    break;
                case Tool.Ellipse:
                    using (Pen pen = new Pen(color, w))
                    {
                        RectangleF r = RectFOf(a, b);
                        g.DrawEllipse(pen, r.X, r.Y, r.Width, r.Height);
                    }
                    break;
                case Tool.Line:
                    using (Pen pen = new Pen(color, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawLine(pen, a, b);
                    break;
                case Tool.Arrow:
                    using (Pen pen = new Pen(color, w) { StartCap = LineCap.Round })
                    {
                        pen.CustomEndCap = new AdjustableArrowCap(3f, 4f, true);
                        g.DrawLine(pen, a, b);
                    }
                    break;
            }
        }

        // 모자이크: 영역을 블록 단위로 뭉갠다
        void ApplyMosaic(Rectangle r)
        {
            r = Rectangle.Intersect(r, new Rectangle(0, 0, image.Width, image.Height));
            if (r.Width < 2 || r.Height < 2) return;
            int block = Math.Max(4, penWidth * 4);
            int sw = Math.Max(1, r.Width / block), sh = Math.Max(1, r.Height / block);
            using (Bitmap small = new Bitmap(sw, sh, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (Graphics gs = Graphics.FromImage(small))
                {
                    gs.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    gs.DrawImage(image, new Rectangle(0, 0, sw, sh), r, GraphicsUnit.Pixel);
                }
                using (Graphics g = Graphics.FromImage(image))
                {
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(small, r);
                }
            }
        }

        void FinishShape()
        {
            if (tool == Tool.Crop)
            {
                cropRect = RectOf(startPt, curPt);
                hasCrop = cropRect.Width > 1 && cropRect.Height > 1;
                UpdateMenus();
                UpdateInfo();
                return;
            }
            if (tool != Tool.Pen && tool != Tool.Highlighter && tool != Tool.Text
                && Math.Abs(curPt.X - startPt.X) < 1 && Math.Abs(curPt.Y - startPt.Y) < 1)
                return;

            PushUndo();
            if (tool == Tool.Mosaic)
            {
                ApplyMosaic(RectOf(startPt, curPt));
            }
            else
            {
                using (Graphics g = Graphics.FromImage(image))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    DrawShape(g, tool, startPt, curPt, strokePts, penColor, penWidth);
                }
            }
            dirty = true;
            UpdateTitle();
            UpdateMenus();
        }

        // ───────── 번호 도장 · 끌어서 내보내기 ─────────

        // 번호 도장: 펜 색 동그라미 안에 흰 숫자. 클릭할 때마다 1, 2, 3... 늘어난다.
        void StampNumber(PointF p)
        {
            PushUndo();
            int radius = 10 + penWidth * 2;
            using (Graphics g = Graphics.FromImage(image))
            using (SolidBrush fill = new SolidBrush(penColor))
            using (Font f = new Font(SystemFonts.MessageBoxFont.FontFamily, radius * 1.15f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.FillEllipse(fill, p.X - radius, p.Y - radius, radius * 2, radius * 2);
                using (Pen ring = new Pen(Color.White, 2))
                    g.DrawEllipse(ring, p.X - radius, p.Y - radius, radius * 2, radius * 2);
                g.DrawString(numberCounter.ToString(), f, Brushes.White, new RectangleF(p.X - radius, p.Y - radius, radius * 2, radius * 2), center);
            }
            numberCounter++;
            dirty = true;
            UpdateTitle();
            UpdateMenus();
            UpdateInfo();
            canvas.Invalidate();
        }

        // 이 그림을 끌어가기용 데이터로 만든다 (임시 폴더에 PNG 로 저장해 파일과 그림을 함께 담는다)
        DataObject BuildDragData()
        {
            string dir = Path.Combine(Path.GetTempPath(), "MongCapture");
            Directory.CreateDirectory(dir);
            string name = savedPath != null ? Path.GetFileNameWithoutExtension(savedPath) : "캡쳐_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = Path.Combine(dir, name + ".png");
            image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            DataObject data = new DataObject();
            System.Collections.Specialized.StringCollection files = new System.Collections.Specialized.StringCollection();
            files.Add(path);
            data.SetFileDropList(files);
            data.SetImage(image);
            return data;
        }

        // ───────── 글자 넣기 ─────────

        void BeginText(PointF p)
        {
            textPt = p;
            textBox = new TextBox
            {
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                ForeColor = penColor,
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, Math.Max(6, TextSize() * zoom), GraphicsUnit.Pixel),
                Location = new Point((int)(p.X * zoom), (int)(p.Y * zoom)),
                Width = 240,
            };
            textBox.LostFocus += delegate { CommitText(); };
            canvas.Controls.Add(textBox);
            textBox.Focus();
        }

        void CommitText()
        {
            TextBox tb = textBox;
            if (tb == null) return;
            textBox = null;
            string text = tb.Text;
            canvas.Controls.Remove(tb);
            if (text.Length == 0)
            {
                canvas.Focus();
                return;
            }

            PushUndo();
            using (Font f = new Font(SystemFonts.MessageBoxFont.FontFamily, TextSize(), FontStyle.Bold, GraphicsUnit.Pixel))
            using (Graphics g = Graphics.FromImage(image))
            using (SolidBrush b = new SolidBrush(penColor))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.DrawString(text, f, b, textPt);
            }
            dirty = true;
            UpdateTitle();
            UpdateMenus();
            canvas.Focus();
            canvas.Invalidate();
        }

        void CancelText()
        {
            TextBox tb = textBox;
            if (tb == null) return;
            textBox = null;
            canvas.Controls.Remove(tb);
            canvas.Focus();
        }

        // ───────── 자르기 ─────────

        void ApplyCrop()
        {
            if (!hasCrop) return;
            Rectangle r = Rectangle.Intersect(cropRect, new Rectangle(0, 0, image.Width, image.Height));
            if (r.Width < 1 || r.Height < 1) return;
            PushUndo();
            image = image.Clone(r, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            hasCrop = false;
            AfterImageChanged();
            dirty = true;
            UpdateTitle();
        }

        void CancelCrop()
        {
            if (!hasCrop) return;
            hasCrop = false;
            UpdateMenus();
            UpdateInfo();
            canvas.Invalidate();
        }

        // ───────── 실행 취소 · 다시 실행 ─────────

        void PushUndo()
        {
            undo.Add((Bitmap)image.Clone());
            if (undo.Count > MaxUndo)
            {
                undo[0].Dispose();
                undo.RemoveAt(0);
            }
            foreach (Bitmap b in redo) b.Dispose();
            redo.Clear();
        }

        void Undo()
        {
            CancelText();
            if (undo.Count == 0) return;
            redo.Add(image);
            image = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            AfterImageChanged();
            dirty = true;
            UpdateTitle();
        }

        void Redo()
        {
            CancelText();
            if (redo.Count == 0) return;
            undo.Add(image);
            image = redo[redo.Count - 1];
            redo.RemoveAt(redo.Count - 1);
            AfterImageChanged();
            dirty = true;
            UpdateTitle();
        }

        void AfterImageChanged()
        {
            hasCrop = false;
            ResizeCanvas();
            canvas.Invalidate();
            UpdateMenus();
            UpdateInfo();
        }

        // ───────── 저장 · 복사 · 인쇄 ─────────

        void Save()
        {
            if (savedPath == null) savedPath = ImageFile.MakePath(Settings.SaveFolder, Settings.FileNamePattern, Settings.Format);
            WriteFile(savedPath);
        }

        void SaveAs()
        {
            string[] formats = { "png", "jpg", "bmp", "gif" };
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "PNG 이미지|*.png|JPG 이미지|*.jpg|BMP 이미지|*.bmp|GIF 이미지|*.gif";
                dlg.FilterIndex = Math.Max(1, Array.IndexOf(formats, Settings.Format) + 1);
                try { Directory.CreateDirectory(Settings.SaveFolder); } catch { }
                dlg.InitialDirectory = Settings.SaveFolder;
                dlg.FileName = Path.GetFileName(savedPath ?? ImageFile.MakePath(Settings.SaveFolder, Settings.FileNamePattern, Settings.Format));
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                Settings.Set("Format", ImageFile.FormatOf(dlg.FileName));
                savedPath = dlg.FileName;
                WriteFile(savedPath);
            }
        }

        void WriteFile(string path)
        {
            try
            {
                ImageFile.Save(image, path, ImageFile.FormatOf(path));
                dirty = false;
                UpdateTitle();
                SetStatus("저장했습니다: " + path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "저장하지 못했습니다.\n" + ex.Message, "몽캡쳐", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void CopyImage()
        {
            if (ImageFile.CopyToClipboard(image)) SetStatus("클립보드에 복사했습니다.");
            else MessageBox.Show(this, "클립보드에 복사하지 못했습니다. 다른 프로그램이 클립보드를 쓰고 있을 수 있습니다.", "몽캡쳐", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void Print()
        {
            using (PrintDocument doc = new PrintDocument())
            {
                doc.DocumentName = "몽캡쳐";
                doc.PrintPage += delegate(object sender, PrintPageEventArgs e)
                {
                    Rectangle area = e.MarginBounds;
                    float scale = Math.Min((float)area.Width / image.Width, (float)area.Height / image.Height);
                    e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    e.Graphics.DrawImage(image, area.X, area.Y, (int)(image.Width * scale), (int)(image.Height * scale));
                    e.HasMorePages = false;
                };
                using (PrintPreviewDialog dlg = new PrintPreviewDialog { Document = doc })
                {
                    dlg.ShowDialog(this);
                }
            }
        }

        // ───────── 도움말 ─────────

        void ShowShortcuts()
        {
            MessageBox.Show(this,
                "편집기 단축키\n\n" +
                "P 펜    H 형광펜    R 사각형    O 타원    L 직선\n" +
                "A 화살표    T 텍스트    N 번호    M 모자이크    C 자르기\n" +
                "Enter 자르기 적용 (텍스트 입력 중에는 글자 확정)    Esc 취소\n\n" +
                "Ctrl+S 저장    Ctrl+Shift+S 다른 이름으로 저장    Ctrl+P 인쇄\n" +
                "Ctrl+C 클립보드로 복사    Ctrl+Z 실행 취소    Ctrl+Y 다시 실행\n" +
                "Ctrl++ 확대    Ctrl+- 축소    Ctrl+0 실제 크기    Ctrl+9 창에 맞춤\n" +
                "Ctrl+마우스 휠 확대·축소\n\n" +
                "전역 캡처 단축키는 몽캡쳐 창의 [캡쳐] 메뉴와 설정 화면에서 확인·변경합니다.",
                "몽캡쳐 단축키", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void ShowAbout()
        {
            MessageBox.Show(this, "몽캡쳐 " + Util.VersionText + "\n\n화면 캡처 도구 (몽 시리즈)", "버전 정보", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ───────── 입력 ─────────

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (image.Width > scroller.ClientSize.Width - 24 || image.Height > scroller.ClientSize.Height - 24) ZoomFit();
            else SetZoom(1f);
            if (!Program.NoFocus) Activate();
        }

        protected override bool ShowWithoutActivation
        {
            get { return Program.NoFocus; }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (textBox != null)
            {
                // 글자 입력 중: Enter 확정, Esc 취소, 나머지는 입력칸이 받는다
                if (e.KeyCode == Keys.Enter) { CommitText(); e.Handled = true; e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape) { CancelText(); e.Handled = true; e.SuppressKeyPress = true; }
                return;
            }
            if (e.Control || e.Alt) return;

            switch (e.KeyCode)
            {
                case Keys.Escape: CancelCrop(); break;
                case Keys.Enter: ApplyCrop(); break;
                case Keys.P: SetTool(Tool.Pen); break;
                case Keys.H: SetTool(Tool.Highlighter); break;
                case Keys.R: SetTool(Tool.Rectangle); break;
                case Keys.O: SetTool(Tool.Ellipse); break;
                case Keys.L: SetTool(Tool.Line); break;
                case Keys.A: SetTool(Tool.Arrow); break;
                case Keys.T: SetTool(Tool.Text); break;
                case Keys.N: SetTool(Tool.Number); break;
                case Keys.M: SetTool(Tool.Mosaic); break;
                case Keys.C: SetTool(Tool.Crop); break;
                case Keys.F1: ShowShortcuts(); break;
                default: return;
            }
            e.Handled = true;
        }

        void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!dirty) return;
            DialogResult r = MessageBox.Show(this, "저장하지 않은 변경 사항이 있습니다. 저장할까요?", "몽캡쳐", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel)
            {
                e.Cancel = true;
            }
            else if (r == DialogResult.Yes)
            {
                Save();
                if (dirty) e.Cancel = true;
            }
        }

        void CanvasDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && tool == Tool.Number)
            {
                numberCounter = 1;
                UpdateInfo();
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            canvas.Focus();
            PointF p = ImagePoint(e.Location);
            if (tool == Tool.Number)
            {
                CommitText();
                StampNumber(p);
                return;
            }
            if (tool == Tool.Text)
            {
                CommitText();
                BeginText(p);
                return;
            }
            CommitText();
            canvas.Capture = true;
            drawing = true;
            startPt = p;
            curPt = p;
            strokePts.Clear();
            strokePts.Add(p);
            canvas.Invalidate();
        }

        void CanvasMove(MouseEventArgs e)
        {
            if (!drawing) return;
            curPt = ImagePoint(e.Location);
            if (tool == Tool.Pen || tool == Tool.Highlighter) strokePts.Add(curPt);
            canvas.Invalidate();
        }

        void CanvasUp(MouseEventArgs e)
        {
            if (!drawing || e.Button != MouseButtons.Left) return;
            drawing = false;
            canvas.Capture = false;
            curPt = ImagePoint(e.Location);
            FinishShape();
            canvas.Invalidate();
        }

        // 캔버스 그리기: 이미지 → 미리보기 순서로 그린다
        void PaintCanvas(Graphics g)
        {
            g.InterpolationMode = zoom >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.DrawImage(image, 0, 0, canvas.Width, canvas.Height);

            g.ScaleTransform(zoom, zoom);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (drawing && (tool == Tool.Crop))
            {
                DrawCropOverlay(g, RectOf(startPt, curPt));
            }
            else if (drawing && tool == Tool.Mosaic)
            {
                using (Pen pen = new Pen(Color.White, 1f / zoom) { DashStyle = DashStyle.Dash })
                    g.DrawRectangle(pen, RectOf(startPt, curPt));
            }
            else if (drawing)
            {
                DrawShape(g, tool, startPt, curPt, strokePts, penColor, penWidth);
            }
            else if (hasCrop)
            {
                DrawCropOverlay(g, cropRect);
            }
        }

        void DrawCropOverlay(Graphics g, Rectangle r)
        {
            using (Region outside = new Region(new Rectangle(0, 0, image.Width, image.Height)))
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            using (Pen pen = new Pen(Color.White, 1f / zoom) { DashStyle = DashStyle.Dash })
            {
                outside.Exclude(r);
                g.FillRegion(dim, outside);
                g.DrawRectangle(pen, r);
            }
        }

        // 캔버스 전용 컨트롤: 마우스 입력과 그리기를 편집기에 넘긴다
        sealed class Canvas : Control
        {
            readonly EditorForm editor;

            public Canvas(EditorForm editor)
            {
                this.editor = editor;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
                Cursor = Cursors.Cross;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                editor.PaintCanvas(e.Graphics);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                editor.CanvasDown(e);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                editor.CanvasMove(e);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                editor.CanvasUp(e);
            }
        }
    }
}
