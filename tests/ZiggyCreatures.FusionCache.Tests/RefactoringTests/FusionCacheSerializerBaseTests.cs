using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ZiggyCreatures.Caching.Fusion.Serialization;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;
using FusionCacheTests.Stuff;

namespace FusionCacheTests.RefactoringTests;

public class FusionCacheSerializerBaseTests : AbstractTests
{
	public FusionCacheSerializerBaseTests(ITestOutputHelper output)
		: base(output, null)
	{
	}

	[Fact]
	public void SerializeAsync_DelegatesToSyncMethod()
	{
		// Arrange
		var serializer = new FusionCacheSystemTextJsonSerializer();
		var testData = "Hello World";

		// Act
		var result = serializer.SerializeAsync(testData).Result;

		// Assert
		Assert.NotEmpty(result);
	}

	[Fact]
	public async Task SerializeAsync_WithCancellationToken_DelegatesToSyncMethod()
	{
		// Arrange
		var serializer = new FusionCacheSystemTextJsonSerializer();
		var testData = "Hello World";
		var cancellationToken = CancellationToken.None;

		// Act
		var result = await serializer.SerializeAsync(testData, cancellationToken);

		// Assert
		Assert.NotEmpty(result);
	}

	[Fact]
	public void DeserializeAsync_DelegatesToSyncMethod()
	{
		// Arrange
		var serializer = new FusionCacheSystemTextJsonSerializer();
		var testData = "Hello World";
		var serializedData = serializer.Serialize(testData);

		// Act
		var result = serializer.DeserializeAsync<string>(serializedData).Result;

		// Assert
		Assert.Equal(testData, result);
	}

	[Fact]
	public async Task DeserializeAsync_WithCancellationToken_DelegatesToSyncMethod()
	{
		// Arrange
		var serializer = new FusionCacheSystemTextJsonSerializer();
		var testData = "Hello World";
		var serializedData = serializer.Serialize(testData);
		var cancellationToken = CancellationToken.None;

		// Act
		var result = await serializer.DeserializeAsync<string>(serializedData, cancellationToken);

		// Assert
		Assert.Equal(testData, result);
	}

	[Fact]
	public void ToString_ReturnsTypeName()
	{
		// Arrange
		var serializer = new FusionCacheSystemTextJsonSerializer();

		// Act
		var result = serializer.ToString();

		// Assert
		Assert.Equal("FusionCacheSystemTextJsonSerializer", result);
	}

	[Fact]
	public void RoundTrip_SerializeAndDeserialize_WorksCorrectly()
	{
		// Arrange
		var serializer = new FusionCacheSystemTextJsonSerializer();
		var testData = "Test Value for Round Trip";

		// Act
		var serialized = serializer.Serialize(testData);
		var deserialized = serializer.Deserialize<string>(serialized);

		// Assert
		Assert.Equal(testData, deserialized);
	}

	[Fact]
	public async Task RoundTripAsync_SerializeAndDeserialize_WorksCorrectly()
	{
		// Arrange
		var serializer = new FusionCacheSystemTextJsonSerializer();
		var testData = "Test Value for Async Round Trip";

		// Act
		var serialized = await serializer.SerializeAsync(testData);
		var deserialized = await serializer.DeserializeAsync<string>(serialized);

		// Assert
		Assert.Equal(testData, deserialized);
	}

	private class ComplexTestObject
	{
		public string? Name { get; set; }
		public int Value { get; set; }
		public DateTime Timestamp { get; set; }
	}

	[Fact]
	public void ComplexObjectSerialization_WorksCorrectly()
	{
		// Arrange
		var serializer = new FusionCacheSystemTextJsonSerializer();
		var testObject = new ComplexTestObject
		{
			Name = "Test Object",
			Value = 42,
			Timestamp = new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Utc)
		};

		// Act
		var serialized = serializer.Serialize(testObject);
		var deserialized = serializer.Deserialize<ComplexTestObject>(serialized);

		// Assert
		Assert.NotNull(deserialized);
		Assert.Equal(testObject.Name, deserialized.Name);
		Assert.Equal(testObject.Value, deserialized.Value);
		Assert.Equal(testObject.Timestamp, deserialized.Timestamp);
	}
}