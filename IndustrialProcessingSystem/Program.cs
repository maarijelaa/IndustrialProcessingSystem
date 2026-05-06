using IndustrialProcessingSystem.Config;
using IndustrialProcessingSystem.Models;
using IndustrialProcessingSystem.Services;

namespace IndustrialProcessingSystem
{
    class Program
    {
        static void Main(string[] args)
        {
            // Load config from XML
            string configPath = "SystemConfig.xml";
            if (args.Length > 0)
                configPath = args[0];

            if (!File.Exists(configPath))
            {
                Console.WriteLine($"Config file not found: {configPath}");
                Console.WriteLine("Creating default SystemConfig.xml...");
                CreateDefaultConfig(configPath);
            }

            SystemConfig config;
            try
            {
                config = SystemConfig.LoadFromXml(configPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load config: {ex.Message}");
                return;
            }

            Console.WriteLine($"[Main] Starting system: {config.WorkerCount} workers, max queue {config.MaxQueueSize}");

            using var processingSystem = new ProcessingSystem(
                config.WorkerCount,
                config.MaxQueueSize,
                logFilePath: "job_log.txt",
                reportDirectory: "reports"
            );

            // Load initial jobs from XML
            Console.WriteLine($"[Main] Loading {config.InitialJobs.Count} initial jobs from config...");
            foreach (var job in config.InitialJobs)
            {
                try
                {
                    var handle = processingSystem.Submit(job);
                    Console.WriteLine($"[Main] Submitted initial job {job.Id} (Type={job.Type}, Priority={job.Priority})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Main] Could not submit initial job: {ex.Message}");
                }
            }

            // Start producer threads that randomly add new jobs
            int producerCount = config.WorkerCount;
            var producerThreads = new List<Thread>();
            var cts = new CancellationTokenSource();

            for (int i = 0; i < producerCount; i++)
            {
                int threadId = i;
                var producerThread = new Thread(() =>
                {
                    ProducerLoop(processingSystem, threadId, cts.Token);
                })
                {
                    Name = $"Producer-{i + 1}",
                    IsBackground = true
                };
                producerThreads.Add(producerThread);
                producerThread.Start();
            }

            Console.WriteLine($"[Main] Started {producerCount} producer threads. Press Enter to stop...");
            Console.ReadLine();

            cts.Cancel();
            Console.WriteLine("[Main] Stopping...");

            // Wait for producers to finish
            foreach (var t in producerThreads)
                t.Join(2000);

            Console.WriteLine("[Main] System stopped.");
        }

        static void ProducerLoop(ProcessingSystem system, int threadId, CancellationToken token)
        {
            var rng = new Random(threadId * 1000 + Environment.TickCount);
            string[] jobTypes = { "Prime", "IO" };
            string[] primePayloads = { "numbers:5_000,threads:2", "numbers:10_000,threads:3", "numbers:20_000,threads:4" };
            string[] ioPayloads = { "delay:500", "delay:1_000", "delay:3_000" };

            while (!token.IsCancellationRequested)
            {
                try
                {
                    string typeStr = jobTypes[rng.Next(jobTypes.Length)];
                    string payload;
                    JobType jobType;

                    if (typeStr == "Prime")
                    {
                        jobType = JobType.Prime;
                        payload = primePayloads[rng.Next(primePayloads.Length)];
                    }
                    else
                    {
                        jobType = JobType.IO;
                        payload = ioPayloads[rng.Next(ioPayloads.Length)];
                    }

                    int priority = rng.Next(1, 6); // priorities 1-5
                    var job = new Job(Guid.NewGuid(), jobType, payload, priority);

                    var handle = system.Submit(job);
                    Console.WriteLine($"[Producer-{threadId}] Submitted job {job.Id} (Type={job.Type}, Priority={job.Priority})");

                    // Sleep random time before next submission
                    int sleepMs = rng.Next(200, 1500);
                    Thread.Sleep(sleepMs);
                }
                catch (InvalidOperationException ex)
                {
                    Console.WriteLine($"[Producer-{threadId}] Job rejected: {ex.Message}");
                    Thread.Sleep(500);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Producer-{threadId}] Error: {ex.Message}");
                }
            }
        }

        static void CreateDefaultConfig(string path)
        {
            var xml = """
<?xml version="1.0" encoding="utf-8"?>
<SystemConfig>
  <WorkerCount>5</WorkerCount>
  <MaxQueueSize>100</MaxQueueSize>
  <Jobs>
    <Job Type="Prime" Payload="numbers:10_000,threads:3" Priority="1"/>
    <Job Type="Prime" Payload="numbers:20_000,threads:2" Priority="2"/>
    <Job Type="IO" Payload="delay:1_000" Priority="3"/>
    <Job Type="IO" Payload="delay:3_000" Priority="3"/>
    <Job Type="IO" Payload="delay:15_000" Priority="3"/>
  </Jobs>
</SystemConfig>
""";
            File.WriteAllText(path, xml);
        }
    }
}
