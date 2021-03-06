﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FileHistoryStandalone
{
    public partial class FrmManager : Form
    {
        public FrmManager()
        {
            InitializeComponent();
        }

        private int busy = 0;

        private void NicTray_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            Show();
        }

        private void FrmManager_Shown(object sender, EventArgs e)
        {
            if (Properties.Settings.Default.FirstRun)
            {
                if (!Reconfigure())
                {
                    Close();
                    return;
                }
            }
            else
            {
                TsslStatus.Text = "正在打开存档库";
                new Thread(() =>
                {
                    Program.Repo = Repository.Open(Properties.Settings.Default.Repo.Trim());
                    Program.Repo.CopyMade += Repo_CopyMade;
                    Program.Repo.Renamed += Repo_Renamed;
                    Program.DocLib = new DocLibrary(Program.Repo)
                    {
                        Paths = Properties.Settings.Default.DocPath.Trim()
                    };
                    ScanLibAsync();
                    menuStrip1.Invoke(new Action(() => 寻找版本FToolStripMenuItem.Enabled = true));
                }).Start();
            }
            if (Program.CommandLine.Length > 0)
            {
                string arg0 = Program.CommandLine[0].ToLower();
                if (arg0 == "--hide" || arg0 == "-h") Hide();
            }
        }

        private void 另存为AToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem it in LvwFiles.SelectedItems)
                if (it.Tag is Repository.RepoFile ver)
                {
                    if (SfdSaveAs.ShowDialog() == DialogResult.OK)
                        Program.Repo.SaveAs(ver, SfdSaveAs.FileName, true);
                    break;
                }
        }

        private void 删除DToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem it in LvwFiles.SelectedItems)
                if (it.Tag is Repository.RepoFile ver)
                {
                    if (MessageBox.Show(this, "确实要删除这个版本吗？", Application.ProductName,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                    {
                        Program.Repo.DeleteVersion(ver);
                        LvwFiles.Items.Remove(it);
                    }
                    break;
                }
        }

        private void Repo_CopyMade(object sender, string e)
        {
            StatusStripDefault.BeginInvoke(new Action<DateTime>((t) => TsslStatus.Text = $"[{t:H:mm:ss}] 已备份 " + e), DateTime.Now);
        }

        private void Repo_Renamed(object sender, string e)
        {
            StatusStripDefault.BeginInvoke(new Action<DateTime>((t) => TsslStatus.Text = $"[{t:H:mm:ss}] 重命名 " + e), DateTime.Now);
        }

        private bool Reconfigure()
        {
            if (new FrmConfig().ShowDialog(this) == DialogResult.OK)
            {
                Program.Repo.CopyMade += Repo_CopyMade;
                Program.Repo.Renamed += Repo_Renamed;
                重新配置RToolStripMenuItem.Enabled = false;
                ScanLibAsync();
                return true;
            }
            return false;
        }

        private Thread ScanThread = null;
        private void ScanLibAsync()
        {
            if (ScanThread != null) return;
            if (InvokeRequired) Invoke(new Action(() => TsslStatus.Text = "已启动文档库扫描"));
            Interlocked.Increment(ref busy);
            ScanThread = new Thread(() =>
              {
                  Program.DocLib.ScanLibrary();
                  Invoke(new Action(() =>
                  {
                      // NicTray.ShowBalloonTip(5000, "FileHistoryStandalone", "文档库扫描完成", ToolTipIcon.Info);
                      重新配置RToolStripMenuItem.Enabled = true;
                      TsslStatus.Text = $"[{DateTime.Now:H:mm:ss}] 文档库扫描完成";
                  }));
                  ScanThread = null;
                  Interlocked.Decrement(ref busy);
              });
            ScanThread.Start();
        }

        private void FrmManager_Load(object sender, EventArgs e)
        {
            NicTray.Icon = Icon;
        }

        private void FrmManager_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (busy > 0)
            {
                e.Cancel = true;
                MessageBox.Show(this, "工作中，现在不能退出", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
            else Program.DocLib.Dispose();
        }

        private void 寻找版本FToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (OfdFind.ShowDialog() == DialogResult.OK)
            {
                TxtDoc.Text = OfdFind.FileName;
                string file = OfdFind.FileName.Trim();
                LvwFiles.BeginUpdate();
                LvwFiles.Items.Clear();
                var vers = Program.Repo.FindVersions(file);
                foreach (var ver in vers)
                {
                    var it = new ListViewItem(ver.LastModifiedTimeUtc.ToLocalTime().ToString());
                    it.SubItems.Add(ver.Length.ToString() + "字节");
                    it.Tag = ver;
                    LvwFiles.Items.Add(it);
                }
                LvwFiles.EndUpdate();
            }
        }

        private void TrimFinished()
        {
            TsslStatus.Text = $"[{DateTime.Now:H:mm:ss}] 版本清理完成";
        }

        private bool TrimPrompt()
        {
            if (MessageBox.Show(this, "确实要删除这些版本吗？", Application.ProductName,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {
                LvwFiles.Items.Clear();
                TsslStatus.Text = $"[{DateTime.Now:H:mm:ss}] 已启动版本清理";
                return true;
            }
            return false;
        }

        private void 仅保留最新版本ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (TrimPrompt())
            {
                Interlocked.Increment(ref busy);
                new Thread(() =>
                {
                    Program.Repo.TrimFull();
                    Interlocked.Decrement(ref busy);
                    BeginInvoke(new Action(TrimFinished));
                }).Start();
            }
        }

        private void 删除90天以前的版本ToolStripMenuItem_Click(object sender, EventArgs e)
        {

            if (TrimPrompt())
            {
                Interlocked.Increment(ref busy);
                new Thread(() =>
                {
                    Program.Repo.Trim(new TimeSpan(90, 0, 0, 0));
                    Interlocked.Decrement(ref busy);
                    BeginInvoke(new Action(TrimFinished));
                }).Start();
            }
        }

        private void 删除已删除文件的备份ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (TrimPrompt())
            {
                Interlocked.Increment(ref busy);
                new Thread(() =>
                {
                    Program.Repo.Trim();
                    Interlocked.Decrement(ref busy);
                    BeginInvoke(new Action(TrimFinished));
                }).Start();
            }
        }

        private void 隐藏HToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Hide();
        }

        private void 重新配置RToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Reconfigure();
        }

        private void 退出XToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
