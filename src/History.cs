using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MongCapture
{
    // 자동 저장한 캡처 파일 목록 (최근 것이 위). %AppData%\MongCapture\history.txt 에 한 줄에 하나씩 둔다.
    static class History
    {
        const int Max = 100;

        static string ListFile
        {
            get { return Path.Combine(Settings.FolderPath, "history.txt"); }
        }

        // 지금도 있는 파일만 돌려준다
        public static List<string> Load()
        {
            List<string> list = new List<string>();
            try
            {
                if (!File.Exists(ListFile)) return list;
                foreach (string line in File.ReadAllLines(ListFile, Encoding.UTF8))
                {
                    string p = line.Trim();
                    if (p.Length > 0 && File.Exists(p) && !list.Contains(p)) list.Add(p);
                }
            }
            catch { }
            return list;
        }

        public static void Add(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            List<string> list = Load();
            list.Remove(path);
            list.Insert(0, path);
            if (list.Count > Max) list.RemoveRange(Max, list.Count - Max);
            Write(list);
        }

        public static void Remove(string path)
        {
            List<string> list = Load();
            list.Remove(path);
            Write(list);
        }

        static void Write(List<string> list)
        {
            try
            {
                Directory.CreateDirectory(Settings.FolderPath);
                File.WriteAllLines(ListFile, list.ToArray(), Encoding.UTF8);
            }
            catch { }
        }
    }
}
