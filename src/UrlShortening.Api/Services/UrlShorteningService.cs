using Dapper;
using Npgsql;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using UrlShortening.Api.Models;

namespace UrlShortening.Api.Services;

internal sealed class UrlShorteningService(NpgsqlDataSource dataSource,
    ILogger<UrlShorteningService> logger,
    IHttpContextAccessor httpContextAccessor)
{
    private const int MaxRetries = 3;
    private static readonly Meter Meter = new Meter("UrlShortening.api");
    private static readonly Counter<int> RedirectCounter = Meter.CreateCounter<int>(
        "url.shortener.redirects",
        "The number of successfull redirects");

    private static readonly Counter<int> FailedRedirectCounter = Meter.CreateCounter<int>(
        "url.shortener.failed_redirects",
        "The number of failed redirects");

    public async Task<string> shortenUrlAsync(string url)
    {
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {

            try
            {
                var shortCode = GenerateShortCode();

                const string sql =
                    """
            INSERT INTO shortened_urls (short_code , original_url)
            VALUES(@ShortCode,@Url)
            RETURNING short_code
            """;

                await using var connection = await dataSource.OpenConnectionAsync();

                var result = await connection.QuerySingleAsync<string>(sql,
                    new { ShortCode = shortCode, Url = url });

                return result;
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {

                if (attempt == MaxRetries)
                {
                    logger.LogError(ex, "Failed to generate");
                    throw new InvalidOperationException();
                }


            }
        }

        throw new InvalidOperationException();
        
    }

    private static string GenerateShortCode()
    {
        const int length = 7;
        const string aplhabet = "qwertyuiopasdfghjklzxcvbnmQWERTYUIOASDFGHJKLZXCVBNM0123456789";

        var chars = Enumerable.Range(0, length)
            .Select(_=> aplhabet[Random.Shared.Next(aplhabet.Length)])
            .ToArray();

        return new string(chars);
    }

    public async Task<string?> GetOriginalAsync(string shortCode)
    {
        const string sql =
            """
            SELECT original_url
            FROM shortened_urls
            WHERE short_code = @shortCode
            """;

        await using var connection = await dataSource.OpenConnectionAsync();

        var originalUrl = await connection.QuerySingleOrDefaultAsync<string>(sql, new { ShortCode = shortCode });

        if (originalUrl == null)
        {
            FailedRedirectCounter.Add(1);
        }
        else
        {
            await RecordVisist(shortCode);
            RedirectCounter.Add(1 , new TagList
            {
                { "short_code",shortCode }
            });
        }

        return originalUrl;
    }

    public async Task<IEnumerable<ShortenedUrl>> GetAllUrlsAsync()
    {
        const string sql =
            """
            SELECT short_code as ShortCode,original_url as OriginalUrl,create_at as CreatedAt
            FROM shortened_urls
            ORDER BY create_at DESC
            """;

        await using var connection = await dataSource.OpenConnectionAsync();

        return await connection.QueryAsync<ShortenedUrl>(sql);
    }

    private async Task RecordVisist(string shortCode)
    {
        var context = httpContextAccessor.HttpContext;
        var userAgent = context?.Request.Headers.UserAgent;
        var referer = context?.Request.Headers.Referer;

        const string sql =
            """
            INSERT INTO url_visits(short_code,user_agent,referer)
            VALUES(@ShortCode,@UserAgent,@Referer);
            """;

        await using var connection = await dataSource.OpenConnectionAsync();

        await connection.ExecuteAsync(
            sql,
            new
            {
                ShortCode = shortCode,
                UserAgent = userAgent,
                referer = referer,
            });

    }
}
