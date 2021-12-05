using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MonitorMemory
{
    class MainWindowViewModel
    {
        public const string MemoryLogDirectory  = "Log";
        public ReactiveCommand RunCommand { get; set; }
        public ReactiveCommand ClearMemoryCommand { get; set; }
        public ReactiveCommand StopMonitorCommand { get; set; }

        public ReactiveProperty<long> MaxMemory { get; set; }
        public ReactiveProperty<string> ProgramPath { get; set; }

        public ReactiveProperty<IList<string>> DropProgramPaths { get; set; }

        public ReactiveProperty<string> LogMessage { get; set; }

        private ReactiveProperty<bool> isRunning;

        private CancellationTokenSource cancelTokenSrouce = null;

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

            StopMonitorCommand = isRunning.ToReactiveCommand();
            StopMonitorCommand.Subscribe(x =>
            {
                if(cancelTokenSrouce != null)
                    cancelTokenSrouce.Cancel();
            });

            RunCommand = new []{
                isRunning.Inverse(),
                ProgramPath.Select(x => !string.IsNullOrEmpty(x)), 
            }
            .CombineLatestValuesAreAllTrue()
            .ToReactiveCommand();
            RunCommand.Subscribe(() =>
            {
                isRunning.Value = true;
                MaxMemory.Value = 0;
                Log("プロセスを起動して、モニタを開始します。");
                var memLogPath = CreateMemoryLogPath(ProgramPath.Value);

                cancelTokenSrouce = new CancellationTokenSource();
                // メモリ使用量監視
                var monitor = CreateMonitorMemoryObserbable(ProgramPath.Value, 1, cancelTokenSrouce.Token);

                // メモリ使用量を購読
                monitor.Finally(() =>
                    {
                        // モニタ終了時の処理
                        Log("モニタを停止しました。");
                        if (File.Exists(memLogPath))
                            Log($"メモリ使用量の履歴を{memLogPath}に格納しています。");

                        isRunning.Value = false;
                        cancelTokenSrouce = null;
                    })
                    .Subscribe(x => {
                        // 現在のメモリ使用量を取得
                        MaxMemory.Value = Math.Max(MaxMemory.Value, x);
                        LogMemory(memLogPath, x);
                    },
                    (ex) => {
                        // エラー時の処理
                        Log(ex.Message);
                    });
                monitor.Connect();
            });
        }
        public IConnectableObservable<long> CreateMonitorMemoryObserbable(string programPath, int interval, CancellationToken cancellation)
        {
            return Observable.Create<long>((observer) =>
            {
                Process process = null;
                try
                {
                    process = Process.Start(programPath);
                    do
                    {
                        if (cancellation.IsCancellationRequested)
                            break;

                        process.Refresh();
                        // タスクマネージャの「コミットサイズ」と大体同じ
                        observer.OnNext(process.PrivateMemorySize64);
                        Task.Delay(interval * 1000 ).Wait();
                    } while (!process.HasExited);


                    // MEMO:OnError()の後にOnCompleted()を呼んでも、Subscribe()側で完了通知は呼ばれない
                    observer.OnCompleted();

                }catch(Exception ex)
                {
                    observer.OnError(ex);
                }
                finally
                {
                    if(process != null)
                    {
                        process.Dispose();
                        process = null;
                    }
                }

                return Disposable.Empty;
            }).SubscribeOn(Scheduler.ThreadPool).Publish();
        }
        
        private void LogMemory(string memLogPath, long memorySize)
        {
            if (!Directory.Exists(MemoryLogDirectory))
                Directory.CreateDirectory(MemoryLogDirectory);

            if (!File.Exists(memLogPath))
                WriteMemoryLog("DateTime,MemorySize", false);

            WriteMemoryLog($"{DateTime.Now},{memorySize}", true);


            void  WriteMemoryLog(string log, bool append)
            {
                try
                {
                    using (var memLog = new StreamWriter(memLogPath, append))
                    {
                        memLog.WriteLine(log);
                    }
                }catch(Exception ex)
                {
                    Log(ex.Message);
                }
            }

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
                System.Windows.MessageBox.Show("モニタを停止してください。", "メモリ監視ツール");
                return false;
            }

            Properties.Settings.Default.ProgramPath = ProgramPath.Value;
            Properties.Settings.Default.Save();
            return true;
        }
    }
}
