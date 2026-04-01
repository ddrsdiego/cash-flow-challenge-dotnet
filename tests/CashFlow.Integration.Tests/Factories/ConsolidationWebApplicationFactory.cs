using System;
using System.Linq;
using CashFlow.Consolidation.API;
using CashFlow.Integration.Tests.Infrastructure;
using CashFlow.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CashFlow.Integration.Tests.Factories;

/// <summary>
/// Factory for integration tests with Consolidation API.
/// Sets up WebApplicationFactory with mocked MongoDB repositories, cache, and test auth.
/// </summary>
public class ConsolidationWebApplicationFactory :
    WebApplicationFactory<ConsolidationApiMarker>
{
    public Mock<IConsolidationQueryRepository> QueryRepositoryMock { get; } = 
        new(MockBehavior.Loose);
    
    public Mock<IConsolidationCache> CacheMock { get; } = 
        new(MockBehavior.Loose);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Testing");
        
        builder.ConfigureServices(services =>
        {
            // Remove real MongoDB repositories
            RemoveService(services, typeof(IConsolidationQueryRepository));
            
            // Remove MassTransit HostedService specifically (which depends on IBusDepot)
            RemoveService(services, typeof(Microsoft.Extensions.Hosting.IHostedService));
            
            // Add mocked repositories
            services.AddScoped(_ => QueryRepositoryMock.Object);
            
            // Override cache
            RemoveService(services, typeof(IConsolidationCache));
            services.AddScoped(_ => CacheMock.Object);

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
        var descriptors = services.Where(d => d.ServiceType == type).ToList();
        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }
    }
}
