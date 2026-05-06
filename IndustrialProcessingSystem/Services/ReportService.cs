using IndustrialProcessingSystem.Models;
using System.Xml.Linq;

namespace IndustrialProcessingSystem.Services
{
    public class ReportEntry
    {
        public Job Job { get; set; }
        public int Result { get; set; }
        public bool Failed { get; set; }
        public TimeSpan ExecutionTime { get; set; }

        public ReportEntry(Job job, int result, bool failed, TimeSpan executionTime)
        {
            Job = job;
            Result = result;
            Failed = failed;
            ExecutionTime = executionTime;
        }
    }

    public class ReportService
    {
        private readonly string _reportDirectory;
        private readonly List<ReportEntry> _entries = new();
        private readonly object _lock = new object();
        private readonly int _maxReports = 10;
        private int _reportIndex = 0;

        public ReportService(string reportDirectory)
        {
            _reportDirectory = reportDirectory;
            Directory.CreateDirectory(reportDirectory);
        }

        public void AddEntry(ReportEntry entry)
        {
            lock (_lock)
            {
                _entries.Add(entry);
            }
        }

        public void GenerateReport()
        {
            List<ReportEntry> snapshot;
            lock (_lock)
            {
                snapshot = new List<ReportEntry>(_entries);
            }

            var completedByType = snapshot
                .Where(e => !e.Failed)
                .GroupBy(e => e.Job.Type)
                .Select(g => new
                {
                    Type = g.Key,
                    Count = g.Count(),
                    AvgTime = g.Average(e => e.ExecutionTime.TotalMilliseconds)
                })
                .ToList();

            var failedByType = snapshot
                .Where(e => e.Failed)
                .GroupBy(e => e.Job.Type)
                .OrderBy(g => g.Key.ToString())
                .Select(g => new
                {
                    Type = g.Key,
                    Count = g.Count()
                })
                .ToList();

            var doc = new XDocument(
                new XElement("Report",
                    new XAttribute("GeneratedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
                    new XElement("CompletedJobs",
                        completedByType.Select(t =>
                            new XElement("JobType",
                                new XAttribute("Type", t.Type.ToString()),
                                new XAttribute("Count", t.Count),
                                new XAttribute("AvgExecutionTimeMs", Math.Round(t.AvgTime, 2))
                            )
                        )
                    ),
                    new XElement("FailedJobs",
                        failedByType.Select(t =>
                            new XElement("JobType",
                                new XAttribute("Type", t.Type.ToString()),
                                new XAttribute("Count", t.Count)
                            )
                        )
                    )
                )
            );

            // Circular buffer: overwrite oldest after 10 reports
            int fileNumber = (_reportIndex % _maxReports) + 1;
            string filePath = Path.Combine(_reportDirectory, $"report_{fileNumber:D2}.xml");
            doc.Save(filePath);
            _reportIndex++;
        }

        public void StartPeriodicReporting(CancellationToken cancellationToken)
        {
            Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                        GenerateReport();
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ReportService] Error generating report: {ex.Message}");
                    }
                }
            }, cancellationToken);
        }
    }
}
