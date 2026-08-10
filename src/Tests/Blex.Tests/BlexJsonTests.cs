using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blex;
using Xunit;

namespace Blex.Tests;

/// <summary>
/// A <see cref="JsonSerializerOptions"/> instance becomes read-only on first use, so customizing
/// the shared options has to swap in a fresh copy rather than mutate the live one.
/// </summary>
[Collection(nameof(BlexJsonTests))]
[CollectionDefinition(nameof(BlexJsonTests), DisableParallelization = true)]
public class BlexJsonTests : IDisposable
{
    public void Dispose() => BlexJson.Reset();

    private sealed class UpperCaseStringConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
            => reader.GetString() ?? "";

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions o)
            => writer.WriteStringValue(value.ToUpperInvariant());
    }

    [Fact]
    public void Configure_AppliesToSubsequentStoreSnapshots()
    {
        // Force the default options into their read-only state first: mutating them in place
        // would throw, which is exactly what Configure exists to avoid.
        _ = new CounterStore().SerializeState();

        BlexJson.Configure(o => o.Converters.Add(new UpperCaseStringConverter()));

        var store = new CounterStore();
        store.Label = "idle";
        Assert.Equal("IDLE", store.SerializeState()["Label"]!.GetValue<string>());
    }

    [Fact]
    public void Configure_LeavesTheDefaultsIntact()
    {
        BlexJson.Configure(o => o.WriteIndented = true);

        Assert.True(BlexJson.Options.WriteIndented);
        Assert.True(BlexJson.Options.PropertyNameCaseInsensitive);
        Assert.Contains(BlexJson.Options.Converters, c => c is JsonStringEnumConverter);
    }

    [Fact]
    public void Configure_DoesNotMutateThePreviousInstance()
    {
        var before = BlexJson.Options;
        BlexJson.Configure(o => o.WriteIndented = true);

        Assert.NotSame(before, BlexJson.Options);
        Assert.False(before.WriteIndented);
    }

    [Fact]
    public void Configure_RejectsNull()
        => Assert.Throws<ArgumentNullException>(() => BlexJson.Configure(null!));

    [Fact]
    public void Reset_RestoresTheDefaults()
    {
        BlexJson.Configure(o => o.WriteIndented = true);
        BlexJson.Reset();

        Assert.False(BlexJson.Options.WriteIndented);
    }
}
