using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.Consolidation.API.Application.UseCases.UpdateConsolidationCache;
using CashFlow.SharedKernel.Domain.Entities;
using CashFlow.SharedKernel.Domain.ValueObjects;
using CashFlow.SharedKernel.DTOs.Responses;
using CashFlow.SharedKernel.Interfaces;
using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CashFlow.Consolidation.Tests;

public class UpdateConsolidationCacheCommandHandlerTests
{
    private readonly Mock<IConsolidationQueryRepository> _repositoryMock;
    private readonly Mock<IConsolidationCache> _cacheMock;
    private readonly Mock<ILogger<UpdateConsolidationCacheCommandHandler>> _loggerMock;
    private readonly UpdateConsolidationCacheCommandHandler _sut;

    public UpdateConsolidationCacheCommandHandlerTests()
    {
        _repositoryMock = new Mock<IConsolidationQueryRepository>(MockBehavior.Loose);
        _cacheMock = new Mock<IConsolidationCache>(MockBehavior.Loose);
        _loggerMock = new Mock<ILogger<UpdateConsolidationCacheCommandHandler>>(MockBehavior.Loose);
        
        _sut = new UpdateConsolidationCacheCommandHandler(
            _repositoryMock.Object,
            _cacheMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ShouldReturnOk_WhenConsolidationsFound()
    {
        // Arrange
        var command = new UpdateConsolidationCacheCommand(
            TracerId: "trace-123",
            ConsolidationKeys: new List<string>
            {
                "user-123:2024-03-15"
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
        var command = new UpdateConsolidationCacheCommand(
            TracerId: "trace-123",
            ConsolidationKeys: new List<string>());

        // Act
        var response = await _sut.Handle(command, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(400);
        response.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenRepositoryIsNull()
    {
        // Act & Assert
        var act = () => new UpdateConsolidationCacheCommandHandler(
            null,
            _cacheMock.Object,
            _loggerMock.Object);
        
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("consolidationRepository");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenCacheIsNull()
    {
        // Act & Assert
        var act = () => new UpdateConsolidationCacheCommandHandler(
            _repositoryMock.Object,
            null,
            _loggerMock.Object);
        
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("cache");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        // Act & Assert
        var act = () => new UpdateConsolidationCacheCommandHandler(
            _repositoryMock.Object,
            _cacheMock.Object,
            null);
        
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }
}
