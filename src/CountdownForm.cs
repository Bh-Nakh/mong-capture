using System;
using System.Drawing;
using System.Windows.Forms;

namespace MongCapture
{
    // 타이머 캡처의 카운트다운 표시. 포커스를 가져가지 않고(지금 작업 중인 창이 그대로 있어야 하므로), 클릭하면 취소한다.
    sealed class CountdownForm : Form
    {
        readonly Timer timer = new Timer { Interval = 1000 };
        int remaining;

        public bool Done;
        public bool Cancelled;

        public CountdownForm(int seconds)
        {
            remaining = seconds;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(150, 150);
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(work.Right - Width - 24, work.Top + 24);
            BackColor = Color.FromArgb(32, 32, 32);
            Opacity = 0.9;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            timer.Tick += delegate
            {
                remaining--;
                if (remaining <= 0) Finish(false);
                else Invalidate();
            };
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            timer.Start();
        }

        void Finish(bool cancelled)
        {
            timer.Stop();
            Cancelled = cancelled;
            Done = true;
            Hide();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Finish(true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (Font big = new Font(SystemFonts.MessageBoxFont.FontFamily, 54f, FontStyle.Bold))
            using (Font small = new Font(SystemFonts.MessageBoxFont.FontFamily, 9f))
            using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString(Math.Max(0, remaining).ToString(), big, Brushes.White, new RectangleF(0, 8, Width, Height - 40), center);
                g.DrawString("클릭하면 취소", small, Brushes.Silver, new RectangleF(0, Height - 30, Width, 24), center);
            }
            using (Pen pen = new Pen(Color.FromArgb(0, 120, 215), 2))
                g.DrawRectangle(pen, 1, 1, Width - 2, Height - 2);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
