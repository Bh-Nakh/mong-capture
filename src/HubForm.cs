using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MongCapture
{
    // 메인 창: 캡처 명령과 단축키 목록. 창을 닫으면 트레이로 숨는다. 그림 파일을 끌어다 놓으면 편집기로 연다.
    sealed class HubForm : Form
    {
        static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

        readonly TrayApp app;
        readonly ListView list;
        readonly Label title;
        readonly Label hint;
        readonly ToolStripStatusLabel stMsg;

        public bool ExitRequested;

        public HubForm(TrayApp app)
        {
            this.app = app;
            Text = "몽캡쳐";
            Font = SystemFonts.MessageBoxFont;
            Icon = Util.AppIcon;
            MinimumSize = new Size(400, 380);
            AllowDrop = true;

            Rectangle saved = Settings.GetRect("HubBounds");
            if (Program.HasPos)
            {
                StartPosition = FormStartPosition.Manual;
                Size = new Size(480, 500);
                Location = Program.Pos;
            }
            else if (!saved.IsEmpty && OnScreen(saved))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = saved;
            }
            else
            {
                StartPosition = FormStartPosition.CenterScreen;
                Size = new Size(480, 500);
            }

            MenuStrip menu = BuildMenu();
            MainMenuStrip = menu;

            Font big = new Font(SystemFonts.MessageBoxFont.FontFamily, 10f);
            title = new Label { Dock = DockStyle.Top, Height = 36, Text = "캡처 단축키", Padding = new Padding(16, 12, 0, 0), Font = big };
            list = new ListView
            {
                Dock = DockStyle.Top,
                Height = 270,
                View = View.Details,
                HeaderStyle = ColumnHeaderStyle.None,
                FullRowSelect = true,
                MultiSelect = false,
                BorderStyle = BorderStyle.None,
                Font = big,
            };
            list.Columns.Add("동작", 250);
            list.Columns.Add("단축키", 170);
            // 목록은 보여 주기만 한다 (고르거나 바꾸는 곳은 설정 창)
            list.ItemSelectionChanged += delegate(object s, ListViewItemSelectionChangedEventArgs e) { if (e.IsSelected) e.Item.Selected = false; };
            hint = new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16, 8, 16, 0),
                Text = "단축키는 [도구 → 설정]에서 바꿀 수 있습니다.\n그림 파일을 이 창에 끌어다 놓으면 편집기로 엽니다.\n창을 닫으면 트레이로 숨고, 트레이 아이콘을 두 번 누르면 다시 열립니다.",
            };

            StatusStrip status = new StatusStrip();
            stMsg = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            status.Items.Add(stMsg);
            status.Items.Add(new ToolStripStatusLabel(Util.VersionText));

            // 도킹 순서: 본문 → 하단 → 메뉴 (맨 위가 메뉴)
            Controls.Add(hint);
            Controls.Add(list);
            Controls.Add(title);
            Controls.Add(status);
            Controls.Add(menu);

            RefreshText();

            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (!ExitRequested && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                }
            };
            Move += delegate { SaveBounds(); };
            Resize += delegate { SaveBounds(); };
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;

            Shown += delegate
            {
                // 처음 실행이면 저장 폴더부터 묻고, 그다음 업데이트를 확인한다
                if (!Program.NoFocus || Program.AnswerFolder != null) app.AskSaveFolderIfNeeded(this);
                if (!Program.NoFocus) MongUpdate.UpdateUi.CheckAtStartup(this, "mongcapture", "몽캡쳐", Util.VersionNumber, null);
            };
        }

        protected override bool ShowWithoutActivation
        {
            get { return Program.NoFocus; }
        }

        MenuStrip BuildMenu()
        {
            MenuStrip ms = new MenuStrip();

            ToolStripMenuItem file = new ToolStripMenuItem("파일(&F)");
            ToolStripMenuItem openImage = new ToolStripMenuItem("이미지 열기(&O)...", null, delegate { app.OpenImageDialog(this); });
            openImage.ShortcutKeys = Keys.Control | Keys.O;
            file.DropDownItems.Add(openImage);
            file.DropDownItems.Add(new ToolStripMenuItem("저장 폴더 열기(&D)", null, delegate { OpenSaveFolder(); }));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(new ToolStripMenuItem("트레이로 숨기기(&H)", null, delegate { Hide(); }));
            file.DropDownItems.Add(new ToolStripMenuItem("종료(&X)", null, delegate { app.Exit(); }));

            ToolStripMenuItem capture = new ToolStripMenuItem("캡쳐(&C)");
            foreach (string action in HotKeys.Actions)
            {
                string a = action;
                capture.DropDownItems.Add(new ToolStripMenuItem(HotKeys.NameOf(action) + "\t" + HotKeys.Text(action), null, delegate { app.Capture(a); }));
            }
            capture.DropDownItems.Add(new ToolStripSeparator());
            ToolStripMenuItem timer = new ToolStripMenuItem("타이머로 시작(&T)");
            foreach (int sec in new int[] { 3, 5, 10 })
            {
                int s = sec;
                timer.DropDownItems.Add(new ToolStripMenuItem(s + "초 뒤에 영역 캡처", null, delegate { app.StartTimer(s); }));
            }
            capture.DropDownItems.Add(timer);

            ToolStripMenuItem view = new ToolStripMenuItem("보기(&V)");
            ToolStripMenuItem history = new ToolStripMenuItem("최근 캡처(&H)", null, delegate { app.ShowHistory(); });
            history.ShortcutKeys = Keys.Control | Keys.H;
            view.DropDownItems.Add(history);

            ToolStripMenuItem tools = new ToolStripMenuItem("도구(&T)");
            tools.DropDownItems.Add(new ToolStripMenuItem("설정(&S)...", null, delegate { app.ShowSettings(this); }));

            ToolStripMenuItem help = new ToolStripMenuItem("도움말(&H)");
            help.DropDownItems.Add(new ToolStripMenuItem("업데이트 확인(&U)", null, delegate { MongUpdate.UpdateUi.CheckNow(this, "mongcapture", "몽캡쳐", Util.VersionNumber, null); }));
            help.DropDownItems.Add(new ToolStripSeparator());
            help.DropDownItems.Add(new ToolStripMenuItem("버전 정보(&A)", null, delegate { ShowAbout(); }));

            ms.Items.AddRange(new ToolStripItem[] { file, capture, view, tools, help });
            return ms;
        }

        // 단축키 목록 등 화면 내용을 설정에 맞춰 다시 그린다
        public void RefreshText()
        {
            list.Items.Clear();
            foreach (string action in HotKeys.Actions)
                list.Items.Add(new ListViewItem(new string[] { HotKeys.NameOf(action), HotKeys.Text(action) }));
        }

        public void SetMessage(string message)
        {
            stMsg.Text = message;
        }

        // 그림 파일을 끌어다 놓으면 편집기로 연다
        void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DropFiles(e).Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        }

        void OnDragDrop(object sender, DragEventArgs e)
        {
            foreach (string path in DropFiles(e)) app.OpenInEditor(path);
        }

        static string[] DropFiles(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return new string[0];
            string[] all = (string[])e.Data.GetData(DataFormats.FileDrop);
            System.Collections.Generic.List<string> images = new System.Collections.Generic.List<string>();
            foreach (string p in all)
            {
                if (Array.IndexOf(ImageExtensions, Path.GetExtension(p).ToLowerInvariant()) >= 0) images.Add(p);
            }
            return images.ToArray();
        }

        void OpenSaveFolder()
        {
            try
            {
                Directory.CreateDirectory(Settings.SaveFolder);
                System.Diagnostics.Process.Start(Settings.SaveFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "폴더를 열지 못했습니다.\n" + ex.Message, "몽캡쳐", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void ShowAbout()
        {
            MessageBox.Show(this, "몽캡쳐 " + Util.VersionText + "\n\n화면 캡처 도구 (몽 시리즈)", "버전 정보", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void SaveBounds()
        {
            if (!IsHandleCreated || !Visible || WindowState != FormWindowState.Normal) return;
            Settings.SetRect("HubBounds", Bounds);
        }

        static bool OnScreen(Rectangle r)
        {
            foreach (Screen s in Screen.AllScreens)
            {
                if (s.WorkingArea.IntersectsWith(r)) return true;
            }
            return false;
        }
    }
}
