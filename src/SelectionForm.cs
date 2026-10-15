using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MongCapture
{
    enum SelectMode { Region, Window, Color }

    // 화면 전체를 덮는 선택 창.
    // 영역: 드래그해서 마우스를 놓으면 바로 캡처한다. 드래그하는 동안 방향키로 끝점을 1px씩 조정할 수 있다.
    // 창: 마우스를 올린 창을 파란 테두리로 보여 주고 클릭(또는 Enter)으로 고른다.
    // 색: 클릭한 지점의 색을 고른다.
    // 영역·색 모드에서는 돋보기(확대 격자, 좌표, RGB)가 커서 옆에 뜬다. Esc 또는 오른쪽 클릭은 취소.
    sealed class SelectionForm : Form
    {
        const int LoupeCells = 15;      // 돋보기가 보여 주는 칸 수 (가로·세로)
        const int LoupeCell = 10;       // 한 칸의 화면 크기(px)

        readonly Bitmap background;     // 선택 창을 띄우기 전에 찍은 화면 (가려지지 않은 원본)
        readonly Rectangle bounds;      // 선택 창이 덮는 범위 (보통 모든 모니터)
        readonly SelectMode mode;
        readonly bool showLoupe;
        readonly string hint;           // 위쪽에 띄울 안내문 (없으면 null)

        Point dragStart;                // 화면 좌표
        Point lastMouse;                // 화면 좌표 (조정값 제외)
        Point nudge;                    // 방향키로 준 조정값
        bool dragging;
        bool hasSelection;
        Rectangle selection;            // 화면 좌표

        IntPtr hoverWindow = IntPtr.Zero;
        Rectangle hoverRect;            // 화면 좌표

        public SelectionForm(Bitmap background, Rectangle bounds, SelectMode mode) : this(background, bounds, mode, null)
        {
        }

        public SelectionForm(Bitmap background, Rectangle bounds, SelectMode mode, string hint)
        {
            this.hint = hint;
            this.background = background;
            this.bounds = bounds;
            this.mode = mode;
            showLoupe = mode != SelectMode.Window && Settings.GetBool("Magnifier", true);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            TopMost = true;
            ShowInTaskbar = false;
            KeyPreview = true;
            BackColor = Color.Black;
            Cursor = Cursors.Cross;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        // 화면 좌표로 된 선택 영역 (확정했을 때만 의미가 있음). 색 모드에서는 고른 지점의 1x1 영역.
        public Rectangle Selection
        {
            get { return selection; }
        }

        protected override bool ShowWithoutActivation
        {
            get { return Program.NoFocus; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!Program.NoFocus) Activate();
            lastMouse = Cursor.Position;
            if (mode == SelectMode.Window) UpdateHover(Cursor.Position);
            else if (showLoupe) Invalidate();
        }

        Point ToScreen(Point client)
        {
            return new Point(client.X + bounds.X, client.Y + bounds.Y);
        }

        Point ToClient(Point screen)
        {
            return new Point(screen.X - bounds.X, screen.Y - bounds.Y);
        }

        Point Clamp(Point p)
        {
            return new Point(Math.Max(bounds.Left, Math.Min(bounds.Right - 1, p.X)), Math.Max(bounds.Top, Math.Min(bounds.Bottom - 1, p.Y)));
        }

        void UpdateHover(Point screenPoint)
        {
            IntPtr h = CaptureService.WindowAt(screenPoint, Handle);
            hoverWindow = h;
            hoverRect = h == IntPtr.Zero ? Rectangle.Empty : Rectangle.Intersect(CaptureService.WindowRect(h), bounds);
            Invalidate();
        }

        void UpdateDrag()
        {
            Point end = Clamp(new Point(lastMouse.X + nudge.X, lastMouse.Y + nudge.Y));
            // 끝점 칸까지 포함해서 선택한다 (시작·끝 점이 같으면 1x1)
            selection = Rectangle.FromLTRB(Math.Min(dragStart.X, end.X), Math.Min(dragStart.Y, end.Y),
                Math.Max(dragStart.X, end.X) + 1, Math.Max(dragStart.Y, end.Y) + 1);
            hasSelection = true;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                DialogResult = DialogResult.Cancel;
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            if (mode == SelectMode.Window)
            {
                if (hoverWindow != IntPtr.Zero && !hoverRect.IsEmpty)
                {
                    selection = hoverRect;
                    DialogResult = DialogResult.OK;
                }
                return;
            }

            if (mode == SelectMode.Color)
            {
                Point p = Clamp(ToScreen(e.Location));
                selection = new Rectangle(p.X, p.Y, 1, 1);
                DialogResult = DialogResult.OK;
                return;
            }

            dragging = true;
            nudge = Point.Empty;
            dragStart = Clamp(ToScreen(e.Location));
            lastMouse = dragStart;
            hasSelection = false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point sp = ToScreen(e.Location);
            lastMouse = sp;
            if (mode == SelectMode.Window)
            {
                UpdateHover(sp);
                return;
            }
            if (dragging) UpdateDrag();
            else if (showLoupe) Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (mode != SelectMode.Region || !dragging || e.Button != MouseButtons.Left) return;
            dragging = false;
            lastMouse = ToScreen(e.Location);
            UpdateDrag();

            // 너무 작게 끌었으면(클릭만 한 경우 등) 선택을 버리고 다시 고르게 한다. 아니면 바로 캡처한다.
            if (selection.Width < 3 || selection.Height < 3)
            {
                hasSelection = false;
                Invalidate();
                return;
            }
            DialogResult = DialogResult.OK;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                return;
            }
            if (e.KeyCode == Keys.Enter)
            {
                if (mode == SelectMode.Window && hoverWindow != IntPtr.Zero && !hoverRect.IsEmpty)
                {
                    selection = hoverRect;
                    DialogResult = DialogResult.OK;
                }
                else if (mode == SelectMode.Region && hasSelection && selection.Width >= 3 && selection.Height >= 3)
                {
                    DialogResult = DialogResult.OK;
                }
                return;
            }
            if (mode != SelectMode.Region || !dragging) return;

            // 드래그하는 동안 방향키로 끝점을 1px씩 조정
            switch (e.KeyCode)
            {
                case Keys.Left: nudge.X -= 1; break;
                case Keys.Right: nudge.X += 1; break;
                case Keys.Up: nudge.Y -= 1; break;
                case Keys.Down: nudge.Y += 1; break;
                default: return;
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
            UpdateDrag();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.DrawImageUnscaled(background, 0, 0);

            Rectangle sel = Rectangle.Empty;
            if (mode == SelectMode.Window && !hoverRect.IsEmpty) sel = new Rectangle(ToClient(hoverRect.Location), hoverRect.Size);
            else if (mode == SelectMode.Region && hasSelection) sel = new Rectangle(ToClient(selection.Location), selection.Size);

            using (SolidBrush dim = new SolidBrush(Color.FromArgb(110, 0, 0, 0)))
                g.FillRectangle(dim, ClientRectangle);

            if (!sel.IsEmpty)
            {
                // 선택 영역은 어둡게 덮지 않고 원본 화면을 그대로 보여 준다
                g.DrawImage(background, sel, sel.X, sel.Y, sel.Width, sel.Height, GraphicsUnit.Pixel);
                using (Pen pen = new Pen(Color.FromArgb(0, 120, 215), 2))
                    g.DrawRectangle(pen, sel.X, sel.Y, sel.Width - 1, sel.Height - 1);

                string label = sel.Width + " x " + sel.Height + " px";
                using (Font font = new Font(SystemFonts.MessageBoxFont.FontFamily, 9f))
                {
                    SizeF size = g.MeasureString(label, font);
                    float x = sel.X;
                    float y = sel.Y - size.Height - 6;
                    if (y < 0) y = sel.Y + 6;
                    RectangleF box = new RectangleF(x, y, size.Width + 10, size.Height + 4);
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(190, 0, 0, 0)))
                    {
                        g.FillRectangle(bg, box);
                        g.DrawString(label, font, Brushes.White, x + 5, y + 2);
                    }
                }
            }

            if (hint != null) DrawHint(g);
            if (showLoupe) DrawLoupe(g);
        }

        // 안내문: 마우스가 있는 모니터의 위쪽 가운데에 띄운다
        void DrawHint(Graphics g)
        {
            Rectangle area = Rectangle.Intersect(Screen.FromPoint(lastMouse).Bounds, bounds);
            if (area.IsEmpty) area = bounds;
            Point origin = ToClient(area.Location);
            using (Font font = new Font(SystemFonts.MessageBoxFont.FontFamily, 11f, FontStyle.Bold))
            {
                SizeF size = g.MeasureString(hint, font, Math.Max(200, area.Width - 120));
                Rectangle box = new Rectangle(origin.X + (area.Width - (int)size.Width - 32) / 2, origin.Y + 24, (int)size.Width + 32, (int)size.Height + 20);
                using (SolidBrush back = new SolidBrush(Color.FromArgb(225, 20, 20, 20)))
                    g.FillRectangle(back, box);
                using (Pen pen = new Pen(Color.FromArgb(0, 120, 215), 2))
                    g.DrawRectangle(pen, box);
                g.DrawString(hint, font, Brushes.White, new RectangleF(box.X + 16, box.Y + 10, size.Width + 2, size.Height + 2));
            }
        }

        // 돋보기: 커서 주변을 10배로 키워 한 칸씩 보여 주고, 좌표와 색(#RRGGBB, RGB)을 적는다
        void DrawLoupe(Graphics g)
        {
            Point m = Clamp(lastMouse);
            Point c = ToClient(m);
            int size = LoupeCells * LoupeCell;
            int panelH = mode == SelectMode.Color ? 58 : 42;
            int x = c.X + 24, y = c.Y + 24;
            if (x + size > ClientSize.Width) x = c.X - 24 - size;
            if (y + size + panelH > ClientSize.Height) y = c.Y - 24 - size - panelH;
            if (x < 0) x = 0;
            if (y < 0) y = 0;

            Rectangle dst = new Rectangle(x, y, size, size);
            int half = LoupeCells / 2;
            GraphicsState state = g.Save();
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.FillRectangle(Brushes.Black, dst);
            g.DrawImage(background, dst, c.X - half, c.Y - half, LoupeCells, LoupeCells, GraphicsUnit.Pixel);
            g.Restore(state);

            using (Pen grid = new Pen(Color.FromArgb(50, 255, 255, 255)))
            {
                for (int i = 1; i < LoupeCells; i++)
                {
                    g.DrawLine(grid, x + i * LoupeCell, y, x + i * LoupeCell, y + size);
                    g.DrawLine(grid, x, y + i * LoupeCell, x + size, y + i * LoupeCell);
                }
            }
            using (Pen center = new Pen(Color.FromArgb(255, 60, 60), 2))
                g.DrawRectangle(center, x + half * LoupeCell, y + half * LoupeCell, LoupeCell, LoupeCell);
            using (Pen frame = new Pen(Color.White, 2))
                g.DrawRectangle(frame, x, y, size, size);

            int px = Math.Max(0, Math.Min(background.Width - 1, c.X)), py = Math.Max(0, Math.Min(background.Height - 1, c.Y));
            Color col = background.GetPixel(px, py);
            string hex = "#" + col.R.ToString("X2") + col.G.ToString("X2") + col.B.ToString("X2");
            using (Font font = new Font(SystemFonts.MessageBoxFont.FontFamily, 9f))
            using (SolidBrush panel = new SolidBrush(Color.FromArgb(215, 0, 0, 0)))
            using (SolidBrush swatch = new SolidBrush(col))
            {
                g.FillRectangle(panel, x, y + size, size, panelH);
                g.FillRectangle(swatch, x + 6, y + size + 6, 20, 20);
                g.DrawRectangle(Pens.White, x + 6, y + size + 6, 20, 20);
                g.DrawString(m.X + ", " + m.Y + "   " + hex, font, Brushes.White, x + 32, y + size + 4);
                g.DrawString("RGB(" + col.R + ", " + col.G + ", " + col.B + ")", font, Brushes.White, x + 32, y + size + 22);
                if (mode == SelectMode.Color) g.DrawString("클릭하면 " + hex + " 를 복사합니다", font, Brushes.Gold, x + 6, y + size + 40);
            }
        }
    }
}
