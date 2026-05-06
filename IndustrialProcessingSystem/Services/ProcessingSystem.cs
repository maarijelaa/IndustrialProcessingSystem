using IndustrialProcessingSystem.Models;
using System.Collections.Concurrent;

namespace IndustrialProcessingSystem.Services
{
    public class ProcessingSystem : IDisposable
    {
        // Events
        public event EventHandler<JobEventArgs>? JobCompleted;
        public event EventHandler<JobEventArgs>? JobFailed;

        private readonly int _maxQueueSize;
        private readonly int _workerCount;
        private readonly string _logFilePath;

        // Priority queue: lower priority number = higher priority
        private readonly SortedSet<(int Priority, long InsertOrder, Job Job)> _priorityQueue;
        private readonly object _queueLock = new object();
        private long _insertCounter = 0;

        // Idempotency + status tracking: value = true means fully processed
        private readonly ConcurrentDictionary<Guid, bool> _processedIds = new();

        // Track all submitted jobs for GetJob()
        private readonly ConcurrentDictionary<Guid, Job> _allJobs = new();

        // TaskCompletionSources per job - registered BEFORE enqueuing to avoid race condition
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<int>> _pendingTcs = new();

        // Workers
        private readonly List<Thread> _workers = new();
        private volatile bool _running = true;

        // Lock to serialize log file writes (prevents concurrent access errors)
        private readonly object _logLock = new object();
        private volatile bool _disposed = false;

        private readonly ReportService _reportService;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public ProcessingSystem(int workerCount, int maxQueueSize, string logFilePath = "job_log.txt", string reportDirectory = "reports")
        {
            _workerCount = workerCount;
            _maxQueueSize = maxQueueSize;
            _logFilePath = logFilePath;
            _reportService = new ReportService(reportDirectory);

            _priorityQueue = new SortedSet<(int Priority, long InsertOrder, Job Job)>(
                Comparer<(int Priority, long InsertOrder, Job Job)>.Create((a, b) =>
                {
                    int cmp = a.Priority.CompareTo(b.Priority);
                    if (cmp != 0) return cmp;
                    return a.InsertOrder.CompareTo(b.InsertOrder);
                })
            );

            // Subscribe to events with lambda expressions (as required by the task)
            JobCompleted += async (sender, args) =>
            {
                await LogEventAsync("COMPLETED", args.Job.Id, args.Result);
            };

            JobFailed += async (sender, args) =>
            {
                await LogEventAsync(args.Status, args.Job.Id, args.Result);
            };

            StartWorkers();
            _reportService.StartPeriodicReporting(_cts.Token);
        }

        private void StartWorkers()
        {
            for (int i = 0; i < _workerCount; i++)
            {
                var worker = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = $"Worker-{i + 1}"
                };
                worker.Start();
                _workers.Add(worker);
            }
        }

        private void WorkerLoop()
        {
            while (_running)
            {
                Job? job = null;

                lock (_queueLock)
                {
                    // Use Monitor.Wait - releases lock while waiting, re-acquires on wake
                    while (_running && _priorityQueue.Count == 0)
                    {
                        Monitor.Wait(_queueLock, 500);
                    }

                    if (_priorityQueue.Count > 0)
                    {
                        var first = _priorityQueue.Min;
                        _priorityQueue.Remove(first);
                        job = first.Job;
                    }
                }

                if (job != null)
                {
                    ProcessJobWithRetry(job);
                }
            }
        }

        public JobHandle Submit(Job job)
        {
            // Create TCS BEFORE enqueuing - avoids race where worker finishes
            // before the TCS is registered
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_queueLock)
            {
                if (_priorityQueue.Count >= _maxQueueSize)
                    throw new InvalidOperationException($"Queue is full (max {_maxQueueSize} jobs). Job rejected.");

                // Idempotency check - reject if already submitted or processed
                if (_processedIds.ContainsKey(job.Id))
                    throw new InvalidOperationException($"Job {job.Id} has already been submitted or processed (idempotency check).");

                if (_allJobs.ContainsKey(job.Id))
                    throw new InvalidOperationException($"Job {job.Id} is already in the queue.");

                // Register TCS before adding to queue
                _pendingTcs[job.Id] = tcs;

                long order = Interlocked.Increment(ref _insertCounter);
                _priorityQueue.Add((job.Priority, order, job));
                _allJobs[job.Id] = job;

                // Mark as submitted for idempotency (false = not yet fully processed)
                _processedIds[job.Id] = false;

                // Wake up all waiting workers
                Monitor.PulseAll(_queueLock);
            }

