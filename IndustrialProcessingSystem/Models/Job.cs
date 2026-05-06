namespace IndustrialProcessingSystem.Models
{
    public class Job
    {
        public Guid Id { get; set; }
        public JobType Type { get; set; }
        public string Payload { get; set; } = string.Empty;
        public int Priority { get; set; }

        public Job() { }

        public Job(Guid id, JobType type, string payload, int priority)
        {
            Id = id;
            Type = type;
            Payload = payload;
            Priority = priority;
        }
    }
}
