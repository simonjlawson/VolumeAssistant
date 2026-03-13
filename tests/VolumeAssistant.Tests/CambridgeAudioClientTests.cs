using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VolumeAssistant.Service.CambridgeAudio;
using Xunit;

namespace VolumeAssistant.Tests
{
    public class CambridgeAudioClientTests
    {
        private static CambridgeAudioClient CreateClient()
        {
            var options = Options.Create(new CambridgeAudioOptions());
            return new CambridgeAudioClient(options, NullLogger<CambridgeAudioClient>.Instance);
        }

        [Fact]
        public void BuildParamsNode_ConvertsVariousTypes()
        {
            var parameters = new Dictionary<string, object?>
            {
                ["bool"] = true,
                ["int"] = 42,
                ["long"] = 1234567890123L,
                ["double"] = 3.14,
                ["float"] = 1.5f,
                ["string"] = "hello",
                ["null"] = null,
                ["object"] = new DateTime(2000,1,1)
            };

            var mi = typeof(CambridgeAudioClient).GetMethod(
                "BuildParamsNode",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            var node = (JsonObject)mi.Invoke(null, new object[] { parameters })!;

            Assert.Equal("True", node["bool"].ToString(), ignoreCase: true);
            Assert.Equal("42", node["int"]!.ToString());
            Assert.Contains("1234567890123", node["long"]!.ToString());
            Assert.Contains("3.14", node["double"]!.ToString());
            Assert.Contains("1.5", node["float"]!.ToString());
            Assert.Equal("hello", node["string"]!.ToString().Trim('"'));
            Assert.Null(node["null"]);
            Assert.Contains("2000", node["object"]!.ToString());
        }

        [Fact]
        public async Task RouteMessageAsync_RoutesResponseToPendingTcs()
        {
            var client = CreateClient();

            var pendingField = typeof(CambridgeAudioClient).GetField(
                "_pendingRequests",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            var dict = new ConcurrentDictionary<string, ConcurrentQueue<TaskCompletionSource<JsonElement>>>();
            var queue = new ConcurrentQueue<TaskCompletionSource<JsonElement>>();
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Enqueue(tcs);
            dict.TryAdd("/test", queue);
            pendingField.SetValue(client, dict);

            string json = "{\"path\":\"/test\",\"type\":\"response\",\"result\":200,\"params\":{\"data\":{\"value\":123}}}";
            using var doc = JsonDocument.Parse(json);
            var mi = typeof(CambridgeAudioClient).GetMethod(
                "RouteMessageAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            var task = (Task)mi.Invoke(client, new object[] { doc.RootElement.Clone() })!;
            await task;

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(1000));
            Assert.Equal(tcs.Task, completed);
            var result = await tcs.Task;
            Assert.True(result.TryGetProperty("path", out _));
        }

        [Fact]
        public async Task RouteMessageAsync_SetsExceptionOnErrorResult()
        {
            var client = CreateClient();

            var pendingField = typeof(CambridgeAudioClient).GetField(
                "_pendingRequests",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            var dict = new ConcurrentDictionary<string, ConcurrentQueue<TaskCompletionSource<JsonElement>>>();
            var queue = new ConcurrentQueue<TaskCompletionSource<JsonElement>>();
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Enqueue(tcs);
            dict.TryAdd("/err", queue);
            pendingField.SetValue(client, dict);

            string json = "{\"path\":\"/err\",\"type\":\"response\",\"result\":400,\"message\":\"Bad\"}";
            using var doc = JsonDocument.Parse(json);
            var mi = typeof(CambridgeAudioClient).GetMethod(
                "RouteMessageAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            var task = (Task)mi.Invoke(client, new object[] { doc.RootElement.Clone() })!;
            await task;

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(1000));
            Assert.Equal(tcs.Task, completed);
            await Assert.ThrowsAsync<CambridgeAudioException>(() => tcs.Task);
        }

        [Fact]
        public async Task RouteMessageAsync_RoutesUpdateToSubscriptionHandler()
        {
            var client = CreateClient();

            var subsField = typeof(CambridgeAudioClient).GetField(
                "_subscriptions",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            var subs = new Dictionary<string, Func<JsonElement, Task>>();
            var called = false;
            subs["/zone/state"] = (el) => { called = true; return Task.CompletedTask; };
            subsField.SetValue(client, subs);

            string json = "{\"path\":\"/zone/state\",\"type\":\"update\",\"params\":{\"data\":{\"vol\":1}}}";
            using var doc = JsonDocument.Parse(json);
            var mi = typeof(CambridgeAudioClient).GetMethod(
                "RouteMessageAsync",
                BindingFlags.NonPublic | BindingFlags.Instance)!;

            var task = (Task)mi.Invoke(client, new object[] { doc.RootElement.Clone() })!;
            await task;

            Assert.True(called);
        }
    }
}
