using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph;
using SchedulingAgent.Bot;
using SchedulingAgent.Models;
using SchedulingAgent.Services;

namespace SchedulingAgent.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSchedulingAgent(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Configuration
        services.Configure<CosmosDbOptions>(configuration.GetSection(CosmosDbOptions.SectionName));
        services.Configure<AzureOpenAIOptions>(configuration.GetSection(AzureOpenAIOptions.SectionName));
        services.Configure<GraphOptions>(configuration.GetSection(GraphOptions.SectionName));
        services.Configure<BotOptions>(configuration.GetSection(BotOptions.SectionName));
        services.Configure<ConflictResolutionOptions>(configuration.GetSection(ConflictResolutionOptions.SectionName));

        // Azure Cosmos DB
        services.AddSingleton(sp =>
        {
            var cosmosOptions = configuration.GetSection(CosmosDbOptions.SectionName).Get<CosmosDbOptions>()!;
            var clientOptions = new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                }
            };
            return new CosmosClient(cosmosOptions.Endpoint, new DefaultAzureCredential(), clientOptions);
        });

        // Azure OpenAI
        services.AddSingleton(sp =>
        {
            var openAIOptions = configuration.GetSection(AzureOpenAIOptions.SectionName).Get<AzureOpenAIOptions>()!;
            return new AzureOpenAIClient(new Uri(openAIOptions.Endpoint), new DefaultAzureCredential());
        });

        // Microsoft Graph
        services.AddSingleton(sp =>
        {
            var graphOptions = configuration.GetSection(GraphOptions.SectionName).Get<GraphOptions>()!;
            var credential = new ClientSecretCredential(
                graphOptions.TenantId, graphOptions.ClientId, graphOptions.ClientSecret);
            return new GraphServiceClient(credential);
        });

        // Bot Framework
        services.AddSingleton<BotFrameworkAuthentication, ConfigurationBotFrameworkAuthentication>();
        services.AddSingleton<IBotFrameworkHttpAdapter, AdapterWithErrorHandler>();
        services.AddTransient<IBot, SchedulingBot>();

        // Application services
        services.AddSingleton<ICosmosDbService, CosmosDbService>();
        services.AddSingleton<IGraphService, GraphService>();
        services.AddSingleton<IOpenAIService, OpenAIService>();
        services.AddSingleton<IConflictResolutionService, ConflictResolutionService>();
        services.AddSingleton<ISchedulingOrchestrator, SchedulingOrchestrator>();

        return services;
    }
}
