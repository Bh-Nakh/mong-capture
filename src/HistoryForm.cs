using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace MongCapture
{
    // 최근 캡처 목록: 자동 저장한 그림을 작은 그림으로 보여 준다. 더블클릭하면 편집기로 연다.
    sealed class HistoryForm : Form
    {
        readonly TrayApp app;
        readonly ListView list;
        readonly ImageList images;
        readonly ToolStripStatusLabel stCount;

        public HistoryForm(TrayApp app)
        {
            this.app = app;
            Text = "최근 캡처";
            Font = SystemFonts.MessageBoxFont;
            Icon = Util.AppIcon;
            MinimumSize = new Size(420, 320);

            Rectangle saved = Settings.GetRect("HistoryBounds");
            if (!saved.IsEmpty && OnScreen(saved))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = saved;
            }
            else
            {
                StartPosition = FormStartPosition.CenterScreen;
                Size = new Size(760, 520);
            }
            if (Program.HasPos)
            {
                StartPosition = FormStartPosition.Manual;
                Location = Program.Pos;
            }

            images = new ImageList { ImageSize = new Size(128, 96), ColorDepth = ColorDepth.Depth32Bit };
            list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.LargeIcon,
                LargeImageList = images,
                MultiSelect = true,
                HideSelection = false,
            };
            list.DoubleClick += delegate { OpenSelected(); };
            list.KeyDown += OnListKeyDown;
            list.ContextMenuStrip = BuildMenu();

            StatusStrip status = new StatusStrip();
            stCount = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            status.Items.Add(stCount);
            status.Items.Add(new ToolStripStatusLabel(Util.VersionText));

            Controls.Add(list);
            Controls.Add(status);

            Move += delegate { SaveBounds(); };
            Resize += delegate { SaveBounds(); };
            Reload();
        }

        protected override bool ShowWithoutActivation
        {
            get { return Program.NoFocus; }
        }

        ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("편집기로 열기 (Enter)", null, delegate { OpenSelected(); });
            menu.Items.Add("클립보드로 복사 (Ctrl+C)", null, delegate { CopySelected(); });
            menu.Items.Add("폴더에서 보기", null, delegate { ShowInFolder(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("목록에서 제거 (Delete)", null, delegate { RemoveSelected(); });
            return menu;
        }

        // 목록을 다시 읽어 작은 그림과 함께 채운다
        public void Reload()
        {
            list.BeginUpdate();
            list.Items.Clear();
            images.Images.Clear();
            foreach (string path in History.Load())
            {
                try
                {
                    int index = images.Images.Count;
                    images.Images.Add(Thumbnail(path));
                    ListViewItem item = new ListViewItem(Path.GetFileName(path), index);
                    item.Tag = path;
                    item.ToolTipText = path + "\n" + File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss");
                    list.Items.Add(item);
                }
                catch { }
            }
            list.EndUpdate();
            stCount.Text = list.Items.Count + "개 (더블클릭: 편집기로 열기)";
        }

        static Image Thumbnail(string path)
        {
            Bitmap thumb = new Bitmap(128, 96);
            using (Image src = Image.FromFile(path))
            using (Graphics g = Graphics.FromImage(thumb))
            {
                g.Clear(Color.White);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                float s = Math.Min(128f / src.Width, 96f / src.Height);
                int w = Math.Max(1, (int)(src.Width * s)), h = Math.Max(1, (int)(src.Height * s));
                g.DrawImage(src, (128 - w) / 2, (96 - h) / 2, w, h);
            }
            return thumb;
        }

        List<string> SelectedPaths()
        {
            List<string> paths = new List<string>();
            foreach (ListViewItem item in list.SelectedItems) paths.Add((string)item.Tag);
            return paths;
        }

        void OpenSelected()
        {
            foreach (string p in SelectedPaths()) app.OpenInEditor(p);
        }

        void CopySelected()
        {
            List<string> paths = SelectedPaths();
            if (paths.Count == 0) return;
            try
            {
                using (Image src = Image.FromFile(paths[0]))
                using (Bitmap copy = new Bitmap(src))
                    ImageFile.CopyToClipboard(copy);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "복사하지 못했습니다.\n" + ex.Message, "몽캡쳐", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void ShowInFolder()
        {
            List<string> paths = SelectedPaths();
            if (paths.Count == 0) return;
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + paths[0] + "\""); } catch { }
        }

        void RemoveSelected()
        {
            foreach (string p in SelectedPaths()) History.Remove(p);
            Reload();
        }

        void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) OpenSelected();
            else if (e.KeyCode == Keys.Delete) RemoveSelected();
            else if (e.Control && e.KeyCode == Keys.C) CopySelected();
            else return;
            e.Handled = true;
        }

        void SaveBounds()
        {
            if (!IsHandleCreated || !Visible || WindowState != FormWindowState.Normal) return;
            Settings.SetRect("HistoryBounds", Bounds);
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
