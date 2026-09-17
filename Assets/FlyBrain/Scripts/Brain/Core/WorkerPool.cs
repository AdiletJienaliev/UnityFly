using System;
using System.Threading;

namespace FlyBrain.Brain
{
    /// <summary>Minimal fork-join pool: runs body(0..count-1) on dedicated threads plus the caller.</summary>
    public sealed class WorkerPool : IDisposable
    {
        readonly Thread[] _threads;
        readonly ManualResetEventSlim[] _wake;
        readonly ManualResetEventSlim _done = new ManualResetEventSlim(false, 200);
        Action<int> _body;
        int _count, _next, _remaining;
        volatile bool _disposed;
        Exception _error;

        public int WorkerCount => _threads.Length + 1;

        public WorkerPool(int workers)
        {
            workers = Math.Max(1, workers);
            _threads = new Thread[workers - 1];
            _wake = new ManualResetEventSlim[workers - 1];
            for (int i = 0; i < _threads.Length; i++)
            {
                int id = i;
                _wake[i] = new ManualResetEventSlim(false, 200);
                _threads[i] = new Thread(() => Loop(id)) { IsBackground = true, Name = "FlyBrain worker " + i, Priority = ThreadPriority.AboveNormal };
                _threads[i].Start();
            }
        }

        public void Run(int count, Action<int> body)
        {
            if (count <= 0) return;
            _body = body;
            _count = count;
            _remaining = count;
            _error = null;
            _done.Reset();
            Interlocked.Exchange(ref _next, -1);
            for (int i = 0; i < _wake.Length && i < count - 1; i++) _wake[i].Set();
            Work();
            _done.Wait();
            if (_error != null) throw new AggregateException(_error);
        }

        void Work()
        {
            int k;
            while ((k = Interlocked.Increment(ref _next)) < _count)
            {
                try { _body(k); }
                catch (Exception e) { _error = e; }
                if (Interlocked.Decrement(ref _remaining) == 0) _done.Set();
            }
        }

        void Loop(int id)
        {
            while (true)
            {
                _wake[id].Wait();
                _wake[id].Reset();
                if (_disposed) return;
                Work();
            }
        }

        public void Dispose()
        {
            _disposed = true;
            foreach (var w in _wake) w.Set();
            foreach (var t in _threads) t.Join(1000);
        }
    }
}
