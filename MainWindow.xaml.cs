﻿using MahApps.Metro.Controls;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using Path = System.IO.Path;

namespace SSHMan
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        private readonly HostModel model = new HostModel();
        private bool keepOpen = true;
        private readonly Dictionary<Guid, Thread> threads = new Dictionary<Guid, Thread>();
        private readonly ConcurrentBag<Guid> deadThreads = new ConcurrentBag<Guid>();
        public readonly static ManualResetEventSlim ShutdownSignal = new ManualResetEventSlim(false);


        public MainWindow()
        {
            this.InitializeComponent();
            this.sshMenu.DataContext = model;
        }

        private void LauncherToggleToggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch toggleSwitch)
            {
                keepOpen = toggleSwitch.IsOn;
            }
        }

        private void LaunchSSHSession(string target, Guid workId)
        {
            Log.Information("Connecting to {host}", target);

            var wtpath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
            Log.Information("Executing client script...");
            using (var proc = new Process()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = wtpath,
                    ArgumentList = { "new-tab", "-p", "Remote", "pwsh.exe", "-NoLogo", "-NonInteractive", "-File", App.ScriptPath, workId.ToString() },
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    StandardErrorEncoding = Encoding.Unicode,
                },
                EnableRaisingEvents = true,

            })
            {
                proc.Exited += this.Proc_Exited;
                proc.ErrorDataReceived += this.Proc_ErrorDataReceived;
                var success = proc.Start();
                Debug.Assert(success);
                Log.Debug("Thread ({thread}) initialized and running", Thread.CurrentThread.ManagedThreadId);
                proc.WaitForExit();
                Log.Debug("Process for thread ({thread}) exited", Thread.CurrentThread.ManagedThreadId);
            }
        }

        private void Proc_ErrorDataReceived(object sender, DataReceivedEventArgs e) => Log.Error("SSH Error: {error}", e.Data);

        private void Proc_Exited(object sender, EventArgs e)
        {
            if (sender is Process handle)
            {
                if (handle.ExitCode != 0)
                {
                    var err = handle.StandardError.ReadToEnd();
                    _ = MessageBox.Show(err, "Connection failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                var workId = new Guid(handle.StartInfo.ArgumentList.Last());
                Log.Debug("Thread with work id {id} died and was added to the queue", workId);
                deadThreads.Add(workId);
            }
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            var host = this.sshMenu.SelectedItem as SSHHostEntry;
            Connect(host);
        }


        private void Connect(SSHHostEntry host)
        {
            var workId = Guid.NewGuid();
            var msghandle = PwshIPC.Message(host.Name, exitAfterDelivery: !keepOpen, workId);
            var thread = new Thread(() => this.LaunchSSHSession(host.Name, workId));
            thread.Start();
            Log.Debug("Thread ({id}) started with work id: {workid}", thread.ManagedThreadId, workId);
            threads[workId] = thread;

            if (!keepOpen)
            {
                ShutdownSignal.Wait();
                Application.Current.Shutdown();
            }
        }


        private void ConfigClick(object sender, RoutedEventArgs e)
        {
            var localsshdir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
            var localsshcfg = Path.Combine(localsshdir, "config");
            App.EnsureDirectory(localsshdir);

            if (!File.Exists(localsshcfg))
            {
                File.WriteAllText(localsshcfg, Encoding.UTF8.GetString(Scripts.ssh));
            }

            using var proc = new Process() { StartInfo = new ProcessStartInfo() { FileName = "notepad", ArgumentList = { localsshcfg }, LoadUserProfile = true, UseShellExecute = true } };
            _ = proc.Start();
            proc.WaitForExit();
            model.Clear();
            model.Update();
        }


        private void SettingsBntClick(object sender, RoutedEventArgs e) => this.settingsFlyout.IsOpen = !this.settingsFlyout.IsOpen;

        private void MetroWindow_Closed(object sender, EventArgs e)
        {
            ShutdownSignal.Set();
            Log.Debug("Found {count} dead threads. Beginning with cleanup", deadThreads.Count);
            foreach (var corpse in deadThreads)
            {

                if (threads[corpse].ThreadState == System.Threading.ThreadState.Unstarted) continue;
                Log.Debug("\t > .. joining Thread {id}", threads[corpse].ManagedThreadId);
                var success = threads[corpse].Join(TimeSpan.FromSeconds(3));
                if (!success)
                {
                    Log.Error("Thread (id) stopped responding and will be terminated", threads[corpse].ManagedThreadId);
                    threads[corpse].Abort();
                }
            }
            ShutdownSignal.Dispose();
        }

        private void SSHMenu_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var host = this.sshMenu.SelectedItem as SSHHostEntry;
            Connect(host);
        }

        private void ExportProfiles_Click(object sender, RoutedEventArgs e)
        {
            var exporter = new SSHToProfileConverter();
            var json = exporter.GenerateSettings();


            var backupPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Path.GetFileName(exporter.WtSettings) + ".bak");
            File.Copy(exporter.WtSettings, backupPath, true);
            File.WriteAllText(exporter.WtSettings, json);
            MessageBox.Show($"Successfully exported all know SSH hosts into Windows Terminal profiles.\nA backup from your old config has been created at\n\n{backupPath}", "Profiles exported", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
