using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MongCapture
{
    // 화면 복사 · 창 위치 찾기
    static class CaptureService
    {
        public static Rectangle VirtualScreen
        {
            get { return SystemInformation.VirtualScreen; }
        }

        // 전체 화면 범위: 설정 FullScope = primary(주 모니터, 기본) 또는 all(모든 모니터)
        public static Rectangle FullScreenRect()
        {
            return Settings.Get("FullScope", "primary") == "all" ? VirtualScreen : Screen.PrimaryScreen.Bounds;
        }

        public static Bitmap CopyScreen(Rectangle r)
        {
            return CopyScreen(r, Settings.GetBool("IncludeCursor", false));
        }

        public static Bitmap CopyScreen(Rectangle r, bool includeCursor)
        {
            Bitmap bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                if (includeCursor) DrawCursor(g, r);
            }
            return bmp;
        }

        // 화면 복사에는 마우스 커서가 안 찍히므로, 옵션이 켜져 있으면 커서 모양을 따로 그려 넣는다
        static void DrawCursor(Graphics g, Rectangle area)
        {
            NativeMethods.CURSORINFO ci = new NativeMethods.CURSORINFO();
            ci.cbSize = Marshal.SizeOf(typeof(NativeMethods.CURSORINFO));
            if (!NativeMethods.GetCursorInfo(ref ci) || (ci.flags & 1) == 0 || ci.hCursor == IntPtr.Zero) return;
            NativeMethods.ICONINFO ii;
            int hx = 0, hy = 0;
            if (NativeMethods.GetIconInfo(ci.hCursor, out ii))
            {
                hx = ii.xHotspot;
                hy = ii.yHotspot;
                if (ii.hbmMask != IntPtr.Zero) NativeMethods.DeleteObject(ii.hbmMask);
                if (ii.hbmColor != IntPtr.Zero) NativeMethods.DeleteObject(ii.hbmColor);
            }
            IntPtr hdc = g.GetHdc();
            try { NativeMethods.DrawIconEx(hdc, ci.ptScreenPos.X - hx - area.X, ci.ptScreenPos.Y - hy - area.Y, ci.hCursor, 0, 0, 0, IntPtr.Zero, 3); }
            finally { g.ReleaseHdc(hdc); }
        }

        // 가상 화면(background) 안에서 화면 좌표 rect 부분을 잘라낸다
        public static Bitmap Crop(Bitmap background, Rectangle bounds, Rectangle screenRect)
        {
            Rectangle local = Rectangle.Intersect(new Rectangle(screenRect.X - bounds.X, screenRect.Y - bounds.Y, screenRect.Width, screenRect.Height),
                new Rectangle(0, 0, background.Width, background.Height));
            if (local.Width <= 0 || local.Height <= 0) return null;
            return background.Clone(local, PixelFormat.Format32bppArgb);
        }

        // 창의 실제 테두리(그림자 제외). DWM 값이 없으면 일반 창 위치를 쓴다.
        public static Rectangle WindowRect(IntPtr hWnd)
        {
            NativeMethods.RECT r;
            // 창 그림자 포함 옵션을 켜면 그림자(보이지 않는 테두리)까지 포함한 창 위치를 쓴다
            bool withShadow = Settings.GetBool("IncludeShadow", false);
            if (withShadow || NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(NativeMethods.RECT))) != 0)
            {
                if (!NativeMethods.GetWindowRect(hWnd, out r)) return Rectangle.Empty;
            }
            return NativeMethods.ToRectangle(r);
        }

        // 화면 좌표 p 에 보이는 가장 위의 창 (exclude 창은 건너뜀)
        public static IntPtr WindowAt(Point p, IntPtr exclude)
        {
            IntPtr found = IntPtr.Zero;
            NativeMethods.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (h == exclude || !NativeMethods.IsWindowVisible(h) || NativeMethods.IsIconic(h) || NativeMethods.IsCloaked(h))
                    return true;
                string cls = ClassName(h);
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd")
                    return true;
                Rectangle r = WindowRect(h);
                if (r.Width <= 0 || r.Height <= 0 || !r.Contains(p)) return true;
                found = h;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        static string ClassName(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(256);
            NativeMethods.GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }
    }
}
