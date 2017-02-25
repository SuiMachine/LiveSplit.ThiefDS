using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LiveSplit.ComponentUtil;

namespace LiveSplit.ThiefDS
{
    class GameMemory
    {

        public event EventHandler OnLoadStarted;
        public event EventHandler OnLoadFinished;

        private Task _thread;
        private CancellationTokenSource _cancelSource;
        private SynchronizationContext _uiThread;
        private List<int> _ignorePIDs;
        private ThiefDSSettings _settings;

        private DeepPointer _isLoadingPtr;
        private IntPtr baseAddress = IntPtr.Zero;

        private enum ExpectedDllSizes
        {
        }

        public bool[] splitStates { get; set; }

        public GameMemory(ThiefDSSettings componentSettings)
        {
            _isLoadingPtr = new DeepPointer(0x5FFA00);
            _settings = componentSettings;

            _ignorePIDs = new List<int>();
        }

        public void StartMonitoring()
        {
            if (_thread != null && _thread.Status == TaskStatus.Running)
            {
                throw new InvalidOperationException();
            }
            if (!(SynchronizationContext.Current is WindowsFormsSynchronizationContext))
            {
                throw new InvalidOperationException("SynchronizationContext.Current is not a UI thread.");
            }

            _uiThread = SynchronizationContext.Current;
            _cancelSource = new CancellationTokenSource();
            _thread = Task.Factory.StartNew(MemoryReadThread);
        }

        public void Stop()
        {
            if (_cancelSource == null || _thread == null || _thread.Status != TaskStatus.Running)
            {
                return;
            }

            _cancelSource.Cancel();
            _thread.Wait();
        }

        bool isLoading = false;
        bool prevIsLoading = false;
        bool loadingStarted = false;
        uint delay = 62;

        void MemoryReadThread()
        {
            Debug.WriteLine("[NoLoads] MemoryReadThread");

            while (!_cancelSource.IsCancellationRequested)
            {
                try
                {
                    Debug.WriteLine("[NoLoads] Waiting for dx2main.exe...");

                    Process game;
                    while ((game = GetGameProcess()) == null)
                    {
                        delay = 90;
                        Thread.Sleep(250);
                        if (_cancelSource.IsCancellationRequested)
                        {
                            return;
                        }

                        isLoading = true;
                        baseAddress = IntPtr.Zero;

                        if (isLoading != prevIsLoading)
                        {
                            loadingStarted = true;

                            // pause game timer
                            _uiThread.Post(d =>
                            {
                                if (this.OnLoadStarted != null)
                                {
                                    this.OnLoadStarted(this, EventArgs.Empty);
                                }
                            }, null);
                        }

                        prevIsLoading = true;

                    }

                    Debug.WriteLine("[NoLoads] Got games process!");

                    uint frameCounter = 0;

                    while (!game.HasExited)
                    {
                        if(delay == 0)
                        {
                            if(_settings.UseNonSafeMemoryReading)
                            {   //You've seen nothing!!!!!
                                if(baseAddress == IntPtr.Zero)
                                {
                                    baseAddress = game.MainModule.BaseAddress;
                                }
                                
                                if(baseAddress != IntPtr.Zero)
                                    isLoading = Convert.ToBoolean(Trainer.ReadByte(game, baseAddress.ToInt32() + 0x5FFA00));
                            }
                            else
                                _isLoadingPtr.Deref(game, out isLoading);

                            if (isLoading != prevIsLoading)
                            {
                                if (isLoading)
                                {
                                    Debug.WriteLine(String.Format("[NoLoads] Load Start - {0}", frameCounter));

                                    loadingStarted = true;

                                    // pause game timer
                                    _uiThread.Post(d =>
                                    {
                                        if (this.OnLoadStarted != null)
                                        {
                                            this.OnLoadStarted(this, EventArgs.Empty);
                                        }
                                    }, null);
                                }
                                else
                                {
                                    Debug.WriteLine(String.Format("[NoLoads] Load End - {0}", frameCounter));

                                    if (loadingStarted)
                                    {
                                        loadingStarted = false;

                                        // unpause game timer
                                        _uiThread.Post(d =>
                                        {
                                            if (this.OnLoadFinished != null)
                                            {
                                                this.OnLoadFinished(this, EventArgs.Empty);
                                            }
                                        }, null);
                                    }
                                }
                            }

                            prevIsLoading = isLoading;
                            frameCounter++;

                            Thread.Sleep(15);

                            if (_cancelSource.IsCancellationRequested)
                            {
                                return;
                            }
                        }
                        else
                        {
                            prevIsLoading = isLoading;
                            frameCounter++;
                            delay--;
                            Thread.Sleep(15);
                        }
                    }

                    // pause game timer on exit or crash
                    _uiThread.Post(d =>
                    {
                        if (this.OnLoadStarted != null)
                        {
                            this.OnLoadStarted(this, EventArgs.Empty);
                        }
                    }, null);
                    isLoading = true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.ToString());
                    Thread.Sleep(1000);
                }
            }
        }

        private int getDLLAddress(Process procRef)
        {
            try
            {
                IntPtr ptr = AwfulRippedOffCode.GetGameModuleBase(procRef);

                return ptr.ToInt32();
            }
            catch(Exception e)
            {
                Debug.WriteLine(e.ToString());
                return 0x0;
            }

        }


        Process GetGameProcess()
        {
            Process game = Process.GetProcesses().FirstOrDefault(p => p.ProcessName.ToLower() == "t3main" && !p.HasExited && !_ignorePIDs.Contains(p.Id));
            if (game == null)
            {
                return null;
            }

            return game;
        }
    }
}
