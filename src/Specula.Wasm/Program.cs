using System.Runtime.InteropServices.JavaScript;

// .NET's JSExport source generator is a Roslyn/C# boundary; F# cannot emit it.
// This file contains no application rule, state, transition or projection.
// It forwards one serialized Limen message to the F# engine and returns the
// serialized result.
return;

public partial class SpeculaWasm
{
    [JSExport]
    internal static string Dispatch(string messageJson) =>
        Specula.Engine.Dispatch.handle(messageJson);
}
