using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MongCapture
{
    static class Program
    {
        // 이미 실행 중인 몽캡쳐를 찾아 창을 띄우는 신호
        // (--data 로 설정 폴더를 따로 쓰는 시험 실행은 이름 끝에 폴더 표시를 붙여 일반 실행과 섞이지 않게 한다)
        static string instanceSuffix = "";
        public static string SignalCaption { get { return "MongCapture.Signal" + instanceSuffix; } }
        public const int WM_APP_OPEN = 0x8001;
        public const int WM_APP_EXIT = 0x8002;

        // 시작 인자
        //   --tray 창 없이 트레이에만 숨어서 시작 (Windows 시작 때 자동 실행하는 경우)
        //   --exit 실행 중인 몽캡쳐를 정상 종료
        // 시험용 인자
        //   --data <폴더> 설정 폴더, --pos x,y 창 위치, --nofocus 포커스를 가져가지 않음, --edit <파일> 편집기로 열기,
        //   --area x,y,w,h 선택 창을 이 범위에만 띄움, --settings 설정 창 열기, --history 최근 캡처 열기,
        //   --answer-folder <폴더> 첫 실행 폴더 질문에 대신 답함
        public static bool NoFocus;
        public static bool StartHidden;
        public static bool HasPos;
        public static Point Pos;
        public static string EditFile;
        public static bool HasArea;
        public static Rectangle Area;
        public static bool OpenSettings;
        public static bool OpenHistory;
        public static string AnswerFolder;

        [STAThread]
        static void Main(string[] args)
        {
            bool exitRequest = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--nofocus") NoFocus = true;
                else if (args[i] == "--tray") StartHidden = true;
                else if (args[i] == "--exit") exitRequest = true;
                else if (args[i] == "--data" && i + 1 < args.Length)
                {
                    Settings.UseDirectory(args[++i]);
                    int hash = 17;
                    foreach (char c in args[i].ToLowerInvariant()) hash = hash * 31 + c;
                    instanceSuffix = "." + Math.Abs(hash);
                }
                else if (args[i] == "--edit" && i + 1 < args.Length) EditFile = args[++i];
                else if (args[i] == "--settings") OpenSettings = true;
                else if (args[i] == "--history") OpenHistory = true;
                else if (args[i] == "--answer-folder" && i + 1 < args.Length) AnswerFolder = args[++i];
                else if (args[i] == "--area" && i + 1 < args.Length)
                {
                    string[] a = args[++i].Split(',');
                    int ax, ay, aw, ah;
                    if (a.Length == 4 && int.TryParse(a[0], out ax) && int.TryParse(a[1], out ay) && int.TryParse(a[2], out aw) && int.TryParse(a[3], out ah))
                    {
                        HasArea = true;
                        Area = new Rectangle(ax, ay, aw, ah);
                    }
                }
                else if (args[i] == "--pos" && i + 1 < args.Length)
                {
                    string[] p = args[++i].Split(',');
                    int x, y;
                    if (p.Length == 2 && int.TryParse(p[0], out x) && int.TryParse(p[1], out y)) { HasPos = true; Pos = new Point(x, y); }
                }
            }

            // --exit: 이미 실행 중인 몽캡쳐를 정상 종료시킨다 (강제 종료 대신)
            if (exitRequest)
            {
                IntPtr running = NativeMethods.FindWindow(null, SignalCaption);
                if (running != IntPtr.Zero) NativeMethods.PostMessage(running, (uint)WM_APP_EXIT, IntPtr.Zero, IntPtr.Zero);
                return;
            }

            bool created;
            using (Mutex mutex = new Mutex(true, "MongCapture.SingleInstance" + instanceSuffix, out created))
            {
                if (!created)
                {
                    IntPtr h = NativeMethods.FindWindow(null, SignalCaption);
                    if (h != IntPtr.Zero) NativeMethods.PostMessage(h, (uint)WM_APP_OPEN, IntPtr.Zero, IntPtr.Zero);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Settings.Load();
                Application.Run(new TrayApp());
            }
        }
    }

    // 두 번째 실행이 보낸 신호를 받는 숨김 창
    sealed class SignalWindow : NativeWindow
    {
        public event Action OpenRequested;
        public event Action ExitRequested;

        public SignalWindow()
        {
            CreateHandle(new CreateParams { Caption = Program.SignalCaption });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Program.WM_APP_OPEN && OpenRequested != null) OpenRequested();
            if (m.Msg == Program.WM_APP_EXIT && ExitRequested != null) ExitRequested();
            base.WndProc(ref m);
        }
    }

    // 트레이에 상주하며 단축키·캡처 흐름을 맡는다
    sealed class TrayApp : ApplicationContext
    {
        readonly HubForm hub;
        readonly NotifyIcon tray;
        readonly SignalWindow signal;
        HistoryForm historyForm;
        bool busy;
        bool exiting;                   // 종료 중 (이때는 메뉴·창을 다시 만들지 않는다)
        string lastSavedPath;           // 마지막으로 자동 저장한 파일 (알림을 누르면 편집기로 연다)

        public TrayApp()
        {
            MongUpdate.UpdateService.CleanupOld();

            hub = new HubForm(this);
            signal = new SignalWindow();
            signal.OpenRequested += delegate { ShowHub(); };
            signal.ExitRequested += delegate { Exit(); };
            HotKeys.Pressed += Capture;

            tray = new NotifyIcon
            {
                Icon = Util.CreateTrayIcon(),
                Text = "몽캡쳐 " + Util.VersionText,
                Visible = true,
            };
            tray.DoubleClick += delegate { ShowHub(); };
            tray.BalloonTipClicked += delegate { if (lastSavedPath != null) OpenInEditor(lastSavedPath); };
            RefreshMenus();

            ApplyHotKeys(null);

            if (Program.StartHidden)
            {
                // 창 없이 트레이에만 시작: 창 핸들만 만들어 두고, 첫 실행 질문·업데이트 확인은 그대로 한다
                IntPtr unused = hub.Handle;
                hub.BeginInvoke(new Action(delegate
                {
                    AskSaveFolderIfNeeded(hub);
                    MongUpdate.UpdateUi.CheckAtStartup(hub, "mongcapture", "몽캡쳐", Util.VersionNumber, null);
                }));
            }
            else
            {
                ShowHub();
            }

            if (Program.EditFile != null) OpenInEditor(Program.EditFile);
            if (Program.OpenSettings) hub.BeginInvoke(new Action(delegate { ShowSettings(hub); }));
            if (Program.OpenHistory) hub.BeginInvoke(new Action(delegate { ShowHistory(); }));
        }

        public void ShowHub()
        {
            if (!hub.Visible) hub.Show();
            if (hub.WindowState == FormWindowState.Minimized) hub.WindowState = FormWindowState.Normal;
            if (!Program.NoFocus) hub.Activate();
        }

        // 트레이 메뉴와 창의 단축키 표시를 설정에 맞춰 다시 만든다
        public void RefreshMenus()
        {
            if (exiting) return;
            ContextMenuStrip menu = new ContextMenuStrip();
            foreach (string action in HotKeys.Actions)
            {
                string a = action;
                menu.Items.Add(HotKeys.NameOf(action) + "\t" + HotKeys.Text(action), null, delegate { Capture(a); });
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("최근 캡처\tCtrl+H", null, delegate { ShowHistory(); });
            menu.Items.Add("열기", null, delegate { ShowHub(); });
            menu.Items.Add("설정...", null, delegate { ShowSettings(hub); });
            menu.Items.Add("업데이트 확인", null, delegate { MongUpdate.UpdateUi.CheckNow(hub, "mongcapture", "몽캡쳐", Util.VersionNumber, null); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("종료", null, delegate { Exit(); });

            ContextMenuStrip old = tray.ContextMenuStrip;
            tray.ContextMenuStrip = menu;
            if (old != null) old.Dispose();
            hub.RefreshText();
        }

        // 단축키를 다시 등록한다. 실패가 있으면 false (설정 창에서 이전 키로 되돌리는 데 쓴다)
        public bool ApplyHotKeys(Form owner)
        {
            List<string> failed = HotKeys.RegisterAll();
            hub.RefreshText();
            RefreshMenus();
            if (failed.Count == 0) return true;

            List<string> names = new List<string>();
            foreach (string a in failed) names.Add(HotKeys.NameOf(a) + " (" + HotKeys.Text(a) + ")");
            string message = "다른 프로그램이 쓰고 있어 등록하지 못한 단축키가 있습니다:\n" + string.Join("\n", names);
            if (owner != null) MessageBox.Show(owner, message, "몽캡쳐", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else tray.ShowBalloonTip(3000, "몽캡쳐", message, ToolTipIcon.Warning);
            return false;
        }

        public void ShowSettings(Form owner)
        {
            using (SettingsForm form = new SettingsForm(this))
            {
                form.ShowDialog(owner);
            }
            RefreshMenus();
        }

        public void ShowHistory()
        {
            if (historyForm == null || historyForm.IsDisposed) historyForm = new HistoryForm(this);
            else historyForm.Reload();
            if (!historyForm.Visible) historyForm.Show();
            if (historyForm.WindowState == FormWindowState.Minimized) historyForm.WindowState = FormWindowState.Normal;
            if (!Program.NoFocus) historyForm.Activate();
        }

        // 타이머 캡처 시간(초)을 바꾸고 바로 시작한다
        public void StartTimer(int seconds)
        {
            Settings.SetInt("TimerSeconds", seconds);
            Capture("timer");
        }

        // 그림 파일을 편집기로 연다 (파일을 잠그지 않도록 복사본을 쓴다)
        public void OpenInEditor(string path)
        {
            try
            {
                using (Bitmap src = new Bitmap(path))
                    new EditorForm(new Bitmap(src), path).Show();
            }
            catch (Exception ex)
            {
                ShowMessage("그림을 열지 못했습니다.\n" + ex.Message);
            }
        }

        public void OpenBitmapInEditor(Bitmap bmp)
        {
            new EditorForm(bmp, null).Show();
        }

        public void OpenImageDialog(Form owner)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "편집할 그림 열기";
                dlg.Filter = "그림 파일|*.png;*.jpg;*.jpeg;*.bmp;*.gif|모든 파일|*.*";
                try { Directory.CreateDirectory(Settings.SaveFolder); dlg.InitialDirectory = Settings.SaveFolder; } catch { }
                if (dlg.ShowDialog(owner) == DialogResult.OK) OpenInEditor(dlg.FileName);
            }
        }

        // 처음 실행하면 저장 폴더를 묻는다. 취소하면 기본 폴더(사진\몽캡쳐)를 쓰고, 설정에서 언제든 바꿀 수 있다.
        public void AskSaveFolderIfNeeded(Form owner)
        {
            if (Settings.Has("SaveFolder")) return;
            string chosen = Program.AnswerFolder;
            if (chosen == null)
            {
                using (FolderBrowserDialog dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "캡처를 자동으로 저장할 폴더를 골라 주세요.\n(나중에 [도구 → 설정]에서 바꿀 수 있습니다.)";
                    dlg.SelectedPath = Settings.SaveFolder;
                    dlg.ShowNewFolderButton = true;
                    chosen = dlg.ShowDialog(owner) == DialogResult.OK ? dlg.SelectedPath : Settings.SaveFolder;
                }
            }
            try { Directory.CreateDirectory(chosen); } catch { }
            Settings.Set("SaveFolder", chosen);
        }

        // ───────── 캡처 ─────────

        // 선택 창을 띄워 영역(또는 창)을 고른다. 취소하면 null. 고른 화면 범위는 screenRect 로 돌려준다.
        Bitmap Pick(SelectMode mode, out Rectangle screenRect)
        {
            return Pick(mode, out screenRect, null);
        }

        Bitmap Pick(SelectMode mode, out Rectangle screenRect, string hint)
        {
            Rectangle virt = Program.HasArea ? Program.Area : CaptureService.VirtualScreen;
            screenRect = Rectangle.Empty;
            using (Bitmap background = CaptureService.CopyScreen(virt))
            using (SelectionForm sel = new SelectionForm(background, virt, mode, hint))
            {
                if (sel.ShowDialog() != DialogResult.OK) return null;
                screenRect = sel.Selection;
                return CaptureService.Crop(background, virt, sel.Selection);
            }
        }

        // 카운트다운이 끝나면 true, 사용자가 클릭해서 취소하면 false
        bool Countdown()
        {
            using (CountdownForm f = new CountdownForm(Math.Max(1, Settings.GetInt("TimerSeconds", 5))))
            {
                f.Show();
                while (!f.Done)
                {
                    Application.DoEvents();
                    Thread.Sleep(30);
                }
                bool cancelled = f.Cancelled;
                f.Close();
                return !cancelled;
            }
        }

        // 캡처 한 번: 범위를 고른 뒤 설정에 따라 처리한다. 모든 캡처는 클립보드에도 복사한다.
        public void Capture(string mode)
        {
            if (busy) return;
            busy = true;
            bool hubWasVisible = hub.Visible;
            try
            {
                if (mode == "timer")
                {
                    if (!Countdown()) return;
                    Thread.Sleep(150);
                    mode = "region";
                }

                if (hubWasVisible)
                {
                    hub.Hide();
                    Thread.Sleep(200);
                }

                Bitmap bmp = null;
                bool clipboardOnly = false;
                Rectangle rect;
                string note = null;         // 캡처 끝에 알려 줄 안내 (스크롤 캡처가 끝까지 못 간 이유 등)
                switch (mode)
                {
                    case "full":
                        bmp = CaptureService.CopyScreen(CaptureService.FullScreenRect());
                        break;

                    case "window":
                    case "region":
                    case "clipboard":
                        clipboardOnly = mode == "clipboard";
                        bmp = Pick(mode == "window" ? SelectMode.Window : SelectMode.Region, out rect);
                        if (bmp != null) Settings.SetRect("LastRegion", rect);
                        break;

                    case "last":
                        {
                            Rectangle last = Settings.GetRect("LastRegion");
                            if (last.IsEmpty)
                            {
                                ShowMessage("이전 영역이 없습니다. 영역 캡처를 먼저 해 주세요.");
                                return;
                            }
                            bmp = CaptureService.CopyScreen(last);
                            break;
                        }

                    case "scroll":
                        {
                            using (Bitmap picked = Pick(SelectMode.Region, out rect,
                                "스크롤 캡처: 캡처를 시작할 위치에서 스크롤되는 부분의 폭만큼 드래그하세요. 세로 길이는 상관없습니다.\n마우스를 놓으면 창 아래 끝까지 자동으로 스크롤하며 캡처합니다. 고정된 사이드바·스크롤바는 폭에서 빼 주세요. (Esc: 취소)"))
                            {
                                if (picked == null) return;
                            }
                            Balloon("스크롤 캡처 중입니다. 끝까지 내려가면 자동으로 끝나고, Esc 를 누르면 지금까지 것으로 마칩니다.");
                            Thread.Sleep(400);
                            bmp = ScrollCapture.Capture(rect, out note);
                            break;
                        }

                    case "color":
                        PickColor();
                        return;

                    case "ocr":
                        {
                            Bitmap picked = Pick(SelectMode.Region, out rect);
                            if (picked == null) return;
                            using (picked) RunOcr(picked);
                            return;
                        }

                    case "pin":
                        {
                            Bitmap picked = Pick(SelectMode.Region, out rect);
                            if (picked == null) return;
                            bool pinCopied = ImageFile.CopyToClipboard(picked);
                            new PinForm(picked, rect.Location, OpenBitmapInEditor).Show();
                            Balloon("고정 창을 만들었습니다. 끌어서 옮기고, 우클릭 메뉴로 투명도·크기를 바꿉니다." + (pinCopied ? " (클립보드에도 복사됨)" : " (클립보드 복사는 실패했습니다)"));
                            return;
                        }
                }

                if (bmp == null) return;
                AfterCapture(bmp, clipboardOnly);
                if (note != null) ShowMessage(note);
            }
            catch (Exception ex)
            {
                ShowMessage("캡처하지 못했습니다.\n" + ex.Message);
            }
            finally
            {
                busy = false;
                if (hubWasVisible) ShowHub();
            }
        }

        // 클릭한 지점의 색을 #RRGGBB 로 클립보드에 복사한다
        void PickColor()
        {
            Rectangle virt = Program.HasArea ? Program.Area : CaptureService.VirtualScreen;
            using (Bitmap background = CaptureService.CopyScreen(virt, false))
            using (SelectionForm sel = new SelectionForm(background, virt, SelectMode.Color))
            {
                if (sel.ShowDialog() != DialogResult.OK) return;
                Color c = background.GetPixel(sel.Selection.X - virt.X, sel.Selection.Y - virt.Y);
                string hex = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
                ImageFile.CopyToClipboard(null, hex);
                Balloon("색 " + hex + " (RGB " + c.R + ", " + c.G + ", " + c.B + ") 를 클립보드에 복사했습니다.");
            }
        }

        // 고른 영역의 글자를 읽어 클립보드에 글자로만 복사한다 (그림은 넣지 않는다: 그림을 붙여 넣는 곳에 그림이 붙지 않게)
        void RunOcr(Bitmap bmp)
        {
            string text;
            try
            {
                text = Ocr.Recognize(bmp);
            }
            catch (Exception ex)
            {
                ShowMessage("문자 인식에 실패했습니다.\n" + ex.Message);
                return;
            }
            if (text == null)
            {
                ShowMessage("이 PC에서는 문자 인식을 쓸 수 없습니다.\n(Windows 설정 → 시간 및 언어 → 언어에서 문자 인식을 지원하는 언어를 추가해 주세요.)");
                return;
            }
            string trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                Balloon("글자를 찾지 못했습니다. 글자가 선명한 부분만 골라 다시 해 보세요. (클립보드는 그대로입니다)");
                return;
            }
            if (!ImageFile.CopyToClipboard(null, trimmed))
            {
                ShowMessage("글자를 읽었지만 클립보드에 복사하지 못했습니다. 다른 프로그램이 클립보드를 쓰고 있을 수 있습니다.");
                return;
            }
            string preview = trimmed.Replace("\r", " ").Replace("\n", " ");
            if (preview.Length > 40) preview = preview.Substring(0, 40) + "...";
            Balloon("글자 " + trimmed.Length + "자를 클립보드에 복사했습니다.\n" + preview);
        }

        void AfterCapture(Bitmap bmp, bool clipboardOnly)
        {
            // 모든 캡처는 클립보드에도 복사한다
            bool copied = ImageFile.CopyToClipboard(bmp);

            if (clipboardOnly)
            {
                bmp.Dispose();
                Balloon(copied ? "클립보드에 복사했습니다." : "클립보드에 복사하지 못했습니다.");
                return;
            }

            string savedPath = null;
            if (Settings.GetBool("AfterSave", true))
            {
                try
                {
                    savedPath = ImageFile.MakePath(Settings.SaveFolder, Settings.FileNamePattern, Settings.Format);
                    ImageFile.Save(bmp, savedPath, Settings.Format);
                    History.Add(savedPath);
                }
                catch (Exception ex)
                {
                    savedPath = null;
                    ShowMessage("파일로 저장하지 못했습니다.\n" + ex.Message);
                }
            }

            if (Settings.GetBool("AfterEditor", false))
            {
                // 편집기가 이미지를 쓰므로 여기서 해제하지 않는다
                new EditorForm(bmp, savedPath).Show();
                return;
            }

            bmp.Dispose();
            lastSavedPath = savedPath;
            if (savedPath != null) Balloon("저장했습니다: " + savedPath + (copied ? "\n(클립보드에도 복사됨, 누르면 편집기로 엽니다)" : "\n(클립보드 복사는 실패했습니다. 누르면 편집기로 엽니다)"));
            else Balloon(copied ? "클립보드에 복사했습니다." : "캡처했습니다.");
        }

        void Balloon(string message)
        {
            hub.SetMessage(message);
            tray.ShowBalloonTip(2500, "몽캡쳐", message, ToolTipIcon.Info);
        }

        void ShowMessage(string message)
        {
            MessageBox.Show(hub, message, "몽캡쳐", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public void Exit()
        {
            if (exiting) return;
            exiting = true;
            hub.ExitRequested = true;
            HotKeys.UnregisterAll();
            tray.Visible = false;
            tray.Dispose();
            hub.Dispose();
            ExitThread();

            // 설정 창 같은 대화상자가 열려 있으면 메시지 루프가 바로 끝나지 않을 수 있다.
            // 정리는 끝났으니, 잠시 뒤에도 남아 있으면 프로세스를 마친다 (트레이 아이콘 없이 남는 것을 막는다).
            System.Windows.Forms.Timer failsafe = new System.Windows.Forms.Timer { Interval = 2000 };
            failsafe.Tick += delegate { Environment.Exit(0); };
            failsafe.Start();
        }
    }
}
