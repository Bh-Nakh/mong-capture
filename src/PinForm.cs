using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MongCapture
{
    // 고정 창: 캡처한 그림을 항상 위에 떠 있는 작은 창으로 둔다. 끌어서 옮기고, 휠로 크기를, 우클릭 메뉴로 투명도를 바꾼다.
    sealed class PinForm : Form
    {
        readonly Bitmap image;
        readonly Action<Bitmap> openEditor;
        float scale = 1f;
        bool dragging;
        Point dragOrigin;

        public PinForm(Bitmap image, Point location, Action<Bitmap> openEditor)
        {
            this.image = image;
            this.openEditor = openEditor;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Location = location;
            ClientSize = image.Size;
            Icon = Util.AppIcon;
            Text = "몽캡쳐 고정 창";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            ContextMenuStrip = BuildMenu();
        }

        protected override bool ShowWithoutActivation
        {
            get { return Program.NoFocus; }
        }

        ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem opacity = new ToolStripMenuItem("투명도");
            foreach (int percent in new int[] { 100, 80, 60, 40, 20 })
            {
                int p = percent;
                opacity.DropDownItems.Add(new ToolStripMenuItem(p + "%", null, delegate { Opacity = p / 100.0; }));
            }
            ToolStripMenuItem size = new ToolStripMenuItem("크기");
            foreach (int percent in new int[] { 25, 50, 100, 150, 200 })
            {
                int p = percent;
                size.DropDownItems.Add(new ToolStripMenuItem(p + "%", null, delegate { SetScale(p / 100f); }));
            }
            menu.Items.Add(opacity);
            menu.Items.Add(size);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("클립보드로 복사", null, delegate { ImageFile.CopyToClipboard(image); });
            menu.Items.Add("편집기로 열기", null, delegate { if (openEditor != null) openEditor((Bitmap)image.Clone()); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("닫기 (더블클릭, Esc)", null, delegate { Close(); });
            return menu;
        }

        void SetScale(float s)
        {
            scale = Math.Max(0.1f, Math.Min(4f, s));
            ClientSize = new Size(Math.Max(20, (int)(image.Width * scale)), Math.Max(20, (int)(image.Height * scale)));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(image, 0, 0, ClientSize.Width, ClientSize.Height);
            using (Pen pen = new Pen(Color.FromArgb(0, 120, 215), 1))
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            dragOrigin = e.Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) Location = new Point(Location.X + e.X - dragOrigin.X, Location.Y + e.Y - dragOrigin.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragging = false;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            Close();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            SetScale(scale + (e.Delta > 0 ? 0.1f : -0.1f));
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            image.Dispose();
        }
    }
}
