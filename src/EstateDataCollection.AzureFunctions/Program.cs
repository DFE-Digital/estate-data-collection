using EstateDataCollection.Application;
using EstateDataCollection.AzureFunctions;
using EstateDataCollection.AzureFunctions.Middleware;
using EstateDataCollection.Domain;
using EstateDataCollection.Infrastructure;
using EstateDataCollection.Infrastructure.Monitoring;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.UseMiddleware<RequestSizeLimitMiddleware>();
builder.UseMiddleware<GlobalExceptionMiddleware>();
builder.UseMiddleware<VersionLoggingMiddleware>();

builder.Services.AddLogging(builder.Configuration, builder.Logging);
builder.Services.AddFunctions();
builder.Services.AddApplication();
builder.Services.AddDomain();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddMonitoring(builder.Configuration, builder.Logging);

var app = builder.Build();
app.Run();
