using EstateDataCollection.Application;
using EstateDataCollection.AzureFunctions.Public.Middleware;
using EstateDataCollection.AzureFunctions.Public.OpenApi;
using EstateDataCollection.Common.Web;
using EstateDataCollection.Domain;
using EstateDataCollection.Infrastructure;
using EstateDataCollection.Infrastructure.Monitoring;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();
builder.UseFunctionsMiddleware();
builder.UseMiddleware<ActorContextMiddleware>();

builder.Services.AddLogging(builder.Configuration, builder.Logging);
builder.Services.AddFunctions(typeof(Program).Assembly);
builder.Services.AddPublicOpenApiDocs(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddDomain();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddMonitoring(
    builder.Configuration,
    builder.Logging,
    new MonitoringOptions { ServiceName = "estate-data-collection-public" });

var app = builder.Build();
app.Run();
