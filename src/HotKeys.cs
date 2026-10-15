using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace MongCapture
{
    // 전역 단축키. 각 동작의 키는 설정 Hotkey_<동작> 에 저장한다 (예: Ctrl+Shift+A).
    static class HotKeys
    {
        public static readonly string[] Actions = { "region", "full", "window", "last", "clipboard", "scroll", "timer", "color", "ocr", "pin" };

        static readonly Dictionary<string, string> names = new Dictionary<string, string>
        {
            { "region", "영역 캡처" },
            { "full", "전체 화면 캡처" },
            { "window", "활성 창 캡처" },
            { "last", "이전 영역 재캡처" },
            { "clipboard", "영역 캡처 → 클립보드" },
            { "scroll", "스크롤 캡처" },
            { "timer", "타이머 캡처" },
            { "color", "색상 추출" },
            { "ocr", "문자 인식(OCR)" },
            { "pin", "고정 창 만들기" },
        };

        public static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>
        {
            { "region", "Ctrl+Shift+A" },
            { "full", "Ctrl+Shift+F" },
            { "window", "Ctrl+Shift+W" },
            { "last", "Ctrl+Shift+R" },
            { "clipboard", "Ctrl+Shift+C" },
            { "scroll", "Ctrl+Shift+S" },
            { "timer", "Ctrl+Shift+D" },
            { "color", "Ctrl+Shift+X" },
            { "ocr", "Ctrl+Shift+T" },
            { "pin", "Ctrl+Shift+P" },
        };

        // 단축키가 눌리면 동작 이름("region" 등)을 알려 준다
        public static event Action<string> Pressed;

        static Notifier notifier;

        public static string NameOf(string action)
        {
            return names[action];
        }

        public static string Text(string action)
        {
            return Settings.Get("Hotkey_" + action, Defaults[action]);
        }

        // 같은 키를 쓰는 다른 동작 이름을 돌려준다 (없으면 null)
        public static string ConflictWith(string action, string text)
        {
            foreach (string other in Actions)
            {
                if (other != action && string.Equals(Text(other), text, StringComparison.OrdinalIgnoreCase)) return other;
            }
            return null;
        }

        // 모든 단축키를 다시 등록한다. 실패한 동작 이름 목록을 돌려준다 (다른 프로그램이 쓰고 있는 키 등)
        public static List<string> RegisterAll()
        {
            if (notifier == null) notifier = new Notifier();
            List<string> failed = new List<string>();
            for (int i = 0; i < Actions.Length; i++) NativeMethods.UnregisterHotKey(notifier.Handle, i + 1);
            for (int i = 0; i < Actions.Length; i++)
            {
                uint mods, vk;
                if (!TryParse(Text(Actions[i]), out mods, out vk)
                    || !NativeMethods.RegisterHotKey(notifier.Handle, i + 1, mods | NativeMethods.MOD_NOREPEAT, vk))
                {
                    failed.Add(Actions[i]);
                }
            }
            return failed;
        }

        public static void UnregisterAll()
        {
            if (notifier == null) return;
            for (int i = 0; i < Actions.Length; i++) NativeMethods.UnregisterHotKey(notifier.Handle, i + 1);
        }

        public static bool TryParse(string text, out uint mods, out uint vk)
        {
            mods = 0;
            vk = 0;
            if (string.IsNullOrEmpty(text)) return false;
            string[] parts = text.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (i < parts.Length - 1)
                {
                    if (p == "Ctrl") mods |= NativeMethods.MOD_CONTROL;
                    else if (p == "Shift") mods |= NativeMethods.MOD_SHIFT;
                    else if (p == "Alt") mods |= NativeMethods.MOD_ALT;
                    else return false;
                }
                else
                {
                    Keys key;
                    if (!TryKey(p, out key)) return false;
                    vk = (uint)key;
                }
            }
            // 수식키(Ctrl·Shift·Alt) 없이 쓰는 키는 일반 입력과 겹치므로 막는다
            return mods != 0 && vk != 0;
        }

        static bool TryKey(string name, out Keys key)
        {
            if (name.Length == 1 && char.IsDigit(name[0])) name = "D" + name;
            return Enum.TryParse(name, true, out key) && key != Keys.None;
        }

        public static string Format(Keys key)
        {
            StringBuilder sb = new StringBuilder();
            if ((key & Keys.Control) != 0) sb.Append("Ctrl+");
            if ((key & Keys.Shift) != 0) sb.Append("Shift+");
            if ((key & Keys.Alt) != 0) sb.Append("Alt+");
            string name = (key & Keys.KeyCode).ToString();
            if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1])) name = name.Substring(1);
            sb.Append(name);
            return sb.ToString();
        }

        // 숨겨진 창으로 WM_HOTKEY 를 받는다
        sealed class Notifier : NativeWindow
        {
            public Notifier()
            {
                CreateHandle(new CreateParams());
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == NativeMethods.WM_HOTKEY)
                {
                    int id = m.WParam.ToInt32();
                    if (id >= 1 && id <= Actions.Length && Pressed != null) Pressed(Actions[id - 1]);
                }
                base.WndProc(ref m);
            }
        }
    }
}
