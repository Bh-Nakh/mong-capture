using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace MongCapture
{
    // 문자 인식(OCR): Windows 10 기본 OCR 엔진을 쓴다 (설치된 언어 중 사용자 프로필 언어, 보통 한국어·영어).
    // 이 엔진은 글자가 작으면(화면의 보통 글자 12~20px) 아무것도 못 읽으므로, 그림을 2~4배로 키워서 읽어 보고
    // 가장 많이 읽힌 결과를 쓴다.
    static class Ocr
    {
        // 인식한 글자를 돌려준다. 엔진을 쓸 수 없으면 null, 글자가 없으면 빈 문자열.
        public static string Recognize(Bitmap bmp)
        {
            // WinForms 화면 스레드를 막지 않고 기다리도록 별도 작업에서 실행한다
            return Task.Run(() => RecognizeAllAsync(bmp)).GetAwaiter().GetResult();
        }

        static async Task<string> RecognizeAllAsync(Bitmap bmp)
        {
            OcrEngine engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null) return null;

            int max = (int)OcrEngine.MaxImageDimension;
            string best = "";
            int bestScore = 0;

            // 3배를 먼저, 그다음 2배·4배. 모두 비면 원래 크기로도 해 본다 (큰 글자 그림).
            foreach (int scale in new int[] { 3, 2, 4, 1 })
            {
                if (scale == 1 && bestScore > 0) break;
                if ((long)bmp.Width * scale > max || (long)bmp.Height * scale > max)
                {
                    if (scale != 1) continue;           // 너무 커서 키울 수 없으면 원래 크기만
                }
                using (Bitmap work = scale == 1 ? FitTo(bmp, max) : Scale(bmp, scale))
                {
                    string text = await ReadAsync(engine, work);
                    int score = CountLetters(text);
                    if (score > bestScore)
                    {
                        best = text;
                        bestScore = score;
                    }
                }
            }
            return best;
        }

        static async Task<string> ReadAsync(OcrEngine engine, Bitmap work)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                work.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                var stream = System.IO.WindowsRuntimeStreamExtensions.AsRandomAccessStream(ms);
                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
                using (SoftwareBitmap sb = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied))
                {
                    OcrResult result = await engine.RecognizeAsync(sb);
                    return result.Text ?? "";
                }
            }
        }

        // 글자·숫자 수 (어느 배율의 결과가 더 잘 읽혔는지 비교하는 점수).
        // 공백은 물론 \ , " 같은 깨진 기호는 세지 않아서, 기호가 섞여 길어진 엉터리 결과가 뽑히지 않는다.
        static int CountLetters(string text)
        {
            int n = 0;
            foreach (char c in text) if (char.IsLetterOrDigit(c)) n++;
            return n;
        }

        static Bitmap Scale(Bitmap src, int scale)
        {
            Bitmap b = new Bitmap(src.Width * scale, src.Height * scale, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, 0, 0, b.Width, b.Height);
            }
            return b;
        }

        // 엔진이 받는 최대 크기를 넘으면 줄인다
        static Bitmap FitTo(Bitmap src, int max)
        {
            if (src.Width <= max && src.Height <= max) return new Bitmap(src);
            double scale = Math.Min((double)max / src.Width, (double)max / src.Height);
            return new Bitmap(src, new Size((int)(src.Width * scale), (int)(src.Height * scale)));
        }
    }
}
