using System;
using System.IO;
using System.IO.Compression;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Linq;
using System.Diagnostics;
using fNbt;

namespace MapSentinel
{
    public class MainForm : Form
    {
        FileSystemWatcher watcher;
        NotifyIcon tray;
		
		bool isExit = false;
		
        CheckBox autoStartChk, popupChk, notifyChk;

        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        string mcSaves = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "saves");

        public MainForm()
        {
            Text = "MapSentinel";
            Size = new Size(420, 260);
            BackColor = Color.Black;
            ForeColor = Color.White;

            autoStartChk = new CheckBox() { Text = "시작프로그램", Top = 20, Left = 20 };
            popupChk = new CheckBox() { Text = "다운로드 창", Top = 60, Left = 20, Checked = true };
            notifyChk = new CheckBox() { Text = "알림", Top = 100, Left = 20, Checked = true };

            autoStartChk.CheckedChanged += (s, e) => ToggleStartup(autoStartChk.Checked);

            Controls.Add(autoStartChk);
            Controls.Add(popupChk);
            Controls.Add(notifyChk);

            InitTray();

            Load += (s, e) =>
            {
                Hide();
                StartWatching();
                Notify("백그라운드 실행 중");
            };
        }

        void InitTray()
        {
            tray = new NotifyIcon()
            {
                Icon = SystemIcons.Application,
                Visible = true,
                Text = "MapSentinel"
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("열기", null, (s, e) => { Show(); WindowState = FormWindowState.Normal; });
            menu.Items.Add("종료", null, (s, e) => {

			isExit = true;
			watcher?.Dispose();
			tray.Visible = false;
			Application.ExitThread();
			Application.Exit();
		});
		
		tray.ContextMenuStrip = menu;
		}

        void StartWatching()
        {
            watcher = new FileSystemWatcher(downloads, "*.zip");
            watcher.Created += OnZipDetected;
            watcher.EnableRaisingEvents = true;
        }

        void OnZipDetected(object sender, FileSystemEventArgs e)
        {
            new Thread(() =>
            {
                Form popup = null;
                if (popupChk.Checked)
                    popup = CreatePopup(e.Name);

                WaitDownload(e.FullPath, popup);

                if (!CheckZip(e.FullPath))
                {
                    Alert("ZIP 손상");
                    return;
                }

                if (!CheckMapStructure(e.FullPath))
                {
                    Alert("맵 구조 아님");
                    return;
                }

                int? version = ParseNBT(e.FullPath);
                if (version == null)
                {
                    Alert("NBT 오류");
                    return;
                }

                Log("버전: " + version);

                CheckModMap(e.FullPath);

                InstallMap(e.FullPath);

                Alert("맵 설치 완료");

                popup?.Close();

            }).Start();
        }

        void WaitDownload(string path, Form popup)
        {
            long last = 0;
            Stopwatch sw = new Stopwatch();
            sw.Start();

            ProgressBar bar = popup?.Controls.OfType<ProgressBar>().FirstOrDefault();

            while (true)
            {
                try
                {
                    long size = new FileInfo(path).Length;
                    double speed = (size - last) / 1024.0;

                    if (bar != null)
                        bar.Invoke(() => bar.Value = Math.Min(100, bar.Value + 3));

                    if (size == last) break;

                    last = size;
                }
                catch { }

                Thread.Sleep(500);
            }
        }

        bool CheckZip(string path)
        {
            try { ZipFile.OpenRead(path).Dispose(); return true; }
            catch { return false; }
        }

        bool CheckMapStructure(string path)
        {
            using (var zip = ZipFile.OpenRead(path))
            {
                var names = zip.Entries.Select(e => e.FullName);
                return names.Any(n => n.Contains("level.dat")) && names.Any(n => n.Contains("region/"));
            }
        }

        int? ParseNBT(string path)
        {
            try
            {
                string temp = Path.Combine(Path.GetTempPath(), "level.dat");

                using (var zip = ZipFile.OpenRead(path))
                {
                    var entry = zip.Entries.FirstOrDefault(x => x.FullName.EndsWith("level.dat"));
                    if (entry == null) return null;

                    entry.ExtractToFile(temp, true);
                }

                var file = new NbtFile(temp);
                return file.RootTag["Data"]["DataVersion"].IntValue;
            }
            catch
            {
                return null;
            }
        }

        void CheckModMap(string path)
        {
            using (var zip = ZipFile.OpenRead(path))
            {
                var names = zip.Entries.Select(e => e.FullName.ToLower());

                if (names.Any(n => n.Contains("fabric")))
                    Log("Fabric 맵");

                if (names.Any(n => n.Contains("forge")))
                    Log("Forge 맵");
            }
        }

        void InstallMap(string zipPath)
        {
            string temp = Path.Combine(Path.GetTempPath(), "map");

            if (Directory.Exists(temp))
                Directory.Delete(temp, true);

            ZipFile.ExtractToDirectory(zipPath, temp);

            string world = Directory.GetDirectories(temp).FirstOrDefault();
            if (world == null) return;

            string dest = Path.Combine(mcSaves, Path.GetFileName(world));

            if (Directory.Exists(dest))
                Directory.Delete(dest, true);

            CopyDir(world, dest);
        }

        void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);

            foreach (var f in Directory.GetFiles(src))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));

            foreach (var d in Directory.GetDirectories(src))
                CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        Form CreatePopup(string name)
        {
            Form f = new Form()
            {
                Size = new Size(300, 100),
                Text = "다운로드 중",
                TopMost = true,
                BackColor = Color.Black,
                ForeColor = Color.White
            };

            Label lbl = new Label() { Text = name, Dock = DockStyle.Top };
            ProgressBar bar = new ProgressBar() { Dock = DockStyle.Bottom };

            f.Controls.Add(lbl);
            f.Controls.Add(bar);

            new Thread(() => Application.Run(f)).Start();

            return f;
        }

        void Notify(string msg)
        {
            if (notifyChk.Checked)
                tray.ShowBalloonTip(2000, "MapSentinel", msg, ToolTipIcon.Info);
        }

        void Alert(string msg)
        {
            Notify(msg);
        }

        void ToggleStartup(bool enable)
        {
            var rk = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);

            if (enable)
                rk.SetValue("MapSentinel", Application.ExecutablePath);
            else
                rk.DeleteValue("MapSentinel", false);
        }

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			if (!isExit)
		{
			e.Cancel = true;
			Hide();
		}
			else
		{
			tray.Visible = false;
			}

			base.OnFormClosing(e);
}
        
        
        
        

        void Log(string msg)
        {
            Console.WriteLine(msg);
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.Run(new MainForm());
        }
    }
}