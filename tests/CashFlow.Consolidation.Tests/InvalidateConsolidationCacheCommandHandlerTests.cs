using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.Consolidation.API.Application.UseCases.InvalidateConsolidationCache;
using CashFlow.SharedKernel.Domain.ValueObjects;
using CashFlow.SharedKernel.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CashFlow.Consolidation.Tests;

public class InvalidateConsolidationCacheCommandHandlerTests
{
    private readonly Mock<IConsolidationCache> _cacheMock;
    private readonly Mock<ILogger<InvalidateConsolidationCacheCommandHandler>> _loggerMock;
    private readonly InvalidateConsolidationCacheCommandHandler _sut;

    public InvalidateConsolidationCacheCommandHandlerTests()
    {
        _cacheMock = new Mock<IConsolidationCache>(MockBehavior.Loose);
        _loggerMock = new Mock<ILogger<InvalidateConsolidationCacheCommandHandler>>(MockBehavior.Loose);
        
        _sut = new InvalidateConsolidationCacheCommandHandler(
            _cacheMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ShouldReturnOk_WhenKeysAreValidAndInvalidated()
    {
        // Arrange
        var command = new InvalidateConsolidationCacheCommand(
            TracerId: "trace-123",
            ConsolidationKeys: new List<string>
            {
                "user-123:2024-03-15",
                "user-123:2024-03-14"
            });

        // Act
        var response = await _sut.Handle(command, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(200);
        response.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldReturnBadRequest_WhenConsolidationKeysIsEmpty()
    {
        // Arrange
        var command = new InvalidateConsolidationCacheCommand(
            TracerId: "trace-123",
            ConsolidationKeys: new List<string>());

        // Act
        var response = await _sut.Handle(command, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(400);
        response.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenCacheIsNull()
    {
        // Act & Assert
        var act = () => new InvalidateConsolidationCacheCommandHandler(
            null,
            _loggerMock.Object);
        
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("cache");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        // Act & Assert
        var act = () => new InvalidateConsolidationCacheCommandHandler(
            _cacheMock.Object,
            null);
        
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }
}
