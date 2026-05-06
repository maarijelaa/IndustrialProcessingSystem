using IndustrialProcessingSystem.Services;
using Xunit;

namespace IndustrialProcessingSystem.Tests
{
    public class JobProcessorTests
    {
        [Fact]
        public void ProcessPrime_Returns_CorrectCount_For_10()
        {
            // Primes up to 10: 2, 3, 5, 7 = 4
            int result = JobProcessor.ProcessPrime("numbers:10,threads:1");
            Assert.Equal(4, result);
        }

        [Fact]
        public void ProcessPrime_Returns_CorrectCount_For_100()
        {
            // There are 25 primes up to 100
            int result = JobProcessor.ProcessPrime("numbers:100,threads:2");
            Assert.Equal(25, result);
        }

        [Fact]
        public void ProcessPrime_ClampsThreads_AboveMax()
        {
            // Threads > 8 should be clamped to 8 - should still return correct result
            int result = JobProcessor.ProcessPrime("numbers:50,threads:20");
            Assert.Equal(15, result); // 15 primes up to 50
        }

        [Fact]
        public void ProcessPrime_ClampsThreads_BelowMin()
        {
            // Threads = 0 should be clamped to 1
            int result = JobProcessor.ProcessPrime("numbers:50,threads:0");
            Assert.Equal(15, result);
        }

        [Fact]
        public void ProcessPrime_WithUnderscoreInPayload()
        {
            // numbers:10_000 should parse to 10000
            int result = JobProcessor.ProcessPrime("numbers:10_000,threads:2");
            Assert.True(result > 0);
        }

        [Fact]
        public void ProcessPrime_ZeroLimit_ReturnsZero()
        {
            int result = JobProcessor.ProcessPrime("numbers:0,threads:1");
            Assert.Equal(0, result);
        }

        [Fact]
        public void ProcessPrime_LimitOf1_ReturnsZero()
        {
            int result = JobProcessor.ProcessPrime("numbers:1,threads:1");
            Assert.Equal(0, result);
        }

        [Fact]
        public void ProcessIO_Returns_ValueBetween0And100()
        {
            int result = JobProcessor.ProcessIO("delay:10");
            Assert.InRange(result, 0, 100);
        }

        [Fact]
        public void ProcessIO_WithUnderscoreDelay()
        {
            int result = JobProcessor.ProcessIO("delay:1_000");
            Assert.InRange(result, 0, 100);
        }

        [Fact]
        public void ProcessPrime_MultipleThreads_ConsistentResult()
        {
            int result1 = JobProcessor.ProcessPrime("numbers:200,threads:1");
            int result2 = JobProcessor.ProcessPrime("numbers:200,threads:4");
            Assert.Equal(result1, result2);
        }
    }
}
