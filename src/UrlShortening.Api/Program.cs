using UrlShortening.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.

builder.Services.AddHostedService<DataBaseInitilizer>();
builder.Services.AddScoped<UrlShorteningService>();

builder.AddNpgsqlDataSource("url-shortener");
builder.AddRedisDistributedCache("redis");
builder.Services.AddHttpContextAccessor();
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter("UrlShortening.api"));

#pragma warning disable EXTEXP0018
builder.Services.AddHybridCache();
#pragma warning disable EXTEXP0018

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    
    app.UseSwaggerUI(options =>
    {
        
        options.SwaggerEndpoint("/openapi/v1.json", "OpenAPI v1");
    });
}

app.UseHttpsRedirection();

app.MapPost("shorten", async (string url, UrlShorteningService urlShorteningService) =>
{
    if(!Uri.TryCreate(url,UriKind.Absolute,out _))
    {
        return Results.BadRequest("Invalid Url format");
    }

    var shortCode = await urlShorteningService.shortenUrlAsync(url);

    return Results.Ok(new {shortCode});
});

app.MapGet("{shortCode}", async (string shortCode, UrlShorteningService urlShorteningService) =>
{
    var originalUrl = await urlShorteningService.GetOriginalAsync(shortCode);

    return originalUrl is null ? Results.NotFound() : Results.Redirect(originalUrl);

});

app.MapGet("urls", async (UrlShorteningService urlShorteningService) =>
{
    var originalUrl = await urlShorteningService.GetAllUrlsAsync();

    return Results.Ok(new {originalUrl});

});


app.Run();
