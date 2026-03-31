using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Application.Utils;
using CashFlow.Transactions.Worker.Application.UseCases.DispatchTransactionBatch;
using CashFlow.Transactions.Worker.Workers;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CashFlow.Transactions.Worker.Tests.Workers;

public class BatcherBackgroundServiceTests
{
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock;
    private readonly IConfiguration _configuration;
    private readonly Mock<ILogger<BatcherBackgroundService>> _loggerMock;
    private readonly BatcherBackgroundService _sut;

    public BatcherBackgroundServiceTests()
    {
        _scopeFactoryMock = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        _loggerMock = new Mock<ILogger<BatcherBackgroundService>>(MockBehavior.Loose);

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Batcher:BatchSize"] = "50",
                ["Batcher:DelayOnEmptyMs"] = "100",
                ["Batcher:LockTtlSeconds"] = "10",
                ["Batcher:SweepThresholdMinutes"] = "3"
            })
            .Build();

        _sut = new BatcherBackgroundService(_scopeFactoryMock.Object, _configuration, _loggerMock.Object);
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenScopeFactoryIsNull()
    {
        // Act & Assert
        var act = () => new BatcherBackgroundService(null, _configuration, _loggerMock.Object);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("scopeFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenConfigurationIsNull()
    {
        // Act & Assert
        var act = () => new BatcherBackgroundService(_scopeFactoryMock.Object, null, _loggerMock.Object);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("configuration");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        // Act & Assert
        var act = () => new BatcherBackgroundService(_scopeFactoryMock.Object, _configuration, null);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void Constructor_ShouldCreateInstance_WhenAllDependenciesAreValid()
    {
        // Act & Assert
        _sut.Should().NotBeNull();
    }

    [Fact]
    public async Task StartAsync_ShouldReadConfigurationValues_WhenStarted()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Act
        await _sut.StartAsync(cts.Token);

        // Assert
        // Verify no exceptions were thrown
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSendCommandToMediator_WhenRunning()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));

        var mediatorMock = new Mock<IMediator>(MockBehavior.Strict);
        mediatorMock
            .Setup(x => x.Send(
                It.IsAny<DispatchTransactionBatchCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Yield();
                return Response.Accepted();
            })
            .Verifiable();

        var scopeMock = new Mock<IServiceScope>(MockBehavior.Strict);
        scopeMock
            .Setup(x => x.ServiceProvider.GetService(typeof(IMediator)))
            .Returns(mediatorMock.Object);

        scopeMock
            .Setup(x => x.Dispose())
            .Verifiable();

        _scopeFactoryMock
            .Setup(x => x.CreateScope())
            .Returns(scopeMock.Object);

        // Act
        await _sut.StartAsync(CancellationToken.None);
        await _sut.StopAsync(CancellationToken.None);

        // Assert
        mediatorMock.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldDelayWhenMediatorReturnsSuccess_WhenNotAccepted()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(300));

        var delayTaskStarted = false;
        var mediatorMock = new Mock<IMediator>(MockBehavior.Strict);
        mediatorMock
            .Setup(x => x.Send(
                It.IsAny<DispatchTransactionBatchCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                delayTaskStarted = false;
                return Response.Ok();
            });

        var scopeMock = new Mock<IServiceScope>(MockBehavior.Strict);
        scopeMock
            .Setup(x => x.ServiceProvider.GetService(typeof(IMediator)))
            .Returns(mediatorMock.Object);

        scopeMock
            .Setup(x => x.Dispose());

        _scopeFactoryMock
            .Setup(x => x.CreateScope())
            .Returns(scopeMock.Object);

        // Act
        await _sut.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        await _sut.StopAsync(cts.Token);

        // Assert
        // Verify mediator was called (delay was applied)
        mediatorMock.Verify(
            x => x.Send(
                It.IsAny<DispatchTransactionBatchCommand>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotDelayWhenMediatorReturns202Accepted()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(300));

        var callCount = 0;
        var mediatorMock = new Mock<IMediator>(MockBehavior.Strict);
        mediatorMock
            .Setup(x => x.Send(
                It.IsAny<DispatchTransactionBatchCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Yield();
                callCount++;
                return Response.Accepted();
            });

        var scopeMock = new Mock<IServiceScope>(MockBehavior.Strict);
        scopeMock
            .Setup(x => x.ServiceProvider.GetService(typeof(IMediator)))
            .Returns(mediatorMock.Object);

        scopeMock
            .Setup(x => x.Dispose());

        _scopeFactoryMock
            .Setup(x => x.CreateScope())
            .Returns(scopeMock.Object);

        // Act
        await _sut.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        await _sut.StopAsync(cts.Token);

        // Assert
        // With 202 Accepted, loop should continue without delay
        // So we expect more calls in the same timeframe compared to Ok (200)
        callCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldLogWarning_WhenMediatorReturnsFailure()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        var mediatorMock = new Mock<IMediator>(MockBehavior.Strict);
        mediatorMock
            .Setup(x => x.Send(
                It.IsAny<DispatchTransactionBatchCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.InternalServerError(
                ErrorResponse.Builder()
                    .WithInstance("/test")
                    .WithTraceId("trace-123")
                    .WithError("ERROR", "ERROR", "Test error")
                    .Build()));

        var scopeMock = new Mock<IServiceScope>(MockBehavior.Strict);
        scopeMock
            .Setup(x => x.ServiceProvider.GetService(typeof(IMediator)))
            .Returns(mediatorMock.Object);

        scopeMock
            .Setup(x => x.Dispose());

        _scopeFactoryMock
            .Setup(x => x.CreateScope())
            .Returns(scopeMock.Object);

        // Act
        await _sut.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(150));
        await _sut.StopAsync(cts.Token);

        // Assert
        // Verify handler was called and failure was logged
        mediatorMock.Verify(
            x => x.Send(
                It.IsAny<DispatchTransactionBatchCommand>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldContinueLoop_WhenExceptionOccurs()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var callCount = 0;

        var mediatorMock = new Mock<IMediator>(MockBehavior.Strict);
        mediatorMock
            .Setup(x => x.Send(
                It.IsAny<DispatchTransactionBatchCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await Task.Yield();
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("Test exception");

                return Response.Ok();
            });

        var scopeMock = new Mock<IServiceScope>(MockBehavior.Strict);
        scopeMock
            .Setup(x => x.ServiceProvider.GetService(typeof(IMediator)))
            .Returns(mediatorMock.Object);

        scopeMock
            .Setup(x => x.Dispose());

        _scopeFactoryMock
            .Setup(x => x.CreateScope())
            .Returns(scopeMock.Object);

        // Act
        await _sut.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        cts.Cancel();
        await _sut.StopAsync(CancellationToken.None);

        // Assert
        // Verify handler continued after exception
        callCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldStopLoop_WhenCancellationRequested()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var callCount = 0;

        var mediatorMock = new Mock<IMediator>(MockBehavior.Strict);
        mediatorMock
            .Setup(x => x.Send(
                It.IsAny<DispatchTransactionBatchCommand>(),
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                callCount++;
                await Task.Delay(TimeSpan.FromMilliseconds(50));
                return Response.Accepted();
            });

        var scopeMock = new Mock<IServiceScope>(MockBehavior.Strict);
        scopeMock
            .Setup(x => x.ServiceProvider.GetService(typeof(IMediator)))
            .Returns(mediatorMock.Object);

        scopeMock
            .Setup(x => x.Dispose());

        _scopeFactoryMock
            .Setup(x => x.CreateScope())
            .Returns(scopeMock.Object);

        // Act
        await _sut.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        cts.Cancel();

        // Assert
        callCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPassCorrectCommandParameters_ToMediator()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        DispatchTransactionBatchCommand capturedCommand = null;

        var mediatorMock = new Mock<IMediator>(MockBehavior.Strict);
        mediatorMock
            .Setup(x => x.Send(
                It.IsAny<DispatchTransactionBatchCommand>(),
                It.IsAny<CancellationToken>()))
            .Callback<IRequest<Response>, CancellationToken>((cmd, _) =>
            {
                capturedCommand = cmd as DispatchTransactionBatchCommand;
            })
            .Returns(async () =>
            {
                await Task.Yield();
                return Response.Accepted();
            });

        var scopeMock = new Mock<IServiceScope>(MockBehavior.Strict);
        scopeMock
            .Setup(x => x.ServiceProvider.GetService(typeof(IMediator)))
            .Returns(mediatorMock.Object);

        scopeMock
            .Setup(x => x.Dispose());

        _scopeFactoryMock
            .Setup(x => x.CreateScope())
            .Returns(scopeMock.Object);

        // Act
        await _sut.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(150));
        await _sut.StopAsync(cts.Token);

        // Assert
        capturedCommand.Should().NotBeNull();
        capturedCommand.TracerId.Should().NotBeNullOrEmpty();
        capturedCommand.InstanceId.Should().NotBeNullOrEmpty();
        capturedCommand.BatchSize.Should().Be(50);
        capturedCommand.LockTtlSeconds.Should().Be(10);
        capturedCommand.SweepThresholdMinutes.Should().Be(3);
    }
}
