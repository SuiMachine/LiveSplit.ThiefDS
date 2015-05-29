﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

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

        private DeepPointer _IsLoading;

        private enum ExpectedDllSizes
        {
        }

        public void resetSplitStates()
        {
        }

        public GameMemory(ThiefDSSettings componentSettings)
        {
            _IsLoading = new DeepPointer("ole32.dll", 0x146310);
            resetSplitStates();

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

        void MemoryReadThread()
        {
            Debug.WriteLine("[NoLoads] MemoryReadThread");

            while (!_cancelSource.IsCancellationRequested)
            {
                try
                {
                    bool isLoading;
                    bool prevIsLoading = false;
                    bool loadingStarted = false;
                    uint simpleDelay = 62;                                                                                   //Counts down 62*15ms before it states there is no loading

                    Debug.WriteLine("[NoLoads] Waiting for T3Main.EXE...");
                    uint frameCounter = 0;
                    
                    Process game;
                    while ((game = GetGameProcess()) == null)
                    {
                        isLoading = true;                                                                                   //Required, because of the game killing process during loadings.

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
                                simpleDelay = 62;
                            }
                        }

                        Thread.Sleep(250);
                        if (_cancelSource.IsCancellationRequested)
                        {
                            return;
                        }

                        prevIsLoading = isLoading;
                    }

                    Debug.WriteLine("[NoLoads] Got games process!");

                    while (!game.HasExited)
                    {
                        _IsLoading.Deref(game, out isLoading);
                        if(simpleDelay==0)
                        {
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
                        }
                        else
                        {
                            simpleDelay--;
                        }
                        prevIsLoading = isLoading;

                        frameCounter++;

                        Thread.Sleep(15);

                        if (_cancelSource.IsCancellationRequested)
                        {
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.ToString());
                    Thread.Sleep(1000);
                }
            }
        }

        Process GetGameProcess()
        {
            Process game = Process.GetProcesses().FirstOrDefault(p => p.ProcessName.ToLower() == "t3main" && !p.HasExited && !_ignorePIDs.Contains(p.Id));
            if (game == null)
            {
                return null;
            }

            /*if (game.MainModule.ModuleMemorySize != (int)ExpectedDllSizes.DXIWSteam && game.MainModule.ModuleMemorySize != (int)ExpectedDllSizes.DXIWGOG)
            {
                _ignorePIDs.Add(game.Id);
                _uiThread.Send(d => MessageBox.Show("Unexpected game version. Deus Ex Invisible War (1.2) on Steam or GOG is required.", "LiveSplit.DXIW",
                    MessageBoxButtons.OK, MessageBoxIcon.Error), null);
                return null;
            }*/

            return game;
        }
    }
}
