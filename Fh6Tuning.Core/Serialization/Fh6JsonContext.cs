using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fh6Tuning.Core.Serialization;

/// <summary>
/// Source-generated JSON context for the parity round-trip. CamelCase property names match the JS
/// keys; nulls are emitted (JS keeps null fields); enums round-trip via their per-type
/// <see cref="JsonStringEnumConverter{T}"/> attributes. Negative-zero is normalized in the engine
/// (<see cref="JsMath.NormZero"/>) so records never hold <c>-0.0</c> — no custom double converter
/// is needed here.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never, // emit nulls (JS keeps null fields)
    NumberHandling = JsonNumberHandling.Strict)]
[JsonSerializable(typeof(Tune))]
[JsonSerializable(typeof(TuneInput))]
[JsonSerializable(typeof(ValidationResult))]
public partial class Fh6JsonContext : JsonSerializerContext { }
