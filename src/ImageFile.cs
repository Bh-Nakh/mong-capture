using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MongCapture
{
    // 이미지 저장 · 파일 이름 · 클립보드
    static class ImageFile
    {
        public static ImageFormat ToImageFormat(string fmt)
        {
            switch (fmt)
            {
                case "jpg": return ImageFormat.Jpeg;
                case "bmp": return ImageFormat.Bmp;
                case "gif": return ImageFormat.Gif;
                default: return ImageFormat.Png;
            }
        }

        // 파일 이름 토큰: {yyyy}{MM}{dd}{HH}{mm}{ss} 날짜·시간, {N} 순번(001, 002...). 같은 이름이 있으면 (2), (3)을 붙인다.
        public static string MakePath(string folder, string pattern, string fmt)
        {
            DateTime now = DateTime.Now;
            string stem = (pattern ?? "")
                .Replace("{yyyy}", now.ToString("yyyy")).Replace("{MM}", now.ToString("MM")).Replace("{dd}", now.ToString("dd"))
                .Replace("{HH}", now.ToString("HH")).Replace("{mm}", now.ToString("mm")).Replace("{ss}", now.ToString("ss"));
            foreach (char c in Path.GetInvalidFileNameChars()) stem = stem.Replace(c.ToString(), "");
            stem = stem.Trim();
            if (stem.Length == 0) stem = "캡쳐";

            bool numbered = stem.IndexOf("{N}", StringComparison.Ordinal) >= 0;
            for (int n = 1; ; n++)
            {
                string name;
                if (numbered) name = stem.Replace("{N}", n.ToString("000"));
                else name = n == 1 ? stem : stem + " (" + n + ")";
                string path = Path.Combine(folder, name + "." + fmt);
                if (!File.Exists(path)) return path;
            }
        }

        public static void Save(Bitmap bmp, string path, string fmt)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (fmt == "jpg")
            {
                ImageCodecInfo codec = null;
                foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders())
                {
                    if (c.FormatID == ImageFormat.Jpeg.Guid) { codec = c; break; }
                }
                using (EncoderParameters ep = new EncoderParameters(1))
                {
                    ep.Param[0] = new EncoderParameter(Encoder.Quality, (long)Math.Max(1, Math.Min(100, Settings.GetInt("JpgQuality", 90))));
                    bmp.Save(path, codec, ep);
                }
            }
            else
            {
                bmp.Save(path, ToImageFormat(fmt));
            }
        }

        public static string FormatOf(string path)
        {
            string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            return ext == "jpeg" ? "jpg" : ext;
        }

        // 다른 프로그램이 클립보드를 잡고 있으면 잠깐 뒤 다시 시도한다
        public static bool CopyToClipboard(Image image)
        {
            return CopyToClipboard(image, null);
        }

        // 이미지와(있으면) 글자를 한 번에 클립보드에 넣는다 (붙여넣는 곳에 따라 그림 또는 글자로 쓰인다)
        public static bool CopyToClipboard(Image image, string text)
        {
            for (int i = 0; i < 12; i++)
            {
                try
                {
                    if (text == null)
                    {
                        Clipboard.SetImage(image);
                    }
                    else
                    {
                        DataObject data = new DataObject();
                        if (image != null) data.SetImage(image);
                        data.SetText(text);
                        Clipboard.SetDataObject(data, true);
                    }
                    return true;
                }
                catch (ExternalException)
                {
                    System.Threading.Thread.Sleep(60 + i * 20);
                }
            }
            return false;
        }
    }
}
