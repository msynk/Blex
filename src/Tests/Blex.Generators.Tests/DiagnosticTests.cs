using Xunit;

namespace Blex.Generators.Tests;

public class DiagnosticTests
{
    private const string Usings = "using System.Threading; using System.Threading.Tasks; using Blex;\n";

    [Fact]
    public void NotPartial_ReportsBLEX001()
    {
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public class S { [StateAttributeBlex] private int _x; }
            """);
        Assert.True(result.HasDiagnostic("BLEX001"));
    }

    [Fact]
    public void ActionWithoutOnPrefix_ReportsBLEX002()
    {
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public partial class S { [ActionAttributeBlex] private void Increment() { } }
            """);
        Assert.True(result.HasDiagnostic("BLEX002"));
    }

    [Fact]
    public void ComputedWithBadName_ReportsBLEX003()
    {
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public partial class S { [ComputedAttributeBlex] private int Twice() => 2; }
            """);
        Assert.True(result.HasDiagnostic("BLEX003"));
    }

    [Fact]
    public void ComputedWithParameters_ReportsBLEX004()
    {
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public partial class S { [ComputedAttributeBlex] private int ComputeX(int y) => y; }
            """);
        Assert.True(result.HasDiagnostic("BLEX004"));
    }

    [Fact]
    public void CollidingGeneratedNames_ReportsBLEX005()
    {
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public partial class S
            {
                [StateAttributeBlex] private int _value;
                [ComputedAttributeBlex] private int ComputeValue() => 1;
            }
            """);
        Assert.True(result.HasDiagnostic("BLEX005"));
    }

    [Fact]
    public void CollisionWithUserMember_ReportsBLEX005()
    {
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public partial class S
            {
                [StateAttributeBlex] private int _count;
                public int Count => 42; // user already declared what the generator would emit
            }
            """);
        Assert.True(result.HasDiagnostic("BLEX005"));
    }

    [Fact]
    public void CollisionWithReservedName_ReportsBLEX005()
    {
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public partial class S { [StateAttributeBlex] private string _name; }
            """);
        Assert.True(result.HasDiagnostic("BLEX005")); // Name is a StoreBaseBlex member
    }

    [Fact]
    public void NestedStore_ReportsBLEX006()
    {
        var result = GeneratorTestHelper.Run(Usings + """
            public class Outer { [StoreAttributeBlex] public partial class S { [StateAttributeBlex] private int _x; } }
            """);
        Assert.True(result.HasDiagnostic("BLEX006"));
    }

    [Fact]
    public void SyncEffect_ReportsBLEX007()
    {
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public partial class S { [EffectAttributeBlex] private void OnLoad() { } }
            """);
        Assert.True(result.HasDiagnostic("BLEX007"));
    }

    [Fact]
    public void StaticStateField_ReportsBLEX008()
    {
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public partial class S { [StateAttributeBlex] private static int _x; }
            """);
        Assert.True(result.HasDiagnostic("BLEX008"));
    }

    [Fact]
    public void ReadonlyStateField_ReportsBLEX008()
    {
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public partial class S { [StateAttributeBlex] private readonly int _x; }
            """);
        Assert.True(result.HasDiagnostic("BLEX008"));
    }

    [Fact]
    public void LatestEffectWithoutToken_ReportsBLEX009Warning()
    {
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public partial class S
            {
                [EffectAttributeBlex(Concurrency = EffectConcurrencyBlex.Latest)]
                private async Task OnSearch(string q) => await Task.Yield();
            }
            """);
        Assert.True(result.HasDiagnostic("BLEX009"));
        Assert.False(result.HasGeneratorError); // warning only; wrapper still generated
    }

    [Fact]
    public void AsyncVoidAction_ReportsBLEX010()
    {
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public partial class S { [ActionAttributeBlex] private async void OnFire() => await Task.Yield(); }
            """);
        Assert.True(result.HasDiagnostic("BLEX010"));
    }

    [Fact]
    public void ValueReturningAction_ReportsBLEX011Warning()
    {
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public partial class S { [ActionAttributeBlex] private int OnCalc() => 42; }
            """);
        Assert.True(result.HasDiagnostic("BLEX011"));
        Assert.False(result.HasGeneratorError);
    }

    [Fact]
    public void StateFieldWithoutPrefix_ReportsBLEX012()
    {
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public partial class S { [StateAttributeBlex] private int Count; }
            """);
        Assert.True(result.HasDiagnostic("BLEX012"));
    }

    [Fact]
    public void RefParameter_ReportsBLEX013()
    {
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public partial class S { [ActionAttributeBlex] private void OnPush(ref int x) { } }
            """);
        Assert.True(result.HasDiagnostic("BLEX013"));
    }

    [Fact]
    public void GenericActionMethod_ReportsBLEX014()
    {
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public partial class S { [ActionAttributeBlex] private void OnSet<T>(T value) { } }
            """);
        Assert.True(result.HasDiagnostic("BLEX014"));
    }

    [Fact]
    public void RecordStore_ReportsBLEX015()
    {
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public partial record S { [StateAttributeBlex] private int _x; }
            """);
        Assert.True(result.HasDiagnostic("BLEX015"));
    }

    [Fact]
    public void StoreWithBaseClass_ReportsBLEX016()
    {
        var result = GeneratorTestHelper.Run(Usings + """
            public class MyBase { }
            [StoreAttributeBlex] public partial class S : MyBase { [StateAttributeBlex] private int _x; }
            """);
        Assert.True(result.HasDiagnostic("BLEX016"));
    }

    [Fact]
    public void GenericStore_ReportsBLEX006()
    {
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public partial class S<T> { [StateAttributeBlex] private int _x; }
            """);
        Assert.True(result.HasDiagnostic("BLEX006"));
    }

    [Fact]
    public void StaticStore_ReportsBLEX006()
    {
        // A static class cannot derive from StoreBaseBlex or hold the instance members the generator
        // emits; without this the user gets four raw CS errors inside generated code.
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public static partial class S { }
            """);
        Assert.True(result.HasDiagnostic("BLEX006"));
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void GeneratedMemberCollidingWithBeginAction_ReportsBLEX005()
    {
        // BeginAction/DispatchApprovedAsync are StoreBaseBlex members the effect wrappers call.
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public partial class S
            {
                [StateAttributeBlex] private int _x;
                [ActionAttributeBlex] private void OnBeginAction() { }
            }
            """);
        Assert.True(result.HasDiagnostic("BLEX005"));
    }

    [Fact]
    public void VoidComputed_ReportsBLEX017()
    {
        // `public void X { get { ... } }` does not compile; the memoized property needs a value.
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public partial class S
            {
                [StateAttributeBlex] private int _x;
                [ComputedAttributeBlex] private void ComputeThing() { }
            }
            """);
        Assert.True(result.HasDiagnostic("BLEX017"));
    }

    [Fact]
    public void VoidComputed_StillEmitsTheRestOfTheStoreWithoutCompileErrors()
    {
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public partial class S
            {
                [StateAttributeBlex] private int _x;
                [ComputedAttributeBlex] private void ComputeThing() { }
                [ComputedAttributeBlex] private int ComputeDouble() => X * 2;
                [ActionAttributeBlex] private void OnBump() => X++;
            }
            """);

        // The offending member is skipped rather than poisoning the whole emission, so the only
        // failure the user sees is the precise BLEX017 diagnostic.
        Assert.True(result.HasDiagnostic("BLEX017"));
        Assert.Empty(result.CompileErrors());
        Assert.Contains("public int Double", result.SingleGenerated);
    }

    [Fact]
    public void StateFieldOutsideAStore_ReportsBLEX018()
    {
        // Without the [StoreAttributeBlex] the whole file compiles cleanly and generates nothing,
        // which is indistinguishable from a broken generator. Say so.
        var result = GeneratorTestHelper.Run(Usings + """
            public partial class NotAStore { [StateAttributeBlex] private int _x; }
            """);
        Assert.True(result.HasDiagnostic("BLEX018"));
    }

    [Fact]
    public void ActionComputedAndEffectOutsideAStore_ReportBLEX018()
    {
        var result = GeneratorTestHelper.Run(Usings + """
            public partial class NotAStore
            {
                [ComputedAttributeBlex] private int ComputeX() => 1;
                [ActionAttributeBlex] private void OnBump() { }
                [EffectAttributeBlex] private Task OnLoad() => Task.CompletedTask;
            }
            """);
        Assert.True(result.HasDiagnostic("BLEX018"));
        Assert.Equal(3, result.GeneratorDiagnostics.Count(d => d.Id == "BLEX018"));
    }

    [Fact]
    public void MembersInsideAStore_DoNotReportBLEX018()
    {
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public partial class S
            {
                [StateAttributeBlex] private int _x;
                [ComputedAttributeBlex] private int ComputeDouble() => X * 2;
                [ActionAttributeBlex] private void OnBump() => X++;
                [EffectAttributeBlex] private Task OnLoad() => Task.CompletedTask;
            }
            """);
        Assert.False(result.HasDiagnostic("BLEX018"));
        Assert.Empty(result.CompileErrors());
    }

    [Fact]
    public void NegativeDebounce_ReportsBLEX019()
    {
        // Task.Delay throws on a negative delay, at runtime, from inside generated code.
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public partial class S
            {
                [StateAttributeBlex] private int _x;
                [EffectAttributeBlex(DebounceMilliseconds = -1)] private Task OnLoad() => Task.CompletedTask;
            }
            """);
        Assert.True(result.HasDiagnostic("BLEX019"));
    }

    [Fact]
    public void ZeroDebounce_IsTheDefaultAndReportsNothing()
    {
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public partial class S
            {
                [StateAttributeBlex] private int _x;
                [EffectAttributeBlex(DebounceMilliseconds = 0)] private Task OnLoad() => Task.CompletedTask;
                [EffectAttributeBlex] private Task OnOther() => Task.CompletedTask;
            }
            """);
        Assert.False(result.HasDiagnostic("BLEX019"));
        Assert.Empty(result.CompileErrors());
        Assert.DoesNotContain("Task.Delay", result.SingleGenerated);
    }

    [Fact]
    public void PositiveDebounce_EmitsTheGateBeforeAnythingObservable()
    {
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public partial class S
            {
                [StateAttributeBlex] private int _x;
                [EffectAttributeBlex(DebounceMilliseconds = 250)] private Task OnSearch(string q) => Task.CompletedTask;
            }
            """);

        Assert.Empty(result.CompileErrors());
        var generated = result.SingleGenerated;
        Assert.Contains("Task.Delay(250", generated);

        // The gate must precede BeginAction: a superseded invocation is never offered to
        // middleware and records nothing.
        Assert.True(generated.IndexOf("Task.Delay(250") < generated.IndexOf("BeginAction(\"Search\""));
    }

    [Fact]
    public void MemberInATypeNestedInsideAStore_ReportsBLEX018()
    {
        // The nested type is not itself a store, so nothing is generated for its members either.
        var result = GeneratorTestHelper.Run(Usings + """
            [StoreAttributeBlex] public partial class S
            {
                [StateAttributeBlex] private int _x;
                public class Inner { [StateAttributeBlex] private int _y; }
            }
            """);
        Assert.True(result.HasDiagnostic("BLEX018"));
    }
}
