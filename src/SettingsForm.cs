using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MongCapture
{
    // 설정 창 (저장 · 캡처 · 단축키 · 기타 탭). 값을 바꾸는 즉시 저장한다 (공통 규칙 12).
    // 단축키는 입력한 키를 바로 등록하고, 실패하면 이전 키로 되돌린다.
    sealed class SettingsForm : Form
    {
        readonly TrayApp app;
        readonly Dictionary<string, TextBox> hotKeyBoxes = new Dictionary<string, TextBox>();
        TabControl tabs;
        TextBox txtFolder, txtPattern;
        ComboBox cmbFormat, cmbTimer;
        NumericUpDown numQuality;
        CheckBox chkEditor, chkSave, chkCursor, chkShadow, chkLoupe, chkAutoStart;
        RadioButton radPrimary, radAll;
        bool loading;

        public SettingsForm(TrayApp app)
        {
            this.app = app;
            Text = "몽캡쳐 설정";
            Font = SystemFonts.MessageBoxFont;
            Icon = Util.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(560, 600);
            AutoScaleMode = AutoScaleMode.Font;

            tabs = new TabControl { Location = new Point(8, 8), Size = new Size(544, 540) };
            Controls.Add(tabs);
            BuildSaveTab();
            BuildCaptureTab();
            BuildHotKeyTab();
            BuildEtcTab();

            Button close = new Button { Text = "닫기", Location = new Point(ClientSize.Width - 112, 558), Size = new Size(100, 30), DialogResult = DialogResult.OK };
            Controls.Add(close);
            AcceptButton = close;
            CancelButton = close;

            LoadValues();
        }

        TabPage AddTab(string title)
        {
            TabPage page = new TabPage(title) { UseVisualStyleBackColor = true };
            tabs.TabPages.Add(page);
            return page;
        }

        // ───────── 저장 ─────────

        void BuildSaveTab()
        {
            TabPage t = AddTab("저장");
            GroupBox g = Group(t, "저장 위치와 이름", 10, 170);
            g.Controls.Add(Caption("저장 폴더", 14, 30));
            txtFolder = new TextBox { Location = new Point(110, 27), Width = 300 };
            txtFolder.Leave += delegate { if (txtFolder.Text.Trim().Length > 0) Settings.Set("SaveFolder", txtFolder.Text.Trim()); };
            g.Controls.Add(txtFolder);
            Button browse = new Button { Text = "찾기...", Location = new Point(416, 26), Size = new Size(96, 26) };
            browse.Click += delegate { BrowseFolder(); };
            g.Controls.Add(browse);

            g.Controls.Add(Caption("파일 이름", 14, 66));
            txtPattern = new TextBox { Location = new Point(110, 63), Width = 402 };
            txtPattern.Leave += delegate { Settings.Set("FileNamePattern", txtPattern.Text.Trim()); };
            g.Controls.Add(txtPattern);
            g.Controls.Add(Caption("토큰: {yyyy} {MM} {dd} {HH} {mm} {ss} 날짜·시간, {N} 순번(001, 002...)", 110, 92, 9f, Color.DimGray));

            g.Controls.Add(Caption("형식", 14, 126));
            cmbFormat = new ComboBox { Location = new Point(110, 123), Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbFormat.Items.AddRange(new object[] { "PNG", "JPG", "BMP", "GIF" });
            cmbFormat.SelectedIndexChanged += delegate
            {
                if (loading) return;
                Settings.Set("Format", cmbFormat.SelectedItem.ToString().ToLowerInvariant());
                UpdateJpgEnabled();
            };
            g.Controls.Add(cmbFormat);
            g.Controls.Add(Caption("JPG 품질", 230, 126));
            numQuality = new NumericUpDown { Location = new Point(310, 123), Width = 70, Minimum = 1, Maximum = 100 };
            numQuality.ValueChanged += delegate
            {
                if (!loading) Settings.SetInt("JpgQuality", (int)numQuality.Value);
            };
            g.Controls.Add(numQuality);

            GroupBox a = Group(t, "캡처 후 동작", 190, 120);
            chkSave = new CheckBox { Text = "자동 저장", Location = new Point(14, 28), AutoSize = true };
            chkEditor = new CheckBox { Text = "편집기 열기", Location = new Point(150, 28), AutoSize = true };
            chkSave.CheckedChanged += delegate { SaveAfter(); };
            chkEditor.CheckedChanged += delegate { SaveAfter(); };
            a.Controls.Add(chkSave);
            a.Controls.Add(chkEditor);
            a.Controls.Add(Caption("모든 캡처는 클립보드에도 자동으로 복사됩니다.", 14, 62, 9f, Color.DimGray));
            a.Controls.Add(Caption("둘 다 끄면 클립보드에만 복사합니다.", 14, 84, 9f, Color.DimGray));
        }

        // ───────── 캡처 ─────────

        void BuildCaptureTab()
        {
            TabPage t = AddTab("캡처");
            GroupBox g = Group(t, "전체 화면 캡처 범위", 10, 60);
            radPrimary = new RadioButton { Text = "주 모니터", Location = new Point(14, 26), AutoSize = true };
            radAll = new RadioButton { Text = "모든 모니터", Location = new Point(150, 26), AutoSize = true };
            radPrimary.CheckedChanged += delegate { if (radPrimary.Checked && !loading) Settings.Set("FullScope", "primary"); };
            radAll.CheckedChanged += delegate { if (radAll.Checked && !loading) Settings.Set("FullScope", "all"); };
            g.Controls.Add(radPrimary);
            g.Controls.Add(radAll);

            GroupBox o = Group(t, "캡처 옵션", 80, 150);
            chkCursor = new CheckBox { Text = "마우스 커서를 그림에 포함", Location = new Point(14, 28), AutoSize = true };
            chkShadow = new CheckBox { Text = "창 캡처에 창 그림자(바깥 테두리) 포함", Location = new Point(14, 58), AutoSize = true };
            chkLoupe = new CheckBox { Text = "영역 선택 때 돋보기(확대·좌표·색) 표시", Location = new Point(14, 88), AutoSize = true };
            chkCursor.CheckedChanged += delegate { if (!loading) Settings.SetBool("IncludeCursor", chkCursor.Checked); };
            chkShadow.CheckedChanged += delegate { if (!loading) Settings.SetBool("IncludeShadow", chkShadow.Checked); };
            chkLoupe.CheckedChanged += delegate { if (!loading) Settings.SetBool("Magnifier", chkLoupe.Checked); };
            o.Controls.Add(chkCursor);
            o.Controls.Add(chkShadow);
            o.Controls.Add(chkLoupe);

            GroupBox tm = Group(t, "타이머 캡처", 240, 80);
            tm.Controls.Add(Caption("카운트다운", 14, 34));
            cmbTimer = new ComboBox { Location = new Point(100, 31), Width = 80, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbTimer.Items.AddRange(new object[] { "3초", "5초", "10초" });
            cmbTimer.SelectedIndexChanged += delegate
            {
                if (loading || cmbTimer.SelectedIndex < 0) return;
                Settings.SetInt("TimerSeconds", new int[] { 3, 5, 10 }[cmbTimer.SelectedIndex]);
            };
            tm.Controls.Add(cmbTimer);
            tm.Controls.Add(Caption("시간이 지나면 영역 선택 화면이 뜹니다 (메뉴·팝업 캡처용)", 14, 56, 9f, Color.DimGray));
        }

        // ───────── 단축키 ─────────

        void BuildHotKeyTab()
        {
            TabPage t = AddTab("단축키");
            t.Controls.Add(Caption("입력칸을 누르고 키를 누르세요. Ctrl·Shift·Alt 중 하나 이상을 포함해야 합니다.", 14, 12, 9f, Color.DimGray));
            int row = 40;
            foreach (string action in HotKeys.Actions)
            {
                string a = action;
                t.Controls.Add(Caption(HotKeys.NameOf(action), 24, row + 4));
                TextBox box = new TextBox { Location = new Point(240, row), Width = 200 };
                box.KeyDown += delegate(object sender, KeyEventArgs e) { HotKeyKeyDown(a, box, e); };
                box.KeyPress += delegate(object sender, KeyPressEventArgs e) { e.Handled = true; };
                hotKeyBoxes[action] = box;
                t.Controls.Add(box);
                row += 36;
            }
        }

        // ───────── 기타 ─────────

        void BuildEtcTab()
        {
            TabPage t = AddTab("기타");
            GroupBox s = Group(t, "시작", 10, 90);
            chkAutoStart = new CheckBox { Text = "Windows 를 켤 때 자동으로 실행 (창 없이 트레이에서 시작)", Location = new Point(14, 30), AutoSize = true };
            chkAutoStart.CheckedChanged += delegate { if (!loading) Util.SetAutoStart(chkAutoStart.Checked); };
            s.Controls.Add(chkAutoStart);
            s.Controls.Add(Caption("지금 실행 중인 몽캡쳐의 위치가 등록됩니다. 폴더를 옮기면 다시 켜 주세요.", 14, 58, 9f, Color.DimGray));

            GroupBox b = Group(t, "설정 파일", 110, 130);
            Button export = new Button { Text = "설정 내보내기...", Location = new Point(14, 28), Size = new Size(160, 30) };
            Button import = new Button { Text = "설정 가져오기...", Location = new Point(184, 28), Size = new Size(160, 30) };
            Button reset = new Button { Text = "기본값 복원", Location = new Point(354, 28), Size = new Size(150, 30) };
            export.Click += delegate { ExportSettings(); };
            import.Click += delegate { ImportSettings(); };
            reset.Click += delegate { ResetToDefaults(); };
            b.Controls.Add(export);
            b.Controls.Add(import);
            b.Controls.Add(reset);
            b.Controls.Add(Caption("내보낸 파일을 다른 PC에서 가져오면 같은 설정으로 쓸 수 있습니다.", 14, 72, 9f, Color.DimGray));
            b.Controls.Add(Caption("설정 파일 위치: " + Settings.FolderPath, 14, 94, 9f, Color.DimGray));
        }

        // ───────── 도우미 ─────────

        GroupBox Group(Control parent, string title, int y, int height)
        {
            GroupBox g = new GroupBox { Text = title, Location = new Point(8, y), Size = new Size(520, height) };
            parent.Controls.Add(g);
            return g;
        }

        static Label Caption(string text, int x, int y)
        {
            return new Label { Text = text, Location = new Point(x, y), AutoSize = true };
        }

        static Label Caption(string text, int x, int y, float size, Color color)
        {
            Label l = new Label { Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = color };
            l.Font = new Font(SystemFonts.MessageBoxFont.FontFamily, size);
            return l;
        }

        // ───────── 값 읽기 · 쓰기 ─────────

        void LoadValues()
        {
            loading = true;
            txtFolder.Text = Settings.SaveFolder;
            txtPattern.Text = Settings.FileNamePattern;
            string fmt = Settings.Format;
            cmbFormat.SelectedIndex = fmt == "jpg" ? 1 : fmt == "bmp" ? 2 : fmt == "gif" ? 3 : 0;
            numQuality.Value = Math.Max(1, Math.Min(100, Settings.GetInt("JpgQuality", 90)));
            chkSave.Checked = Settings.GetBool("AfterSave", true);
            chkEditor.Checked = Settings.GetBool("AfterEditor", false);
            bool all = Settings.Get("FullScope", "primary") == "all";
            radAll.Checked = all;
            radPrimary.Checked = !all;
            chkCursor.Checked = Settings.GetBool("IncludeCursor", false);
            chkShadow.Checked = Settings.GetBool("IncludeShadow", false);
            chkLoupe.Checked = Settings.GetBool("Magnifier", true);
            int sec = Settings.GetInt("TimerSeconds", 5);
            cmbTimer.SelectedIndex = sec == 3 ? 0 : sec == 10 ? 2 : 1;
            foreach (string action in HotKeys.Actions) hotKeyBoxes[action].Text = HotKeys.Text(action);
            chkAutoStart.Checked = Util.GetAutoStart();
            loading = false;
            UpdateJpgEnabled();
        }

        void UpdateJpgEnabled()
        {
            numQuality.Enabled = cmbFormat.SelectedIndex == 1;
        }

        void SaveAfter()
        {
            if (loading) return;
            Settings.SetBool("AfterSave", chkSave.Checked);
            Settings.SetBool("AfterEditor", chkEditor.Checked);
        }

        void BrowseFolder()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog { Description = "캡처를 저장할 폴더", SelectedPath = Settings.SaveFolder })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                txtFolder.Text = dlg.SelectedPath;
                Settings.Set("SaveFolder", dlg.SelectedPath);
            }
        }

        // ───────── 단축키 ─────────

        void HotKeyKeyDown(string action, TextBox box, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
                return;
            if (!e.Control && !e.Shift && !e.Alt)
            {
                MessageBox.Show(this, "Ctrl, Shift, Alt 중 하나 이상을 함께 눌러 주세요.", "몽캡쳐", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string text = HotKeys.Format(e.Modifiers | (e.KeyCode & Keys.KeyCode));
            string previous = HotKeys.Text(action);
            string other = HotKeys.ConflictWith(action, text);
            if (other != null)
            {
                MessageBox.Show(this, "\"" + text + "\" 은 이미 \"" + HotKeys.NameOf(other) + "\" 에 쓰고 있습니다.", "몽캡쳐", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Settings.Set("Hotkey_" + action, text);
            if (!app.ApplyHotKeys(this))
            {
                // 다른 프로그램이 이 키를 쓰고 있어 등록하지 못했다 → 이전 키로 되돌린다
                Settings.Set("Hotkey_" + action, previous);
                app.ApplyHotKeys(this);
                box.Text = previous;
                return;
            }
            box.Text = text;
        }

        // ───────── 설정 파일 ─────────

        void ExportSettings()
        {
            using (SaveFileDialog dlg = new SaveFileDialog { Title = "설정 내보내기", Filter = "몽캡쳐 설정 (*.ini)|*.ini", FileName = "몽캡쳐설정.ini" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Settings.ExportTo(dlg.FileName);
                    MessageBox.Show(this, "설정을 내보냈습니다.\n" + dlg.FileName, "몽캡쳐", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "내보내지 못했습니다.\n" + ex.Message, "몽캡쳐", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        void ImportSettings()
        {
            using (OpenFileDialog dlg = new OpenFileDialog { Title = "설정 가져오기", Filter = "몽캡쳐 설정 (*.ini)|*.ini|모든 파일|*.*" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                if (MessageBox.Show(this, "지금 설정을 이 파일의 설정으로 바꿀까요?", "몽캡쳐", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try
                {
                    Settings.ImportFrom(dlg.FileName);
                    app.ApplyHotKeys(this);
                    LoadValues();
                    app.RefreshMenus();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "가져오지 못했습니다.\n" + ex.Message, "몽캡쳐", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        void ResetToDefaults()
        {
            if (MessageBox.Show(this, "모든 설정을 처음 상태로 되돌릴까요?\n(저장 폴더, 단축키, 펜 설정 등 옵션이 기본값이 됩니다.)", "몽캡쳐", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            Settings.ResetOptions();
            app.ApplyHotKeys(this);
            LoadValues();
            app.RefreshMenus();
        }
    }
}
