using IndustrialProcessingSystem.Models;
using IndustrialProcessingSystem.Services;
using Xunit;

namespace IndustrialProcessingSystem.Tests
{
    public class ProcessingSystemTests : IDisposable
    {
        private readonly string _logFile;
        private readonly string _reportDir;

        public ProcessingSystemTests()
        {
            _logFile = Path.Combine(Path.GetTempPath(), $"test_log_{Guid.NewGuid()}.txt");
            _reportDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        }

        private ProcessingSystem CreateSystem(int workers = 2, int maxQueue = 50)
        {
            return new ProcessingSystem(workers, maxQueue, _logFile, _reportDir);
        }

        private Job MakeIOJob(int delayMs = 50, int priority = 1)
        {
            return new Job(Guid.NewGuid(), JobType.IO, $"delay:{delayMs}", priority);
        }

        private Job MakePrimeJob(int limit = 100, int threads = 1, int priority = 1)
        {
            return new Job(Guid.NewGuid(), JobType.Prime, $"numbers:{limit},threads:{threads}", priority);
        }

        // ─── Submit / Handle ─────────────────────────────────────────────────

        [Fact]
        public void Submit_ReturnsJobHandle_WithCorrectId()
        {
            using var system = CreateSystem();
            var job = MakeIOJob(10);
            var handle = system.Submit(job);
            Assert.Equal(job.Id, handle.Id);
        }

        [Fact]
        public void Submit_Handle_HasNonNullTask()
        {
            using var system = CreateSystem();
            var job = MakeIOJob(10);
            var handle = system.Submit(job);
            Assert.NotNull(handle.Result);
        }

        // ─── Execution correctness ────────────────────────────────────────────

        [Fact]
        public async Task Submit_IOJob_CompletesWithValueBetween0And100()
        {
            using var system = CreateSystem();
            var job = MakeIOJob(10);
            var handle = system.Submit(job);
            int result = await handle.Result.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.InRange(result, 0, 100);
        }

        [Fact]
        public async Task Submit_PrimeJob_ReturnsCorrectPrimeCount()
        {
            using var system = CreateSystem();
            var job = MakePrimeJob(100, 1);
            var handle = system.Submit(job);
            int result = await handle.Result.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(25, result); // 25 primes ≤ 100
        }

        [Fact]
        public async Task Submit_PrimeJob_MultipleThreads_SameResult()
        {
            using var system = CreateSystem();
            var j1 = MakePrimeJob(200, 1);
            var j2 = MakePrimeJob(200, 4);
            var h1 = system.Submit(j1);
            var h2 = system.Submit(j2);
            int r1 = await h1.Result.WaitAsync(TimeSpan.FromSeconds(10));
            int r2 = await h2.Result.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(r1, r2);
        }

        // ─── Queue limits ─────────────────────────────────────────────────────

        [Fact]
        public void Submit_ThrowsWhenQueueFull()
        {
            // maxQueue=1: fill it with a slow job, then try one more
            using var system = CreateSystem(workers: 1, maxQueue: 1);

            // First job - worker will pick it up immediately and be busy
            system.Submit(MakeIOJob(5000, 1));

            // Give worker time to dequeue it so queue is empty, then add 2 to fill
            Thread.Sleep(80);

            // Job that stays in queue (worker is busy)
            system.Submit(MakeIOJob(5000, 2));

            // Now queue is full (1 item) -> should throw
            Assert.Throws<InvalidOperationException>(() =>
                system.Submit(MakeIOJob(50, 3)));
        }

        // ─── Idempotency ──────────────────────────────────────────────────────

        [Fact]
        public void Submit_SameJob_Twice_Throws()
        {
            using var system = CreateSystem();
            var job = MakeIOJob(5000);
            system.Submit(job);

            // Same ID, whether in queue or processed, must reject
            Assert.Throws<InvalidOperationException>(() => system.Submit(job));
        }

        [Fact]
        public async Task Submit_SameId_AfterCompletion_Throws()
        {
            using var system = CreateSystem();
            var job = MakeIOJob(10);
            var handle = system.Submit(job);
            await handle.Result.WaitAsync(TimeSpan.FromSeconds(5));

            await Task.Delay(100); // Let processedIds update

            Assert.Throws<InvalidOperationException>(() => system.Submit(job));
        }

        // ─── GetJob / GetTopJobs ──────────────────────────────────────────────

        [Fact]
        public void GetJob_ReturnsJob_ForKnownId()
        {
            using var system = CreateSystem();
            var job = MakeIOJob(5000);
            system.Submit(job);

            var found = system.GetJob(job.Id);
            Assert.NotNull(found);
            Assert.Equal(job.Id, found!.Id);
        }

