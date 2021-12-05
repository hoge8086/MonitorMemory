using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MonitorMemory
{
    class MainWindowViewModel
    {
        public const string MemoryLogDirectory  = "Log";
        public ReactiveCommand RunCommand { get; set; }
        public ReactiveCommand ClearMemoryCommand { get; set; }

        public ReactiveProperty<long> MaxMemory { get; set; }
        public ReactiveProperty<string> ProgramPath { get; set; }

        public ReactiveProperty<IList<string>> DropProgramPaths { get; set; }

        public ReactiveProperty<string> LogMessage { get; set; }

        private ReactiveProperty<bool> isRunning;

        
        public MainWindowViewModel()
        {
            MaxMemory = new ReactiveProperty<long>();
            ProgramPath = new ReactiveProperty<string>(Properties.Settings.Default.ProgramPath);
            LogMessage = new ReactiveProperty<string>();
            DropProgramPaths = new ReactiveProperty<IList<string>>();
            isRunning = new ReactiveProperty<bool>(false);

            ClearMemoryCommand = new ReactiveCommand();
            ClearMemoryCommand.Subscribe(() => MaxMemory.Value = 0);
            DropProgramPaths.Subscribe(x => ProgramPath.Value = x != null ? x[0] : ProgramPath.Value);

            RunCommand = new []{
                isRunning.Inverse(),
                ProgramPath.Select(x => !string.IsNullOrEmpty(x)), 
            }
            .CombineLatestValuesAreAllTrue()
            .ToReactiveCommand();
            RunCommand.Subscribe(async () =>
            {
                Process process = null;
                var memLogPath = CreateMemoryLogPath(ProgramPath.Value);

                try
                {
                    if (!Directory.Exists(MemoryLogDirectory))
                        Directory.CreateDirectory(MemoryLogDirectory);

                    process = Process.Start(ProgramPath.Value);
                    isRunning.Value = true;
                    MaxMemory.Value = 0;
                    Log("プロセスが開始しました。モニタを開始します。");

                    using (var memLog = new StreamWriter(memLogPath, true))
                    {

                        memLog.WriteLine("DateTime,MemorySize");
                        do
                        {
                            process.Refresh();
                            // タスクマネージャの「コミットサイズ」と大体同じ
                            MaxMemory.Value = Math.Max(MaxMemory.Value, process.PrivateMemorySize64);

                            try
                            {
                                memLog.WriteLine($"{DateTime.Now},{process.PrivateMemorySize64}");
                            }
                            catch(Exception ex)
                            {
                                Log(ex.Message);
                            }

                            await Task.Delay(1000);
                        } while (!process.HasExited);


                        Log("プロセスが終了しました。モニタを停止します。");
                        if(File.Exists(memLogPath))
                            Log($"メモリ使用量の履歴を{memLogPath}に格納しています。");

                    }
                }
                catch (Exception ex)
                {
                    Log(ex.Message);
                }
                finally
                {
                    if(process != null)
                    {
                        process.Dispose();
                        process = null;
                    }
                    isRunning.Value = false;
                }
            });
        }
        private string CreateMemoryLogPath(string programPath)
        {
            return MemoryLogDirectory + Path.DirectorySeparatorChar + $"{ DateTime.Now.ToString("yyyyMddHHmmssff")}_{Path.GetFileNameWithoutExtension(programPath)}.csv";
        }
        private void Log(string msg)
        {
            LogMessage.Value += $"[{DateTime.Now.ToString()}] {msg}" + Environment.NewLine;
        }

        public bool Close()
        {
            if (isRunning.Value)
            {
                System.Windows.MessageBox.Show("実行中のアプリケーションを終了してください。", "メモリ監視ツール");
                return false;
            }

            Properties.Settings.Default.ProgramPath = ProgramPath.Value;
            Properties.Settings.Default.Save();
            return true;
        }


    }
}
