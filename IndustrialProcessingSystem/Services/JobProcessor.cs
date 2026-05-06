using IndustrialProcessingSystem.Models;

namespace IndustrialProcessingSystem.Services
{
    public static class JobProcessor
    {
        public static int ProcessPrime(string payload)
        {
            // Parse payload: "numbers:10_000,threads:3"
            var parts = payload.Split(',');
            int limit = 0;
            int threads = 1;

            foreach (var part in parts)
            {
                var kv = part.Split(':');
                if (kv[0].Trim() == "numbers")
                    limit = int.Parse(kv[1].Trim().Replace("_", ""));
                else if (kv[0].Trim() == "threads")
                    threads = int.Parse(kv[1].Trim());
            }

            // Clamp threads to [1, 8]
            threads = Math.Clamp(threads, 1, 8);

            return CountPrimesParallel(limit, threads);
        }

        private static int CountPrimesParallel(int limit, int threads)
        {
            if (limit < 2) return 0;

            int count = 0;
            object lockObj = new object();

            // Split work into chunks for each thread
            int chunkSize = (limit - 2) / threads + 1;
            var tasks = new List<Task>();

            for (int t = 0; t < threads; t++)
            {
                int start = 2 + t * chunkSize;
                int end = Math.Min(start + chunkSize - 1, limit);

                if (start > limit) break;

                int localStart = start;
                int localEnd = end;

                tasks.Add(Task.Run(() =>
                {
                    int localCount = 0;
                    for (int n = localStart; n <= localEnd; n++)
                    {
                        if (IsPrime(n))
                            localCount++;
                    }
                    lock (lockObj)
                    {
                        count += localCount;
                    }
                }));
            }

            Task.WaitAll(tasks.ToArray());
            return count;
        }

        private static bool IsPrime(int n)
        {
            if (n < 2) return false;
            if (n == 2) return true;
            if (n % 2 == 0) return false;
            for (int i = 3; i * i <= n; i += 2)
                if (n % i == 0) return false;
            return true;
        }

        public static int ProcessIO(string payload)
        {
            // Parse payload: "delay:1_000"
            var parts = payload.Split(':');
            int delay = int.Parse(parts[1].Trim().Replace("_", ""));

            Thread.Sleep(delay);

            var rng = new Random();
            return rng.Next(0, 101);
        }
    }
}