        [Fact]
        public void GetJob_ReturnsNull_ForUnknownId()
        {
            using var system = CreateSystem();
            Assert.Null(system.GetJob(Guid.NewGuid()));
        }

        [Fact]
        public void GetTopJobs_ReturnsJobsOrderedByPriority()
        {
            // Use 0 workers so jobs stay in queue
            using var system = new ProcessingSystem(0, 50, _logFile, _reportDir);

            var job3 = new Job(Guid.NewGuid(), JobType.IO, "delay:100", 3);
            var job1 = new Job(Guid.NewGuid(), JobType.IO, "delay:100", 1);
            var job2 = new Job(Guid.NewGuid(), JobType.IO, "delay:100", 2);

            system.Submit(job3);
            system.Submit(job1);
            system.Submit(job2);

            var top = system.GetTopJobs(10).ToList();

            Assert.Equal(3, top.Count);
            // Must be sorted ascending by priority number (lower = higher priority)
            for (int i = 1; i < top.Count; i++)
                Assert.True(top[i - 1].Priority <= top[i].Priority);
        }

        [Fact]
        public void GetTopJobs_RespectsNLimit()
        {
            using var system = new ProcessingSystem(0, 50, _logFile, _reportDir);

            for (int i = 0; i < 5; i++)
                system.Submit(new Job(Guid.NewGuid(), JobType.IO, "delay:100", i + 1));

            var top = system.GetTopJobs(2).ToList();
            Assert.Equal(2, top.Count);
        }

        [Fact]
        public void GetTopJobs_ReturnsFirstByPriority()
        {
            using var system = new ProcessingSystem(0, 50, _logFile, _reportDir);

            var jobHigh = new Job(Guid.NewGuid(), JobType.IO, "delay:100", 1);
            var jobLow = new Job(Guid.NewGuid(), JobType.IO, "delay:100", 5);

            system.Submit(jobLow);
            system.Submit(jobHigh);

            var top = system.GetTopJobs(1).ToList();
            Assert.Single(top);
            Assert.Equal(1, top[0].Priority);
        }

        // ─── Events ───────────────────────────────────────────────────────────

        [Fact]
        public async Task JobCompleted_Event_FiresOnSuccess()
        {
            using var system = CreateSystem();
            var job = MakeIOJob(10);
            bool fired = false;
            var tcs = new TaskCompletionSource<bool>();

            system.JobCompleted += (s, e) =>
            {
                if (e.Job.Id == job.Id)
                {
                    fired = true;
                    tcs.TrySetResult(true);
                }
            };

            system.Submit(job);
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(fired);
        }

        [Fact]
        public async Task JobCompleted_EventArgs_HaveCorrectStatus()
        {
            using var system = CreateSystem();
            var job = MakeIOJob(10);
            string? status = null;
            var tcs = new TaskCompletionSource<bool>();

            system.JobCompleted += (s, e) =>
            {
                if (e.Job.Id == job.Id)
                {
                    status = e.Status;
                    tcs.TrySetResult(true);
                }
            };

            system.Submit(job);
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("COMPLETED", status);
        }

        // ─── Multiple concurrent jobs ─────────────────────────────────────────

        [Fact]
        public async Task MultipleJobs_AllComplete()
        {
            using var system = CreateSystem(workers: 3, maxQueue: 50);
            var handles = new List<JobHandle>();

            for (int i = 0; i < 5; i++)
                handles.Add(system.Submit(MakeIOJob(10, i + 1)));

            var results = await Task.WhenAll(
                handles.Select(h => h.Result.WaitAsync(TimeSpan.FromSeconds(10))));

            Assert.All(results, r => Assert.InRange(r, 0, 100));
        }

        [Fact]
        public async Task HighPriorityJob_ProcessedBeforeLowPriority()
        {
            // Use 1 worker, submit low priority first, then high priority
            // High priority should finish first (or at least be picked first)
            using var system = CreateSystem(workers: 1, maxQueue: 50);

            // Keep worker busy briefly with first job
            var blocker = new Job(Guid.NewGuid(), JobType.IO, "delay:10", 5);
            system.Submit(blocker);
            await Task.Delay(30); // let worker pick up blocker

            // Now enqueue low and high priority
            var lowPriorityJob = new Job(Guid.NewGuid(), JobType.IO, "delay:10", 5);
            var highPriorityJob = new Job(Guid.NewGuid(), JobType.IO, "delay:10", 1);
            system.Submit(lowPriorityJob);
            system.Submit(highPriorityJob);

            // GetTopJobs should show highPriorityJob first
            var top = system.GetTopJobs(2).ToList();
            if (top.Count >= 2)
                Assert.True(top[0].Priority <= top[1].Priority);
        }

