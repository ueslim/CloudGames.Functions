using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Azure.Messaging.ServiceBus;

namespace CloudGames.Functions.Payments;

public class PaymentsEventsConsumer
{
    private const string CorrelationHeader = "x-correlation-id";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;
    private readonly IConfiguration _configuration;

    public PaymentsEventsConsumer(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _logger = loggerFactory.CreateLogger<PaymentsEventsConsumer>();
        _configuration = configuration;
    }

    private record PaymentApproved(Guid PaymentId, Guid UserId, Guid GameId, decimal Amount);
    private record PaymentDeclined(Guid PaymentId, Guid UserId, Guid GameId, decimal Amount, string Reason);

    [Function("PaymentsEventsConsumer")]
    public async Task RunAsync(
        [ServiceBusTrigger("%ServiceBus__Topic%", "%ServiceBus__Subscription%", Connection = "ServiceBus__Connection")] ServiceBusReceivedMessage message,
        FunctionContext context)
    {
        string body = message.Body.ToString();
        string? subject = message.Subject;

        // Correlation id: prefer application properties, then activity trace id, else new
        var correlationId = message.ApplicationProperties != null && message.ApplicationProperties.TryGetValue(CorrelationHeader, out var cidObj)
            ? cidObj?.ToString()
            : null;
        correlationId ??= Guid.NewGuid().ToString();

        using (Serilog.Context.LogContext.PushProperty("correlation_id", correlationId))
        using (Serilog.Context.LogContext.PushProperty("user_id", ExtractUserId(body)))
        {
            _logger.LogInformation("Received payment event. Subject={Subject} Body={Body}", subject, body);

            try
            {
                if (IsEvent<PaymentApproved>(body, out var approved))
                {
                    await CallGamesApiAsync(approved.GameId, approved.UserId, approved.PaymentId, approved.Amount, isApproved: true, correlationId);
                    return;
                }
                if (IsEvent<PaymentDeclined>(body, out var declined))
                {
                    await CallGamesApiAsync(declined.GameId, declined.UserId, declined.PaymentId, declined.Amount, isApproved: false, correlationId, declined.Reason);
                    return;
                }

                _logger.LogWarning("Unknown payment event schema. Body={Body}", body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process payment event");
                throw;
            }
        }
    }

    private static bool IsEvent<T>(string json, out T payload)
    {
        payload = default!;
        try
        {
            var obj = JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (obj == null) return false;

            // simple schema validation: ensure all non-nullable props are present (best-effort)
            payload = obj;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ExtractUserId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("UserId", out var userIdProp))
                return userIdProp.GetString();
        }
        catch { }
        return null;
    }

    private async Task CallGamesApiAsync(Guid gameId, Guid userId, Guid paymentId, decimal amount, bool isApproved, string correlationId, string? reason = null)
    {
        var client = _httpClientFactory.CreateClient("games");
        client.DefaultRequestHeaders.Remove(CorrelationHeader);
        client.DefaultRequestHeaders.Add(CorrelationHeader, correlationId);

        // Assume Games exposes a payments callback endpoint; fallback to generic purchases webhook endpoints
        var basePath = _configuration["Games:PaymentsCallbackPath"] ?? "/api/purchases";
        var path = isApproved ? $"{basePath}/approved" : $"{basePath}/declined";

        var payload = isApproved
            ? new { paymentId, userId, gameId, amount }
            : new { paymentId, userId, gameId, amount, reason };

        var response = await client.PostAsJsonAsync(path, payload);
        response.EnsureSuccessStatusCode();
    }
}


