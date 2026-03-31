using System;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.Consolidation.API.Application.UseCases.GetDailyConsolidation;
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

public class GetDailyConsolidationQueryHandlerTests
{
    private readonly Mock<IConsolidationCache> _cacheMock;
    private readonly Mock<IConsolidationQueryRepository> _repositoryMock;
    private readonly Mock<ILogger<GetDailyConsolidationQueryHandler>> _loggerMock;
    private readonly GetDailyConsolidationQueryHandler _sut;

    public GetDailyConsolidationQueryHandlerTests()
    {
        _cacheMock = new Mock<IConsolidationCache>(MockBehavior.Strict);
        _repositoryMock = new Mock<IConsolidationQueryRepository>(MockBehavior.Strict);
        _loggerMock = new Mock<ILogger<GetDailyConsolidationQueryHandler>>(MockBehavior.Loose);
        
        _sut = new GetDailyConsolidationQueryHandler(
            _cacheMock.Object,
            _repositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ShouldReturnOk_WhenCacheHit()
    {
        // Arrange
        var query = new GetDailyConsolidationQuery(
            TracerId: "trace-123",
            UserId: "user-123",
            Date: DateTime.UtcNow.Date);

        var cachedResponse = new DailyConsolidationResponse(
            query.Date,
            TotalCredits: 1500.00m,
            TotalDebits: 800.50m,
            Balance: 699.50m,
            TransactionCount: 12,
            LastUpdated: DateTime.UtcNow);

        _cacheMock
            .Setup(x => x.GetAsync(It.IsAny<ConsolidationKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Maybe<DailyConsolidationResponse>.From(cachedResponse));

        // Act
        var response = await _sut.Handle(query, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(200);
        response.IsSuccess.Should().BeTrue();
        response.Data.Should().BeEquivalentTo(cachedResponse);
        _cacheMock.VerifyAll();
        _repositoryMock.Verify(x => x.FindByKeyAsync(It.IsAny<ConsolidationKey>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ShouldReturnOk_WhenCacheMissAndDbFound()
    {
        // Arrange
        var query = new GetDailyConsolidationQuery(
            TracerId: "trace-123",
            UserId: "user-123",
            Date: DateTime.UtcNow.Date);

        var dailyBalances = new DailyBalances
        {
            Id = "consol-123",
            UserId = query.UserId,
            Date = query.Date,
            TotalCredits = 1500.00m,
            TotalDebits = 800.50m,
            Balance = 699.50m,
            TransactionCount = 12,
            LastUpdated = DateTime.UtcNow
        };

        _cacheMock
            .Setup(x => x.GetAsync(It.IsAny<ConsolidationKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Maybe<DailyConsolidationResponse>.None);

        _repositoryMock
            .Setup(x => x.FindByKeyAsync(It.IsAny<ConsolidationKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Maybe<DailyBalances>.From(dailyBalances));

        _cacheMock
            .Setup(x => x.SetAsync(It.IsAny<ConsolidationKey>(), It.IsAny<DailyConsolidationResponse>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask());

        // Act
        var response = await _sut.Handle(query, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(200);
        response.IsSuccess.Should().BeTrue();
        _cacheMock.VerifyAll();
        _repositoryMock.VerifyAll();
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFound_WhenCacheMissAndDbNotFound()
    {
        // Arrange
        var query = new GetDailyConsolidationQuery(
            TracerId: "trace-123",
            UserId: "user-123",
            Date: DateTime.UtcNow.Date);

        _cacheMock
            .Setup(x => x.GetAsync(It.IsAny<ConsolidationKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Maybe<DailyConsolidationResponse>.None);

        _repositoryMock
            .Setup(x => x.FindByKeyAsync(It.IsAny<ConsolidationKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Maybe<DailyBalances>.None);

        // Act
        var response = await _sut.Handle(query, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(404);
        response.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldReturnBadRequest_WhenUserIdIsNull()
    {
        // Arrange
        var query = new GetDailyConsolidationQuery(
            TracerId: "trace-123",
            UserId: null,
            Date: DateTime.UtcNow.Date);

        // Act
        var response = await _sut.Handle(query, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(400);
        response.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldReturnBadRequest_WhenDateIsDefault()
    {
        // Arrange
        var query = new GetDailyConsolidationQuery(
            TracerId: "trace-123",
            UserId: "user-123",
            Date: default);

        // Act
        var response = await _sut.Handle(query, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(400);
        response.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldReturnInternalServerError_WhenRepositoryThrows()
    {
        // Arrange
        var query = new GetDailyConsolidationQuery(
            TracerId: "trace-123",
            UserId: "user-123",
            Date: DateTime.UtcNow.Date);

        _cacheMock
            .Setup(x => x.GetAsync(It.IsAny<ConsolidationKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Maybe<DailyConsolidationResponse>.None);

        _repositoryMock
            .Setup(x => x.FindByKeyAsync(It.IsAny<ConsolidationKey>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database connection failed"));

        // Act
        var response = await _sut.Handle(query, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(500);
        response.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldPopulateCache_WhenCacheMissAndDbFound()
    {
        // Arrange
        var query = new GetDailyConsolidationQuery(
            TracerId: "trace-123",
            UserId: "user-123",
            Date: DateTime.UtcNow.Date);

        var dailyBalances = new DailyBalances
        {
            Id = "consol-123",
            UserId = query.UserId,
            Date = query.Date,
            TotalCredits = 1500.00m,
            TotalDebits = 800.50m,
            Balance = 699.50m,
            TransactionCount = 12,
            LastUpdated = DateTime.UtcNow
        };

        _cacheMock
            .Setup(x => x.GetAsync(It.IsAny<ConsolidationKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Maybe<DailyConsolidationResponse>.None);

        _repositoryMock
            .Setup(x => x.FindByKeyAsync(It.IsAny<ConsolidationKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Maybe<DailyBalances>.From(dailyBalances));

        _cacheMock
            .Setup(x => x.SetAsync(It.IsAny<ConsolidationKey>(), It.IsAny<DailyConsolidationResponse>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask());

        // Act
        await _sut.Handle(query, CancellationToken.None);

        // Assert
        _cacheMock.Verify(
            x => x.SetAsync(It.IsAny<ConsolidationKey>(), It.IsAny<DailyConsolidationResponse>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenCacheIsNull()
    {
        // Act & Assert
        var act = () => new GetDailyConsolidationQueryHandler(
            null,
            _repositoryMock.Object,
            _loggerMock.Object);
        
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("cache");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenRepositoryIsNull()
    {
        // Act & Assert
        var act = () => new GetDailyConsolidationQueryHandler(
            _cacheMock.Object,
            null,
            _loggerMock.Object);
        
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("consolidationRepository");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        // Act & Assert
        var act = () => new GetDailyConsolidationQueryHandler(
            _cacheMock.Object,
            _repositoryMock.Object,
            null);
        
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }
}
