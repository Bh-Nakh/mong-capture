using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace MongCapture
{
    // 모든 선택사항은 바꾸는 즉시 %AppData%\MongCapture\settings.ini 에 저장한다 (공통 규칙 12).
    static class Settings
    {
        static string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MongCapture");
        static string file = Path.Combine(dir, "settings.ini");

        // 시험용: 설정 파일을 다른 폴더에 둔다 (--data 폴더)
        public static void UseDirectory(string folder)
        {
            dir = folder;
            file = Path.Combine(dir, "settings.ini");
        }
        static readonly Dictionary<string, string> values = new Dictionary<string, string>();

        // 옵션이 아닌 창 위치 기억값. 기본값 복원 때도 지우지 않는다.
        static readonly string[] geometryKeys = { "HubBounds", "EditorBounds", "EditorMaximized", "LastRegion" };

        public static void Load()
        {
            values.Clear();
            try
            {
                if (!File.Exists(file)) return;
                foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1);
                }
            }
            catch { }
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(dir);
                List<string> lines = new List<string>();
                foreach (KeyValuePair<string, string> kv in values) lines.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(file, lines.ToArray(), Encoding.UTF8);
            }
            catch { }
        }

        // 설정 파일이 들어 있는 폴더 (기록 목록 등 다른 데이터도 여기에 둔다)
        public static string FolderPath
        {
            get { return dir; }
        }

        public static bool Has(string key)
        {
            return values.ContainsKey(key);
        }

        public static string Get(string key, string def)
        {
            string v;
            return values.TryGetValue(key, out v) && v != null ? v : def;
        }

        public static void Set(string key, string value)
        {
            values[key] = value ?? "";
            Save();
        }

        public static int GetInt(string key, int def)
        {
            int n;
            return int.TryParse(Get(key, null), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : def;
        }

        public static void SetInt(string key, int value)
        {
            Set(key, value.ToString(CultureInfo.InvariantCulture));
        }

        public static bool GetBool(string key, bool def)
        {
            return Get(key, def ? "1" : "0") == "1";
        }

        public static void SetBool(string key, bool value)
        {
            Set(key, value ? "1" : "0");
        }

        public static Rectangle GetRect(string key)
        {
            string[] p = Get(key, "").Split(',');
            int x, y, w, h;
            if (p.Length == 4 && int.TryParse(p[0], out x) && int.TryParse(p[1], out y)
                && int.TryParse(p[2], out w) && int.TryParse(p[3], out h) && w > 0 && h > 0)
                return new Rectangle(x, y, w, h);
            return Rectangle.Empty;
        }

        public static void SetRect(string key, Rectangle r)
        {
            Set(key, r.X + "," + r.Y + "," + r.Width + "," + r.Height);
        }

        // 기본값 복원: 창 위치 기억값을 빼고 옵션(저장 방식·단축키·펜 설정 등)만 지운다
        public static void ResetOptions()
        {
            List<string> remove = new List<string>();
            foreach (string key in values.Keys)
            {
                if (Array.IndexOf(geometryKeys, key) < 0) remove.Add(key);
            }
            foreach (string key in remove) values.Remove(key);
            Save();
        }

        // 설정 내보내기: 지금 설정을 파일로 복사한다
        public static void ExportTo(string path)
        {
            Save();
            File.Copy(file, path, true);
        }

        // 설정 가져오기: 파일의 옵션으로 바꾼다 (창 위치 기억값은 그대로 둔다)
        public static void ImportFrom(string path)
        {
            Dictionary<string, string> keep = new Dictionary<string, string>();
            foreach (string key in geometryKeys)
            {
                string v;
                if (values.TryGetValue(key, out v)) keep[key] = v;
            }
            values.Clear();
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1);
            }
            foreach (KeyValuePair<string, string> kv in keep) values[kv.Key] = kv.Value;
            Save();
        }

        public static string SaveFolder
        {
            get { return Get("SaveFolder", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "몽캡쳐")); }
        }

        public static string FileNamePattern
        {
            get { return Get("FileNamePattern", "캡쳐_{yyyy}{MM}{dd}_{HH}{mm}{ss}"); }
        }

        public static string Format
        {
            get { return Get("Format", "png"); }
        }
    }
}
