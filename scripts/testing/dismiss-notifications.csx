#!/usr/bin/env dotnet-script
#nullable enable
// Dismisses every active notification on a running test container, then reads the list again and fails
// if any is still active. A document cannot do this itself: how many notifications exist is not something
// it may assert, and a loop in a document step is what ADR 010 rules out.
//
// Usage (run from repo root):
//   dotnet-script scripts/testing/dismiss-notifications.csx -- --url <base url ending in /api/v1>
//
// Options:
//   --url <url>      The API's base URL (required)
//   --api-key <key>  Value for the X-Api-Key header (default: smoketest, what the test environment sets)

using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

string? Value(string flag) => Args.SkipWhile(a => a != flag).Skip(1).FirstOrDefault();

string? baseUrl = Value("--url");

if (string.IsNullOrEmpty(baseUrl))
{
    Console.Error.WriteLine("Usage: dotnet-script scripts/testing/dismiss-notifications.csx -- --url <base url>");
    Environment.Exit(1);
    return;
}

HttpClient client = new();
client.DefaultRequestHeaders.Add("X-Api-Key", Value("--api-key") ?? "smoketest");

async Task<List<NotificationRow>> ReadActive()
{
    string json = await client.GetStringAsync($"{baseUrl}/notifications?pageSize=0");

    return [.. (JsonSerializer.Deserialize<NotificationList>(json)?.Items ?? []).Where(row => !row.IsDismissed)];
}

int dismissed = 0;

foreach (NotificationRow row in await ReadActive())
{
    HttpResponseMessage response = await client.PostAsync($"{baseUrl}/notifications/{row.Id}/dismiss", null);

    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"Dismissing {row.Id} answered {(int)response.StatusCode}.");
        Environment.Exit(1);
        return;
    }

    dismissed++;
}

int stillActive = (await ReadActive()).Count;

Console.WriteLine($"dismissed = {dismissed}");
Console.WriteLine($"still active = {stillActive}");

if (stillActive > 0)
    Environment.Exit(1);

sealed class NotificationRow
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("isDismissed")]
    public bool IsDismissed { get; init; }
}

sealed class NotificationList
{
    [JsonPropertyName("items")]
    public List<NotificationRow>? Items { get; init; }
}
