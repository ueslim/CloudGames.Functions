using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

public class FunctionsTests
{
    [Fact]
    public async Task PaymentsEventsConsumer_CanBeInstantiated()
    {
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(l => l.CreateLogger(It.IsAny<string>())).Returns(Mock.Of<ILogger>());
        var httpClientFactory = new Mock<IHttpClientFactory>();
        var configuration = new Mock<IConfiguration>();
        
        var func = new CloudGames.Functions.Payments.PaymentsEventsConsumer(
            httpClientFactory.Object, 
            loggerFactory.Object, 
            configuration.Object);
        
        func.Should().NotBeNull();
    }
}
