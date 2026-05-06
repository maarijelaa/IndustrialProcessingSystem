using IndustrialProcessingSystem.Models;
using Xunit;

namespace IndustrialProcessingSystem.Tests
{
    public class JobTests
    {
        [Fact]
        public void Job_DefaultConstructor_SetsDefaults()
        {
            var job = new Job();
            Assert.Equal(Guid.Empty, job.Id);
            Assert.Equal(string.Empty, job.Payload);
        }

        [Fact]
        public void Job_ParameterizedConstructor_SetsAllFields()
        {
            var id = Guid.NewGuid();
            var job = new Job(id, JobType.Prime, "numbers:100,threads:2", 1);

            Assert.Equal(id, job.Id);
            Assert.Equal(JobType.Prime, job.Type);
            Assert.Equal("numbers:100,threads:2", job.Payload);
            Assert.Equal(1, job.Priority);
        }

        [Fact]
        public void Job_IOType_IsSet()
        {
            var job = new Job(Guid.NewGuid(), JobType.IO, "delay:500", 3);
            Assert.Equal(JobType.IO, job.Type);
        }

        [Fact]
        public void JobHandle_StoresIdAndTask()
        {
            var id = Guid.NewGuid();
            var tcs = new TaskCompletionSource<int>();
            var handle = new JobHandle(id, tcs.Task);

            Assert.Equal(id, handle.Id);
            Assert.NotNull(handle.Result);
        }

        [Fact]
        public async Task JobHandle_Result_CompletesWithValue()
        {
            var id = Guid.NewGuid();
            var tcs = new TaskCompletionSource<int>();
            var handle = new JobHandle(id, tcs.Task);

            tcs.SetResult(42);
            int result = await handle.Result;
            Assert.Equal(42, result);
        }

        [Fact]
        public void JobType_Enum_HasPrimeAndIO()
        {
            var values = Enum.GetValues<JobType>();
            Assert.Contains(JobType.Prime, values);
            Assert.Contains(JobType.IO, values);
        }
    }
}
