using System.Text.Json.Serialization;

namespace Fh6Tuning.Core;

// Every categorical input is a C# enum whose member names equal the JS string tokens used as
// dictionary keys throughout legacy/tuning.js. A single JsonStringEnumConverter<T> round-trips
// them and the engine can switch on them with no string layer. Declaration order of PiClass IS
// the PI_INDEX ladder, so (int)c equals the legacy index.

[JsonConverter(typeof(JsonStringEnumConverter<Drivetrain>))]
public enum Drivetrain { FWD, RWD, AWD } // legacy: "FWD" | "RWD" | "AWD"

[JsonConverter(typeof(JsonStringEnumConverter<EngineLocation>))]
public enum EngineLocation { Front, Mid, Rear } // legacy: "Front" | "Mid" | "Rear"

[JsonConverter(typeof(JsonStringEnumConverter<Powertrain>))]
public enum Powertrain { ICE, EV, Hybrid } // legacy: "ICE" | "EV" | "Hybrid"

[JsonConverter(typeof(JsonStringEnumConverter<PiClass>))]
public enum PiClass { D, C, B, A, S1, S2, R, X } // ladder index 0..7 == PI_INDEX

// legacy tire-compound keys (note lowercase 'r' in "Offroad" — DISTINCT from Goal.OffRoad)
[JsonConverter(typeof(JsonStringEnumConverter<TireCompound>))]
public enum TireCompound { Stock, Street, Sport, Race, Rally, Drag, Offroad }

[JsonConverter(typeof(JsonStringEnumConverter<SuspensionType>))]
public enum SuspensionType { Stock, Street, Sport, Race, Drift, Offroad }

// UI aero-kit selector; exploded into hasFrontAero/hasRearAero/aeroInstalled at read time.
[JsonConverter(typeof(JsonStringEnumConverter<AeroKit>))]
public enum AeroKit { None, Front, Rear, Full } // legacy <select> values

// GOALS array order is canonical (legacy/tuning.js:28). NOTE capital-R "OffRoad".
[JsonConverter(typeof(JsonStringEnumConverter<Goal>))]
public enum Goal { Circuit, Drag, Drift, OffRoad, Rally, Touge }

// app.js concept only; the engine is imperial-only.
[JsonConverter(typeof(JsonStringEnumConverter<UnitSystem>))]
public enum UnitSystem { Imperial, Metric }

// derived.classTier: keys FREQ_BASE / MIN_BUMP; surfaces in the "Class tier" summary chip.
[JsonConverter(typeof(JsonStringEnumConverter<ClassTier>))]
public enum ClassTier { Sports, HighPerf, Race }

/// <summary>PI ladder helper. 0..7 ladder index == legacy <c>PI_INDEX</c>.</summary>
public static class PiClassInfo
{
    /// <summary>0..7 ladder index == legacy PI_INDEX. D=0, C=1, B=2, A=3, S1=4, S2=5, R=6, X=7.</summary>
    public static int Index(this PiClass c) => (int)c;

    public const int A = (int)PiClass.A;   // 3
    public const int S1 = (int)PiClass.S1; // 4
}
