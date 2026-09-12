using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Threading;

namespace TiaMcpServer.Siemens
{
    public sealed class StaExecutor : IDisposable
    {
        private readonly Thread _thread;
        private readonly Dispatcher _dispatcher;
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
        private bool _disposed;

        public StaExecutor()
        {
            Dispatcher? captured = null;
            _thread = new Thread(() =>
            {
                captured = Dispatcher.CurrentDispatcher;
                _ready.Set();
                Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "PortalSta"
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _ready.Wait();
            _dispatcher = captured!;
        }

        public bool IsStaThread => Thread.CurrentThread == _thread;

        public T Run<T>(Func<T> function)
        {
            if (IsStaThread)
                return function();

            var holder = new ResultHolder<T>();
            _dispatcher.Invoke((Action)(() =>
            {
                try
                {
                    holder.Value = function();
                }
                catch (Exception ex)
                {
                    holder.Error = ex;
                }
            }));

            if (holder.Error != null)
                ExceptionDispatchInfo.Capture(holder.Error).Throw();

            return holder.Value!;
        }

        public void Run(Action action)
        {
            Run<object>(() =>
            {
                action();
                return null!;
            });
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            try { _dispatcher.InvokeShutdown(); } catch { }
            try { _thread.Join(2000); } catch { }
            _ready.Dispose();
        }

        private sealed class ResultHolder<T>
        {
            public T? Value;
            public Exception? Error;
        }
    }
}
