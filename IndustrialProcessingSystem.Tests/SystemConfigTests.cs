using IndustrialProcessingSystem.Config;
using IndustrialProcessingSystem.Models;
using Xunit;

namespace IndustrialProcessingSystem.Tests
{
    public class SystemConfigTests : IDisposable
    {
        private readonly string _tempFile;

        public SystemConfigTests()
        {
            _tempFile = Path.GetTempFileName() + ".xml";
        }

        private void WriteConfig(string xml)
        {
            File.WriteAllText(_tempFile, xml);
        }

        [Fact]
        public void LoadFromXml_ParsesWorkerCountAndMaxQueueSize()
        {
            WriteConfig("""
<?xml version="1.0"?>
<SystemConfig>
  <WorkerCount>3</WorkerCount>
  <MaxQueueSize>50</MaxQueueSize>
  <Jobs></Jobs>
</SystemConfig>
""");
            var config = SystemConfig.LoadFromXml(_tempFile);
            Assert.Equal(3, config.WorkerCount);
            Assert.Equal(50, config.MaxQueueSize);
        }

        [Fact]
        public void LoadFromXml_ParsesJobs()
        {
            WriteConfig("""
<?xml version="1.0"?>
<SystemConfig>
  <WorkerCount>2</WorkerCount>
  <MaxQueueSize>10</MaxQueueSize>
  <Jobs>
    <Job Type="Prime" Payload="numbers:100,threads:2" Priority="1"/>
    <Job Type="IO" Payload="delay:500" Priority="2"/>
  </Jobs>
</SystemConfig>
""");
            var config = SystemConfig.LoadFromXml(_tempFile);
            Assert.Equal(2, config.InitialJobs.Count);
            Assert.Equal(JobType.Prime, config.InitialJobs[0].Type);
            Assert.Equal(1, config.InitialJobs[0].Priority);
            Assert.Equal(JobType.IO, config.InitialJobs[1].Type);
        }

        [Fact]
        public void LoadFromXml_AssignsNewGuidsToJobs()
        {
            WriteConfig("""
<?xml version="1.0"?>
<SystemConfig>
  <WorkerCount>1</WorkerCount>
  <MaxQueueSize>10</MaxQueueSize>
  <Jobs>
    <Job Type="IO" Payload="delay:100" Priority="1"/>
    <Job Type="IO" Payload="delay:200" Priority="2"/>
  </Jobs>
</SystemConfig>
""");
            var config = SystemConfig.LoadFromXml(_tempFile);
            Assert.NotEqual(config.InitialJobs[0].Id, config.InitialJobs[1].Id);
            Assert.NotEqual(Guid.Empty, config.InitialJobs[0].Id);
        }

        [Fact]
        public void LoadFromXml_SampleXml_HasFiveJobs()
        {
            WriteConfig("""
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
""");
            var config = SystemConfig.LoadFromXml(_tempFile);
            Assert.Equal(5, config.WorkerCount);
            Assert.Equal(100, config.MaxQueueSize);
            Assert.Equal(5, config.InitialJobs.Count);
        }

        public void Dispose()
        {
            if (File.Exists(_tempFile))
                File.Delete(_tempFile);
        }
    }
}
