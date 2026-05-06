using IndustrialProcessingSystem.Models;
using System.Xml.Linq;

namespace IndustrialProcessingSystem.Config
{
    public class SystemConfig
    {
        public int WorkerCount { get; set; }
        public int MaxQueueSize { get; set; }
        public List<Job> InitialJobs { get; set; } = new List<Job>();

        public static SystemConfig LoadFromXml(string filePath)
        {
            var config = new SystemConfig();
            var doc = XDocument.Load(filePath);
            var root = doc.Root!;

            config.WorkerCount = int.Parse(root.Element("WorkerCount")!.Value);
            config.MaxQueueSize = int.Parse(root.Element("MaxQueueSize")!.Value);

            var jobsElement = root.Element("Jobs");
            if (jobsElement != null)
            {
                foreach (var jobElement in jobsElement.Elements("Job"))
                {
                    var typeStr = jobElement.Attribute("Type")!.Value;
                    var jobType = Enum.Parse<JobType>(typeStr, ignoreCase: true);
                    var payload = jobElement.Attribute("Payload")!.Value;
                    var priority = int.Parse(jobElement.Attribute("Priority")!.Value);

                    config.InitialJobs.Add(new Job(Guid.NewGuid(), jobType, payload, priority));
                }
            }

            return config;
        }
    }
}
