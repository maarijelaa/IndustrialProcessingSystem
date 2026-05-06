using IndustrialProcessingSystem.Models;

namespace IndustrialProcessingSystem.Services
{
    public class JobEventArgs : EventArgs
    {
        public Job Job { get; }
        public int Result { get; }
        public string Status { get; }

        public JobEventArgs(Job job, int result, string status)
        {
            Job = job;
            Result = result;
            Status = status;
        }
    }
}
