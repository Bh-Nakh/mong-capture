using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MongCapture
{
    static class Util
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        // Windows 시작 때 실행 항목의 이름 (시험할 때는 다른 이름으로 바꿔 쓴다)
        public static string RunValueName = "MongCapture";

        // 버전은 AssemblyInfo.cs 에서만 정한다. 화면 표시도 여기서 한 번에 가져간다.
        public static string VersionNumber
        {
            get { return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3); }
        }

        public static string VersionText
        {
            get { return "v" + VersionNumber; }
        }

        static Icon appIcon;

        // 앱 아이콘 (몽캡쳐 공식 아이콘, 16~256px 여러 크기가 들어 있다). 창은 이 아이콘에서 제목줄·작업표시줄에 맞는 크기를 골라 쓴다.
        public static Icon AppIcon
        {
            get
            {
                if (appIcon != null) return appIcon;
                try
                {
                    using (System.IO.Stream s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("MongCapture.app.ico"))
                    {
                        if (s != null) appIcon = new Icon(s);
                    }
                }
                catch { }
                if (appIcon == null)
                {
                    try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
                    catch { }
                }
                return appIcon;
            }
        }

        // 트레이용: 화면 배율에 맞는 작은 크기(보통 16px)를 새로 뽑아 쓴다
        public static Icon CreateTrayIcon()
        {
            Icon big = AppIcon;
            return big == null ? SystemIcons.Application : new Icon(big, SystemInformation.SmallIconSize);
        }

        // Windows 시작 때 자동 실행 (창 없이 트레이에만 숨어서 시작한다). 현재 사용자 레지스트리만 쓰고 관리자 권한은 필요 없다.
        public static bool GetAutoStart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(RunValueName) != null;
            }
            catch { return false; }
        }

        public static void SetAutoStart(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\" --tray");
                    else k.DeleteValue(RunValueName, false);
                }
            }
            catch { }
        }
    }
}