            return new JobHandle(job.Id, tcs.Task);
        }

        private void ProcessJobWithRetry(Job job)
        {
            const int maxAttempts = 3;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var startTime = DateTime.Now;
                int result = 0;
                bool success = false;

                try
                {
                    var executeTask = Task.Run(() => ExecuteJob(job));
                    bool completed = executeTask.Wait(TimeSpan.FromSeconds(2));

                    if (!completed)
                    {
                        // Job took longer than 2 seconds = FAILED
                        if (attempt < maxAttempts)
                        {
                            JobFailed?.Invoke(this, new JobEventArgs(job, -1, "FAILED"));
                            continue; // retry
                        }
                        else
                        {
                            // Third attempt also timed out -> ABORT
                            FinalizeAbort(job, startTime);
                            return;
                        }
                    }

                    result = executeTask.Result;
                    success = true;
                }
                catch (AggregateException ae)
                {
                    Console.WriteLine($"[Worker] Exception on job {job.Id} attempt {attempt}: {ae.InnerException?.Message}");
                    if (attempt < maxAttempts)
                    {
                        JobFailed?.Invoke(this, new JobEventArgs(job, -1, "FAILED"));
                        continue;
                    }
                    else
                    {
                        FinalizeAbort(job, startTime);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Worker] Unexpected error on job {job.Id}: {ex.Message}");
                    if (attempt < maxAttempts)
                    {
                        JobFailed?.Invoke(this, new JobEventArgs(job, -1, "FAILED"));
                        continue;
                    }
                    else
                    {
                        FinalizeAbort(job, startTime);
                        return;
                    }
                }

                if (success)
                {
                    var execTime = DateTime.Now - startTime;
                    _processedIds[job.Id] = true;
                    _reportService.AddEntry(new ReportEntry(job, result, false, execTime));
                    JobCompleted?.Invoke(this, new JobEventArgs(job, result, "COMPLETED"));
                    if (_pendingTcs.TryRemove(job.Id, out var doneTcs))
                        doneTcs.TrySetResult(result);
                    return;
                }
            }
        }

        private void FinalizeAbort(Job job, DateTime startTime)
        {
            _processedIds[job.Id] = true;
            JobFailed?.Invoke(this, new JobEventArgs(job, -1, "ABORT"));
            _reportService.AddEntry(new ReportEntry(job, -1, true, DateTime.Now - startTime));
            if (_pendingTcs.TryRemove(job.Id, out var abortTcs))
                abortTcs.TrySetResult(-1);
        }

        private int ExecuteJob(Job job)
        {
            return job.Type switch
            {
                JobType.Prime => JobProcessor.ProcessPrime(job.Payload),
                JobType.IO => JobProcessor.ProcessIO(job.Payload),
                _ => throw new NotSupportedException($"Job type {job.Type} not supported")
            };
        }

        private async Task LogEventAsync(string status, Guid jobId, int result)
        {
            if (_disposed) return;

            string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{status}] {jobId}, {result}";
            try
            {
                // Offload the blocking lock+write to a thread-pool thread
                // so we don't block the event-raising caller
                await Task.Run(() =>
                {
                    lock (_logLock)
                    {
                        if (_disposed) return;
                        File.AppendAllText(_logFilePath, logLine + Environment.NewLine);
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Logger] Failed to write log: {ex.Message}");
            }
        }

        public IEnumerable<Job> GetTopJobs(int n)
        {
            lock (_queueLock)
            {
                return _priorityQueue
                    .Take(n)
                    .Select(entry => entry.Job)
                    .ToList();
            }
        }

        public Job? GetJob(Guid id)
        {
            _allJobs.TryGetValue(id, out var job);
            return job;
        }

        public void Dispose()
        {
            _running = false;
            _disposed = true;

            // Wake up all workers so they can exit their loops
            lock (_queueLock)
            {
                Monitor.PulseAll(_queueLock);
            }

            _cts.Cancel();
            _cts.Dispose();

            foreach (var worker in _workers)
                worker.Join(2000);
        }
    }
}
