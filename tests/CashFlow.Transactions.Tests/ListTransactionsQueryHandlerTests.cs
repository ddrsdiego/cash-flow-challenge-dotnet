using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CashFlow.SharedKernel.Domain.Entities;
using CashFlow.SharedKernel.Domain.Enums;
using CashFlow.SharedKernel.Interfaces;
using CashFlow.Transactions.API.Application.UseCases.ListTransactions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CashFlow.Transactions.Tests;

public class ListTransactionsQueryHandlerTests
{
    private readonly Mock<ITransactionRepository> _repositoryMock;
    private readonly ListTransactionsQueryHandler _handler;

    public ListTransactionsQueryHandlerTests()
    {
        _repositoryMock = new Mock<ITransactionRepository>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<ListTransactionsQueryHandler>>(MockBehavior.Loose);
        _handler = new ListTransactionsQueryHandler(_repositoryMock.Object, loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ShouldReturnOk_WhenTransactionsFound()
    {
        // Arrange
        var userId = "user-123";
        var startDate = DateTime.UtcNow.AddDays(-30).Date;
        var endDate = DateTime.UtcNow.Date;
        var transactions = new List<Transaction>
        {
            new Transaction { Id = "t1", UserId = userId, Type = TransactionType.Debit, Amount = 100m, Category = Category.Sales, Description = "Test", Date = DateTime.UtcNow, CreatedAt = DateTime.UtcNow }
        };

        var query = new ListTransactionsQuery(
            TracerId: "trace-123",
            UserId: userId,
            StartDate: startDate,
            EndDate: endDate,
            Type: null,
            Page: 1,
            PageSize: 20);

        _repositoryMock
            .Setup(x => x.GetByPeriodAsync(startDate, endDate, null, userId, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Transaction>)transactions);

        _repositoryMock
            .Setup(x => x.CountByPeriodAsync(startDate, endDate, null, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);

        // Act
        var response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(200);
        response.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldReturnBadRequest_WhenStartDateGreaterThanEndDate()
    {
        // Arrange
        var query = new ListTransactionsQuery(
            TracerId: "trace-123",
            UserId: "user-123",
            StartDate: DateTime.UtcNow.Date,
            EndDate: DateTime.UtcNow.AddDays(-1).Date,
            Type: null,
            Page: 1,
            PageSize: 20);

        // Act
        var response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(400);
        response.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldReturnBadRequest_WhenUserIdIsNull()
    {
        // Arrange
        var query = new ListTransactionsQuery(
            TracerId: "trace-123",
            UserId: null,
            StartDate: DateTime.UtcNow.AddDays(-30).Date,
            EndDate: DateTime.UtcNow.Date,
            Type: null,
            Page: 1,
            PageSize: 20);

        // Act
        var response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(400);
        response.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldReturnOk_WhenNoTransactionsFound()
    {
        // Arrange
        var userId = "user-456";
        var startDate = DateTime.UtcNow.AddDays(-30).Date;
        var endDate = DateTime.UtcNow.Date;

        var query = new ListTransactionsQuery(
            TracerId: "trace-123",
            UserId: userId,
            StartDate: startDate,
            EndDate: endDate,
            Type: TransactionType.Debit,
            Page: 1,
            PageSize: 20);

        _repositoryMock
            .Setup(x => x.GetByPeriodAsync(startDate, endDate, TransactionType.Debit, userId, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Transaction>());

        _repositoryMock
            .Setup(x => x.CountByPeriodAsync(startDate, endDate, TransactionType.Debit, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);

        // Act
        var response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(200);
        response.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ShouldReturnInternalServerError_WhenRepositoryThrows()
    {
        // Arrange
        var query = new ListTransactionsQuery(
            TracerId: "trace-123",
            UserId: "user-123",
            StartDate: DateTime.UtcNow.AddDays(-30).Date,
            EndDate: DateTime.UtcNow.Date,
            Type: null,
            Page: 1,
            PageSize: 20);

        _repositoryMock
            .Setup(x => x.GetByPeriodAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<TransactionType?>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB error"));

        // Act
        var response = await _handler.Handle(query, CancellationToken.None);

        // Assert
        response.StatusCode.Should().Be(500);
        response.IsFailure.Should().BeTrue();
    }
}
