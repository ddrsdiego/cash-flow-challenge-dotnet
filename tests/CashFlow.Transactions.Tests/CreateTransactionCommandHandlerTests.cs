using System;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Application.Utils;
using CashFlow.SharedKernel.Domain.Entities;
using CashFlow.SharedKernel.Domain.Enums;
using CashFlow.SharedKernel.Interfaces;
using CashFlow.Transactions.API.Application.UseCases.CreateTransaction;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CashFlow.Transactions.Tests;

public class CreateTransactionCommandHandlerTests
{
    private readonly Mock<IRawRequestRepository> _rawRequestRepositoryMock;
    private readonly CreateTransactionCommandHandler _handler;

    public CreateTransactionCommandHandlerTests()
    {
        _rawRequestRepositoryMock = new Mock<IRawRequestRepository>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<CreateTransactionCommandHandler>>(MockBehavior.Loose);
        _handler = new CreateTransactionCommandHandler(
            _rawRequestRepositoryMock.Object,
            loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ShouldReturnAccepted_WhenValidRequest()
    {
        // Arrange
        var command = new CreateTransactionCommand(
            TracerId: "trace-123",
            IdempotencyKey: "idempotency-key-1",
            UserId: "user-123",
            Type: TransactionType.Debit,
            Amount: 100.50m,
            Description: "Test transaction",
            Category: Category.Sales,
            Date: DateTime.UtcNow.AddDays(-1));

        _rawRequestRepositoryMock
            .Setup(x => x.GetByIdempotencyKeyAsync(command.IdempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CSharpFunctionalExtensions.Maybe<RawRequest>.None);

        _rawRequestRepositoryMock
            .Setup(x => x.InsertAsync(
                It.IsAny<RawRequest>(),
                It.IsAny<MongoDB.Driver.IClientSessionHandle>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(202);
        response.IsSuccess.Should().BeTrue();
        _rawRequestRepositoryMock.VerifyAll();
    }

    [Fact]
    public async Task Handle_ShouldReturnIdempotent_WhenKeyExists()
    {
        // Arrange
        var existingRequest = new RawRequest
        {
            Id = "request-id-1",
            IdempotencyKey = "idempotency-key-1",
            UserId = "user-123",
            Status = RawRequestStatus.Pending
        };

        var command = new CreateTransactionCommand(
            TracerId: "trace-123",
            IdempotencyKey: "idempotency-key-1",
            UserId: "user-123",
            Type: TransactionType.Credit,
            Amount: 50m,
            Description: "Idempotent test",
            Category: Category.Sales,
            Date: DateTime.UtcNow);

        _rawRequestRepositoryMock
            .Setup(x => x.GetByIdempotencyKeyAsync(command.IdempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CSharpFunctionalExtensions.Maybe<RawRequest>.From(existingRequest));

        // Act
        var response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(202);
        response.IsSuccess.Should().BeTrue();
        _rawRequestRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<RawRequest>(), null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ShouldReturnBadRequest_WhenAmountIsZero()
    {
        // Arrange
        var command = new CreateTransactionCommand(
            TracerId: "trace-123",
            IdempotencyKey: "idempotency-key-2",
            UserId: "user-123",
            Type: TransactionType.Debit,
            Amount: 0m,
            Description: "Invalid amount",
            Category: Category.Sales,
            Date: DateTime.UtcNow);

        // Act
        var response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(400);
        response.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldReturnBadRequest_WhenAmountIsNegative()
    {
        // Arrange
        var command = new CreateTransactionCommand(
            TracerId: "trace-123",
            IdempotencyKey: "idempotency-key-3",
            UserId: "user-123",
            Type: TransactionType.Credit,
            Amount: -50m,
            Description: "Negative amount",
            Category: Category.Sales,
            Date: DateTime.UtcNow);

        // Act
        var response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(400);
        response.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldReturnBadRequest_WhenDescriptionIsEmpty()
    {
        // Arrange
        var command = new CreateTransactionCommand(
            TracerId: "trace-123",
            IdempotencyKey: "idempotency-key-4",
            UserId: "user-123",
            Type: TransactionType.Debit,
            Amount: 100m,
            Description: string.Empty,
            Category: Category.Sales,
            Date: DateTime.UtcNow);

        // Act
        var response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(400);
        response.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldReturnBadRequest_WhenDescriptionExceeds500Chars()
    {
        // Arrange
        var command = new CreateTransactionCommand(
            TracerId: "trace-123",
            IdempotencyKey: "idempotency-key-5",
            UserId: "user-123",
            Type: TransactionType.Credit,
            Amount: 100m,
            Description: new string('a', 501),
            Category: Category.Sales,
            Date: DateTime.UtcNow);

        // Act
        var response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(400);
        response.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldReturnBadRequest_WhenDateIsMoreThan1DayInFuture()
    {
        // Arrange
        var command = new CreateTransactionCommand(
            TracerId: "trace-123",
            IdempotencyKey: "idempotency-key-6",
            UserId: "user-123",
            Type: TransactionType.Debit,
            Amount: 100m,
            Description: "Future date",
            Category: Category.Sales,
            Date: DateTime.UtcNow.AddDays(2));

        // Act
        var response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(400);
        response.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldReturnInternalServerError_WhenRepositoryThrows()
    {
        // Arrange
        var command = new CreateTransactionCommand(
            TracerId: "trace-123",
            IdempotencyKey: "idempotency-key-7",
            UserId: "user-123",
            Type: TransactionType.Credit,
            Amount: 100m,
            Description: "DB error test",
            Category: Category.Sales,
            Date: DateTime.UtcNow);

        _rawRequestRepositoryMock
            .Setup(x => x.GetByIdempotencyKeyAsync(command.IdempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CSharpFunctionalExtensions.Maybe<RawRequest>.None);

        _rawRequestRepositoryMock
            .Setup(x => x.InsertAsync(
                It.IsAny<RawRequest>(),
                It.IsAny<MongoDB.Driver.IClientSessionHandle>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB connection failed"));

        // Act
        var response = await _handler.Handle(command, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(500);
        response.IsFailure.Should().BeTrue();
    }
}
