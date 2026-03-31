using System;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Application.Utils;
using CashFlow.SharedKernel.Domain.Entities;
using CashFlow.SharedKernel.Domain.Enums;
using CashFlow.SharedKernel.Interfaces;
using CashFlow.Transactions.API.Application.UseCases.GetTransactionById;
using CSharpFunctionalExtensions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CashFlow.Transactions.Tests;

public class GetTransactionByIdQueryHandlerTests
{
    private readonly Mock<ITransactionRepository> _repositoryMock;
    private readonly GetTransactionByIdQueryHandler _handler;

    public GetTransactionByIdQueryHandlerTests()
    {
        _repositoryMock = new Mock<ITransactionRepository>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<GetTransactionByIdQueryHandler>>(MockBehavior.Loose);
        _handler = new GetTransactionByIdQueryHandler(_repositoryMock.Object, loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ShouldReturnOk_WhenTransactionFound()
    {
        // Arrange
        var transactionId = "transaction-123";
        var userId = "user-123";
        var transaction = new Transaction
        {
            Id = transactionId,
            UserId = userId,
            Type = TransactionType.Debit,
            Amount = 100.50m,
            Description = "Found transaction",
            Category = Category.Sales,
            Date = DateTime.UtcNow.AddDays(-1),
            CreatedAt = DateTime.UtcNow
        };

        var query = new GetTransactionByIdQuery(
            TracerId: "trace-123",
            TransactionId: transactionId,
            UserId: userId);

        _repositoryMock
            .Setup(x => x.GetByIdAsync(transactionId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Maybe<Transaction>.From(transaction));

        // Act
        var response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(200);
        response.IsSuccess.Should().BeTrue();
        _repositoryMock.VerifyAll();
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFound_WhenTransactionNotExists()
    {
        // Arrange
        var query = new GetTransactionByIdQuery(
            TracerId: "trace-123",
            TransactionId: "nonexistent-id",
            UserId: "user-123");

        _repositoryMock
            .Setup(x => x.GetByIdAsync(query.TransactionId, query.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Maybe<Transaction>.None);

        // Act
        var response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(404);
        response.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldReturnNotFound_WhenUserIdDoesNotMatch()
    {
        // Arrange
        var query = new GetTransactionByIdQuery(
            TracerId: "trace-123",
            TransactionId: "transaction-123",
            UserId: "different-user");

        _repositoryMock
            .Setup(x => x.GetByIdAsync(query.TransactionId, query.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Maybe<Transaction>.None);

        // Act
        var response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(404);
        response.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldReturnInternalServerError_WhenRepositoryThrows()
    {
        // Arrange
        var query = new GetTransactionByIdQuery(
            TracerId: "trace-123",
            TransactionId: "transaction-123",
            UserId: "user-123");

        _repositoryMock
            .Setup(x => x.GetByIdAsync(query.TransactionId, query.UserId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(500);
        response.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldPassUserIdToRepository()
    {
        // Arrange
        var transactionId = "transaction-456";
        var userId = "user-456";
        var transaction = new Transaction
        {
            Id = transactionId,
            UserId = userId,
            Type = TransactionType.Credit,
            Amount = 50m,
            Description = "Test",
            Category = Category.Sales,
            Date = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        var query = new GetTransactionByIdQuery(
            TracerId: "trace-456",
            TransactionId: transactionId,
            UserId: userId);

        _repositoryMock
            .Setup(x => x.GetByIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Maybe<Transaction>.From(transaction));

        // Act
        await _handler.Handle(query, CancellationToken.None);

        // Assert
        _repositoryMock.Verify(
            x => x.GetByIdAsync(transactionId, userId, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
