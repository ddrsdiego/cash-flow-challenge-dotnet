using System;
using System.Linq;
using CashFlow.Integration.Tests.Infrastructure;
using CashFlow.SharedKernel.Interfaces;
using CashFlow.Transactions.API;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CashFlow.Integration.Tests.Factories;

/// <summary>
/// Factory for integrating tests with Transactions API.
/// Sets up WebApplicationFactory with mocked MongoDB repositories and test auth.
/// </summary>
public class TransactionsWebApplicationFactory : WebApplicationFactory<TransactionsApiMarker>
{
    public Mock<IRawRequestIngestionRepository> RawRequestRepositoryMock { get; } = 
        new(MockBehavior.Loose);
    
    public Mock<ITransactionRepository> TransactionRepositoryMock { get; } = 
        new(MockBehavior.Loose);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove real MongoDB repositories
            RemoveService(services, typeof(IRawRequestIngestionRepository));
            RemoveService(services, typeof(ITransactionRepository));

            // Add mocked repositories
            services.AddScoped(_ => RawRequestRepositoryMock.Object);
            services.AddScoped(_ => TransactionRepositoryMock.Object);

            // Replace JWT with test auth handler
            services
                .AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, options => { });

            // Override JWT to use test scheme
            services.PostConfigureAll<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });
        });
    }

    private static void RemoveService(IServiceCollection services, Type type)
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == type);
        if (descriptor != null)
            services.Remove(descriptor);
    }
}
