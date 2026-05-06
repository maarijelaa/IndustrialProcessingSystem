namespace IndustrialProcessingSystem.Models
{
    public class JobHandle
    {
        public Guid Id { get; set; }
        public Task<int> Result { get; set; }

        public JobHandle(Guid id, Task<int> result)
        {
            Id = id;
            Result = result;
        }
    }
}
