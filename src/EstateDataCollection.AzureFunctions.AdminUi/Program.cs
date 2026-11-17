using EstateDataCollection.AzureFunctions.AdminUi.Extensions;
using EstateDataCollection.Common.Web;
using EstateDataCollection.Domain;
using EstateDataCollection.Infrastructure.Monitoring;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();
builder.UseFunctionsMiddleware();

builder.Services.AddLogging(builder.Configuration, builder.Logging);
builder.Services.AddFunctions(typeof(Program).Assembly);
builder.Services.AddDomain();
builder.Services.AddAdminUiUseCases(builder.Configuration);
builder.Services.AddMonitoring(builder.Configuration, builder.Logging);

var app = builder.Build();
app.Run();