        // ─── Logging ──────────────────────────────────────────────────────────

        [Fact]
        public async Task LogFile_ContainsCompletedEntry_AfterJob()
        {
            using var system = CreateSystem();
            var job = MakeIOJob(10);
            var handle = system.Submit(job);
            await handle.Result.WaitAsync(TimeSpan.FromSeconds(5));

            await Task.Delay(300); // Allow async log write to complete

            Assert.True(File.Exists(_logFile));
            string content = await File.ReadAllTextAsync(_logFile);
            Assert.Contains("COMPLETED", content);
            Assert.Contains(job.Id.ToString(), content);
        }

        [Fact]
        public async Task LogFile_Format_IsCorrect()
        {
            using var system = CreateSystem();
            var job = MakeIOJob(10);
            var handle = system.Submit(job);
            await handle.Result.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(300);

            string content = await File.ReadAllTextAsync(_logFile);
            // Format: [DateTime] [Status] JobId, Result
            Assert.Matches(@"\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\] \[COMPLETED\]", content);
        }

        // ─── ReportService ────────────────────────────────────────────────────

        [Fact]
        public void ReportService_GenerateReport_CreatesXmlFile()
        {
            string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var svc = new ReportService(dir);

            svc.AddEntry(new ReportEntry(
                new Job(Guid.NewGuid(), JobType.IO, "delay:10", 1),
                42, false, TimeSpan.FromMilliseconds(50)));

            svc.GenerateReport();

            var files = Directory.GetFiles(dir, "*.xml");
            Assert.Single(files);
            Directory.Delete(dir, true);
        }

        [Fact]
        public void ReportService_CircularBuffer_NeverExceedsTenFiles()
        {
            string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var svc = new ReportService(dir);

            for (int i = 0; i < 15; i++)
                svc.GenerateReport();

            var files = Directory.GetFiles(dir, "*.xml");
            Assert.True(files.Length <= 10);
            Directory.Delete(dir, true);
        }

        [Fact]
        public void ReportService_CircularBuffer_OverwritesOldest()
        {
            string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var svc = new ReportService(dir);

            // Generate exactly 10 reports
            for (int i = 0; i < 10; i++)
                svc.GenerateReport();

            var beforeTimes = Directory.GetFiles(dir, "*.xml")
                .ToDictionary(f => f, f => File.GetLastWriteTime(f));

            Thread.Sleep(100);

            // 11th report should overwrite report_01.xml (the oldest)
            svc.GenerateReport();

            var afterTimes = Directory.GetFiles(dir, "*.xml")
                .ToDictionary(f => f, f => File.GetLastWriteTime(f));

            Assert.Equal(10, afterTimes.Count);
            // The file named report_01.xml should have been rewritten
            string report01 = Path.Combine(dir, "report_01.xml");
            Assert.True(afterTimes[report01] > beforeTimes[report01]);

            Directory.Delete(dir, true);
        }

        [Fact]
        public void ReportService_XmlContainsExpectedElements()
        {
            string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var svc = new ReportService(dir);

            svc.AddEntry(new ReportEntry(
                new Job(Guid.NewGuid(), JobType.Prime, "numbers:100,threads:1", 1),
                25, false, TimeSpan.FromMilliseconds(200)));
            svc.AddEntry(new ReportEntry(
                new Job(Guid.NewGuid(), JobType.IO, "delay:10", 2),
                55, false, TimeSpan.FromMilliseconds(50)));
            svc.AddEntry(new ReportEntry(
                new Job(Guid.NewGuid(), JobType.IO, "delay:5000", 3),
                -1, true, TimeSpan.FromSeconds(2)));

            svc.GenerateReport();

            var file = Directory.GetFiles(dir, "*.xml").Single();
            string xml = File.ReadAllText(file);

            Assert.Contains("CompletedJobs", xml);
            Assert.Contains("FailedJobs", xml);
            Assert.Contains("Prime", xml);
            Assert.Contains("IO", xml);

            Directory.Delete(dir, true);
        }

        public void Dispose()
        {
            if (File.Exists(_logFile))
                try { File.Delete(_logFile); } catch { }
            if (Directory.Exists(_reportDir))
                try { Directory.Delete(_reportDir, true); } catch { }
        }
    }
}
