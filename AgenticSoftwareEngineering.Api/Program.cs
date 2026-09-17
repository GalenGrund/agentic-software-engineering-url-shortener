using AgenticSoftwareEngineering.Api.Application.Urls;
using AgenticSoftwareEngineering.Api.Application.Orchestration;
using AgenticSoftwareEngineering.Api.Infrastructure.Persistence;
using AgenticSoftwareEngineering.Api.Infrastructure.Validation;
using AgenticSoftwareEngineering.Api.Providers;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=agentic-sdlc.db"));
builder.Services.Configure<OrchestrationOptions>(builder.Configuration.GetSection("Orchestration"));
builder.Services.AddScoped<IUrlShortenerService, UrlShortenerService>();
builder.Services.AddScoped<IOrchestrationService, OrchestrationService>();
builder.Services.AddSingleton<IUrlValidator, UrlValidator>();
builder.Services.AddSingleton<IShortCodeGenerator, SecureShortCodeGenerator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAgentProvider, DeterministicAgentProvider>();
builder.Services.AddSingleton<IAgentProvider, DeterministicFallbackProvider>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseHttpsRedirection();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();

app.Run();

public partial class Program
{
}
