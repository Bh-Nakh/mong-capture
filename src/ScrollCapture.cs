using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MongCapture
{
    // 스크롤 캡처: 고른 영역에 마우스 휠 메시지를 보내 아래로 스크롤하면서, 겹치는 부분을 찾아 세로로 이어 붙인다.
    // 휠에 반응하는 창(브라우저, 문서, 목록 등)에서 동작한다. Esc 를 누르면 지금까지 모은 것으로 마친다.
    static class ScrollCapture
    {
        const int MaxHeight = 20000;    // 이어 붙인 그림의 최대 높이(px)
        const int MaxSteps = 100;       // 최대 스크롤 횟수
        const int WM_MOUSEWHEEL = 0x020A;
        const int VK_ESCAPE = 0x1B;

        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        public static Bitmap Capture(Rectangle region)
        {
            string note;
            return Capture(region, out note);
        }

        // 마지막 캡처에서 휠을 내린 횟수 (시험·진단용)
        public static int LastStepCount;

        // note: 끝까지 가지 못했을 때 이유(없으면 null)
        // region: 사용자가 그린 사각형. 가로 폭과 위쪽 시작 위치로만 쓰고, 세로는 스크롤되는 창의 아래 끝까지 넓혀서 찍는다.
        public static Bitmap Capture(Rectangle region, out string note)
        {
            note = null;
            LastStepCount = 0;
            NativeMethods.POINT pt = new NativeMethods.POINT();
            pt.X = region.X + region.Width / 2;
            pt.Y = region.Y + region.Height / 2;
            IntPtr target = NativeMethods.WindowFromPoint(pt);

            Rectangle area = Expand(region, target);    // 실제로 찍는 영역

            List<Bitmap> slices = new List<Bitmap>();
            Bitmap prev = SettledNonBlank(area);
            slices.Add((Bitmap)prev.Clone());
            int content = prev.Height;      // 이어 붙인 본문 높이 (아래 고정 막대 줄은 제외)
            int fixedBottom = 0;            // 스크롤해도 안 움직이는 아래쪽 줄 수 (아래 고정 막대 등)
            bool footerChecked = false;

            // 휠 한 칸은 보통 100~120px 움직인다. 처음에는 영역 높이의 60% 쯤 움직이도록 칸 수를 잡고,
            // 첫 스크롤에서 칸당 실제로 몇 px 움직였는지 재서 그다음부터는 영역 높이의 최대 90% 까지 한 번에 내린다.
            int notches = Math.Max(1, (int)(area.Height * 0.6 / 120));
            int unsafeAt = int.MaxValue;    // 이 칸 수로는 겹침이 없어 실패했던 값
            int net = 0;                    // 지금까지 아래로 내려 보낸 휠 칸 수 (끝나면 이만큼 되돌린다)

            for (int step = 0; step < MaxSteps && content < MaxHeight; step++)
            {
                Wheel(target, pt, -notches);
                net += notches;
                LastStepCount++;
                Thread.Sleep(450);
                Application.DoEvents();
                if ((GetAsyncKeyState(VK_ESCAPE) & 0x8000) != 0) break;

                int view = area.Height - fixedBottom;   // 앞뒤 비교에 쓰는 줄 수 (아래 고정 막대는 뺀다)

                // 화면이 멈출 때까지 기다린 뒤 비교한다 (부드러운 스크롤 중이거나 다시 그리는 중에 찍지 않도록)
                Bitmap cur = Settled(area);
                int d = FindShiftRows(prev, cur, view);

                // 첫 스크롤에서 겹침을 못 찾았는데 아래쪽에 안 움직이는 줄(아래 고정 막대)이 있으면, 그 줄을 빼고 다시 비교한다
                int rescuedFooter = 0;
                if (d < 0 && !footerChecked)
                {
                    int k = FixedBottomRowsAny(prev, cur);
                    if (k > 0)
                    {
                        int d2 = FindShiftRows(prev, cur, area.Height - k);
                        if (d2 > 0)
                        {
                            d = d2;
                            rescuedFooter = k;
                        }
                    }
                }

                // 앞 장과 똑같으면 맨 끝일 수도 있지만 아직 안 움직인 것일 수도 있다 → 두 번 더 기다려 보고 그래도 같을 때만 끝으로 본다
                for (int retry = 0; d == 0 && retry < 2; retry++)
                {
                    Thread.Sleep(600);
                    Application.DoEvents();
                    cur.Dispose();
                    cur = Settled(area);
                    d = FindShiftRows(prev, cur, view);
                }

                // 겹침을 못 찾았으면 바로 포기하지 않고 몇 번 더 찍어 본다 (화면 복사가 순간적으로 검거나 덜 그려진 장을 줄 수 있다)
                for (int retry = 0; d < 0 && retry < 3; retry++)
                {
                    Thread.Sleep(700);
                    Application.DoEvents();
                    cur.Dispose();
                    cur = Settled(area);
                    d = FindShiftRows(prev, cur, view);
                }

                if (d == 0)
                {
                    // 더 이상 안 움직임: 맨 끝이거나, 이 창이 휠로 스크롤되지 않는 경우
                    cur.Dispose();
                    if (slices.Count == 1) note = "화면이 스크롤되지 않았습니다.\n이 창이 마우스 휠로 스크롤되는지, 그리고 사각형이 스크롤되는 부분 위에 있는지 확인해 주세요.";
                    break;
                }

                if (d < 0)
                {
                    cur.Dispose();
                    if (notches > 1)
                    {
                        // 한 번에 너무 많이 움직여 앞뒤 화면이 겹치지 않았다 → 되돌리고 칸 수를 줄여 다시 한다
                        Wheel(target, pt, notches);
                        net -= notches;
                        Thread.Sleep(450);
                        Application.DoEvents();
                        using (Bitmap back = CaptureService.CopyScreen(area, false))
                        {
                            if (FindShiftRows(prev, back, view) != 0)
                            {
                                note = "스크롤이 어긋나 지금까지 모은 부분까지만 저장했습니다.";
                                break;
                            }
                        }
                        unsafeAt = notches;
                        notches = Math.Max(1, notches / 2);
                        step--;     // 이 시도는 세지 않는다
                        continue;
                    }
                    note = "스크롤되는 부분이 너무 작아서 이어 붙이지 못했습니다.\n휠 한 칸에 움직이는 거리보다 작으면 앞뒤 화면이 겹치지 않습니다. 사각형을 세로로 더 길게(200px 이상) 그려 주세요.";
                    break;
                }

                // 첫 스크롤 때, 움직이지 않은 아래쪽 줄(아래 고정 막대 등)이 있는지 본다.
                // 있으면 그 줄은 첫 장에서 빼고, 마지막에 한 번만 붙인다 (조각마다 되풀이되지 않게).
                if (!footerChecked)
                {
                    footerChecked = true;
                    fixedBottom = rescuedFooter > 0 ? rescuedFooter : FixedBottomRows(prev, cur, d);
                    if (fixedBottom > 0)
                    {
                        Bitmap first = slices[0];
                        slices[0] = first.Clone(new Rectangle(0, 0, first.Width, first.Height - fixedBottom), PixelFormat.Format32bppArgb);
                        first.Dispose();
                        content -= fixedBottom;
                        view = area.Height - fixedBottom;
                    }
                }

                // 새로 나타난 아래쪽 d 줄만 붙인다
                slices.Add(cur.Clone(new Rectangle(0, view - d, cur.Width, d), PixelFormat.Format32bppArgb));
                content += d;
                prev.Dispose();
                prev = cur;

                // 칸당 실제 이동 거리로 다음 칸 수를 정한다 (실패했던 칸 수보다는 늘리지 않는다)
                double perNotch = (double)d / notches;
                int next = (int)Math.Floor(view * 0.9 / perNotch);
                next = Math.Max(1, Math.Min(40, next));
                if (next >= unsafeAt) next = Math.Max(1, unsafeAt - 1);
                notches = next;
            }

            // 아래 고정 막대 줄은 마지막 장에서 한 번만 가져온다
            Bitmap footer = null;
            if (fixedBottom > 0) footer = prev.Clone(new Rectangle(0, prev.Height - fixedBottom, prev.Width, fixedBottom), PixelFormat.Format32bppArgb);
            prev.Dispose();

            // 캡처가 끝났으니 페이지를 처음 있던 스크롤 위치로 되돌린다
            while (net > 0)
            {
                int n = Math.Min(net, 200);
                Wheel(target, pt, n);
                net -= n;
                Thread.Sleep(40);
            }
            Thread.Sleep(300);
            Application.DoEvents();

            Bitmap result = new Bitmap(area.Width, content + fixedBottom, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                int y = 0;
                foreach (Bitmap sl in slices)
                {
                    g.DrawImageUnscaled(sl, 0, y);
                    y += sl.Height;
                    sl.Dispose();
                }
                if (footer != null)
                {
                    g.DrawImageUnscaled(footer, 0, y);
                    footer.Dispose();
                }
            }
            return result;
        }

        // 사각형의 위쪽 y 부터 스크롤되는 창의 아래 끝까지로 찍는 영역을 넓힌다 (가로 폭은 그대로).
        // 사각형 한가운데 점에 있는 창을 스크롤되는 부분으로 보고, 그 창이 부모 창 밖으로 삐져나온 부분은 잘라 낸다.
        // 창을 못 찾으면 사각형 그대로 쓴다.
        static Rectangle Expand(Rectangle region, IntPtr target)
        {
            if (target == IntPtr.Zero) return region;
            Rectangle visible = VisibleRect(target);
            IntPtr desktop = NativeMethods.GetDesktopWindow();
            IntPtr parent = NativeMethods.GetAncestor(target, 1);    // GA_PARENT
            while (parent != IntPtr.Zero && parent != desktop)
            {
                visible = Rectangle.Intersect(visible, VisibleRect(parent));
                parent = NativeMethods.GetAncestor(parent, 1);
            }
            Point center = new Point(region.X + region.Width / 2, region.Y + region.Height / 2);
            if (visible.IsEmpty || !visible.Contains(center)) return region;

            int bottom = Math.Min(Screen.FromRectangle(region).Bounds.Bottom, Math.Max(region.Bottom, visible.Bottom));
            int height = bottom - region.Y;
            return height > region.Height ? new Rectangle(region.X, region.Y, region.Width, height) : region;
        }

        // 창의 실제로 보이는 테두리 (그림자 제외)
        static Rectangle VisibleRect(IntPtr hWnd)
        {
            NativeMethods.RECT r;
            if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(NativeMethods.RECT))) != 0)
            {
                if (!NativeMethods.GetWindowRect(hWnd, out r)) return Rectangle.Empty;
            }
            return NativeMethods.ToRectangle(r);
        }

        // 이동량을 모르는 상태에서, 두 장에서 같은 자리에 그대로 있는 아래쪽 줄 수 (전체의 1/4 까지만 본다)
        static int FixedBottomRowsAny(Bitmap a, Bitmap b)
        {
            long[][] ha = RowHashes(a), hb = RowHashes(b);
            int h = a.Height, limit = h / 4, k = 0;
            while (k < limit && SameStrips(ha[h - 1 - k], hb[h - 1 - k]) == Strips) k++;
            return k;
        }

        // 두 장에서 같은 자리에 그대로 있는 아래쪽 줄 수 (아래 고정 막대). 새로 나타난 d 줄 안에서만, 전체의 1/4 까지만 본다.
        static int FixedBottomRows(Bitmap a, Bitmap b, int d)
        {
            long[][] ha = RowHashes(a), hb = RowHashes(b);
            int h = a.Height, limit = Math.Min(d, h / 4), k = 0;
            while (k < limit && SameStrips(ha[h - 1 - k], hb[h - 1 - k]) == Strips) k++;
            return k;
        }

        // 처음 장이 통째로 한 색(검정·빈 화면)이면 덜 그려진 것일 수 있어 몇 번 더 찍는다
        static Bitmap SettledNonBlank(Rectangle region)
        {
            Bitmap b = Settled(region);
            for (int i = 0; i < 3 && IsUniform(b); i++)
            {
                b.Dispose();
                Thread.Sleep(500);
                Application.DoEvents();
                b = Settled(region);
            }
            return b;
        }

        // 모든 줄이 똑같은 그림인지 (세로로 아무 변화가 없어 스크롤을 맞출 수 없는 그림)
        static bool IsUniform(Bitmap bmp)
        {
            long[][] rows = RowHashes(bmp);
            for (int y = 1; y < rows.Length; y++)
            {
                if (SameStrips(rows[y], rows[0]) != Strips) return false;
            }
            return true;
        }

        // 화면이 가만히 있는 것을 두 번 연속 확인한 그림을 돌려준다 (최대 6번 다시 찍는다)
        static Bitmap Settled(Rectangle region)
        {
            Bitmap a = CaptureService.CopyScreen(region, false);
            for (int i = 0; i < 6; i++)
            {
                Thread.Sleep(120);
                Application.DoEvents();
                Bitmap b = CaptureService.CopyScreen(region, false);
                if (FindShift(a, b) == 0)
                {
                    b.Dispose();
                    return a;
                }
                a.Dispose();
                a = b;
            }
            return a;
        }

        static void Wheel(IntPtr target, NativeMethods.POINT pt, int notches)
        {
            uint delta = (uint)(ushort)(short)(120 * notches);
            IntPtr wParam = new IntPtr((int)(delta << 16));
            IntPtr lParam = new IntPtr(((pt.Y & 0xFFFF) << 16) | (pt.X & 0xFFFF));
            NativeMethods.PostMessage(target, WM_MOUSEWHEEL, wParam, lParam);
        }

        const int Strips = 4;           // 줄을 세로 띠 4개로 나눠 비교한다 (고정 머리글·떠 있는 창이 일부를 가려도 맞추기 위해)
        const int StripsNeeded = 2;     // 띠 4개 중 이만큼 이상 같으면 "같은 줄"로 본다

        // 두 장(앞, 뒤)이 얼마나 스크롤됐는지 찾는다.
        // 돌려주는 값: d>0 이면 뒤 그림의 r 번째 줄이 앞 그림의 r+d 번째 줄과 같다 / 0 이면 같은 그림 / -1 이면 못 맞춤
        public static int FindShift(Bitmap a, Bitmap b)
        {
            return FindShiftRows(a, b, a.Height);
        }

        // rows: 위쪽 몇 줄만 비교할지 (아래 고정 막대 줄은 빼고 비교한다)
        static int FindShiftRows(Bitmap a, Bitmap b, int rows)
        {
            int h = rows;
            if (b.Height != a.Height || b.Width != a.Width || rows > a.Height) return -1;
            long[][] ha = RowHashes(a), hb = RowHashes(b);

            // 모든 띠가 같은 줄이 99.5% 이상이면 아무것도 안 움직인 것 (맨 끝)
            int same = 0;
            for (int i = 0; i < h; i++) if (SameStrips(ha[i], hb[i]) == Strips) same++;
            if (same >= h * 0.995) return 0;

            int best = -1;
            double bestRatio = 0;
            int minOverlap = Math.Max(8, Math.Min(24, h / 5));
            for (int d = 1; d <= h - minOverlap; d++)
            {
                int n = h - d, match = 0;
                for (int i = 0; i < n; i++) if (SameStrips(ha[i + d], hb[i]) >= StripsNeeded) match++;
                double ratio = (double)match / n;
                if (ratio > bestRatio + 1e-9)
                {
                    bestRatio = ratio;
                    best = d;
                }
            }
            return bestRatio >= 0.8 ? best : -1;
        }

        static int SameStrips(long[] x, long[] y)
        {
            int c = 0;
            for (int s = 0; s < Strips; s++) if (x[s] == y[s]) c++;
            return c;
        }

        // 줄마다 띠별 해시를 만든다
        static long[][] RowHashes(Bitmap bmp)
        {
            long[][] result = new long[bmp.Height][];
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int[] row = new int[bmp.Width];
                int stripWidth = Math.Max(1, bmp.Width / Strips);
                for (int y = 0; y < bmp.Height; y++)
                {
                    Marshal.Copy(new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), row, 0, bmp.Width);
                    long[] hashes = new long[Strips];
                    unchecked
                    {
                        for (int s = 0; s < Strips; s++)
                        {
                            int from = s * stripWidth, to = s == Strips - 1 ? bmp.Width : Math.Min(bmp.Width, from + stripWidth);
                            long h = (long)1469598103934665603;
                            for (int x = from; x < to; x++) h = (h ^ (row[x] & 0xFFFFFF)) * 1099511628211;
                            hashes[s] = h;
                        }
                    }
                    result[y] = hashes;
                }
            }
            finally
            {
                bmp.UnlockBits(bd);
            }
            return result;
        }
    }
}
