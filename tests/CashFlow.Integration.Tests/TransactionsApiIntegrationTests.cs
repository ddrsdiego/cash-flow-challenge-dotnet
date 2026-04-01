namespace CashFlow.Integration.Tests;

public class TransactionsApiIntegrationTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private Factories.TransactionsWebApplicationFactory _factory = null!;
    private HttpClient _httpClient = null!;

    public async Task InitializeAsync()
    {
        _factory = new Factories.TransactionsWebApplicationFactory();
        _httpClient = _factory.CreateClient();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _httpClient?.Dispose();
        await _factory.DisposeAsync();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task CreateTransaction_ShouldReturn202Accepted_WhenRequestIsValid()
    {
        // Arrange
        var request = new
        {
            federalTaxId = "12345678000190",
            transactionType = "DEBIT",
            amount = 1000.50m,
            description = "Test transaction",
            referenceId = "REF-001"
        };

        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response = await _httpClient.PostAsync("/api/v1/transactions", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task CreateTransaction_ShouldReturn400BadRequest_WhenAmountIsZero()
    {
        // Arrange
        var request = new
        {
            federalTaxId = "12345678000190",
            transactionType = "DEBIT",
            amount = 0m,
            description = "Invalid transaction",
            referenceId = "REF-002"
        };

        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response = await _httpClient.PostAsync("/api/v1/transactions", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetTransactionById_ShouldReturn404NotFound_WhenTransactionDoesNotExist()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid().ToString();

        // Act
        var response = await _httpClient.GetAsync($"/api/v1/transactions/{nonExistentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
