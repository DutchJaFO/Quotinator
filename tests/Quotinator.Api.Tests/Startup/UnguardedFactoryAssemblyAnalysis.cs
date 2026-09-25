using System.Reflection;
using System.Reflection.Emit;
using Microsoft.AspNetCore.Mvc.Testing;
using Quotinator.Api.Tests.Enums;

namespace Quotinator.Api.Tests.Startup;

/// <summary>
/// Layer A of the #313 guard: reads a compiled assembly's types and every method body's IL, so it judges
/// what the compiler emitted rather than how the source was written.
/// </summary>
internal static class UnguardedFactoryAssemblyAnalysis
{
    private const BindingFlags Declared =
        BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly OpCode[] OneByteOpCodes = new OpCode[0x100];
    private static readonly OpCode[] TwoByteOpCodes = new OpCode[0x100];

    private static readonly MethodInfo ActivatorGeneric =
        typeof(Activator).GetMethod(nameof(Activator.CreateInstance), 1, Type.EmptyTypes)!;

    static UnguardedFactoryAssemblyAnalysis()
    {
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            OpCode code = (OpCode)field.GetValue(null)!;
            ushort value = (ushort)code.Value;
            if (code.Size == 1)
            {
                OneByteOpCodes[value] = code;
            }
            else
            {
                TwoByteOpCodes[value & 0xFF] = code;
            }
        }
    }

    /// <summary>Every unguarded factory usage in <paramref name="assembly"/>.</summary>
    /// <param name="assembly">The compiled code to analyse.</param>
    /// <param name="guardedFactory">The one factory type that may be constructed.</param>
    internal static UnguardedFactoryUsageResult Analyse(Assembly assembly, Type guardedFactory)
    {
        List<(UnguardedFactoryUsageKind Kind, string Location)> findings = [];
        List<string> readFailures = [];
        int guardedConstructions = 0;
        int inspected = 0;

        // Found from the guarded factory's own ancestry rather than written out, so this file never names the
        // closed bare type, which is exactly what its own rules would flag.
        Type? bare = null;
        for (Type? current = guardedFactory.BaseType; current is not null && bare is null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(WebApplicationFactory<>))
            {
                bare = current;
            }
        }

        if (bare is null)
        {
            return new UnguardedFactoryUsageResult { ReadFailures = [$"{guardedFactory.FullName} does not derive from WebApplicationFactory<>."] };
        }

        string reflectionName = typeof(WebApplicationFactory<>).Name;

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return new UnguardedFactoryUsageResult { ReadFailures = [.. exception.LoaderExceptions.Select(e => e?.Message ?? "unknown loader failure")] };
        }

        foreach (Type type in types)
        {
            if (type != guardedFactory && DerivesFrom(type, bare))
            {
                findings.Add((UnguardedFactoryUsageKind.UnguardedSubclass, type.FullName ?? type.Name));
            }

            IEnumerable<MethodBase> methods = [.. type.GetConstructors(Declared), .. type.GetMethods(Declared)];
            foreach (MethodBase method in methods)
            {
                byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
                if (il is not null)
                {
                    ReadMethod(method, il, bare, guardedFactory, reflectionName, findings, readFailures, ref guardedConstructions, ref inspected);
                }
            }
        }

        return new UnguardedFactoryUsageResult
        {
            Findings                 = findings,
            InspectedCount           = inspected,
            GuardedConstructionCount = guardedConstructions,
            ReadFailures             = readFailures,
        };
    }

    private static void ReadMethod(
        MethodBase method,
        byte[] il,
        Type bare,
        Type guarded,
        string reflectionName,
        List<(UnguardedFactoryUsageKind Kind, string Location)> findings,
        List<string> readFailures,
        ref int guardedConstructions,
        ref int inspected)
    {
        Module module          = method.Module;
        Type[]? typeArguments   = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
        Type[]? methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;

        int position = 0;
        while (position < il.Length)
        {
            int offset = position;
            inspected++;
            OpCode code = il[position] == 0xFE ? TwoByteOpCodes[il[position + 1]] : OneByteOpCodes[il[position]];
            position += code.Size;
            string location = $"{method.DeclaringType?.FullName}.{method.Name} IL_{offset:X4}";

            int operandSize = OperandSize(code.OperandType, il, position);
            if (operandSize < 0)
            {
                readFailures.Add($"{location}: unrecognised operand type {code.OperandType}");
                return;
            }

            try
            {
                switch (code.OperandType)
                {
                    case OperandType.InlineMethod:
                        MethodBase? target = module.ResolveMethod(BitConverter.ToInt32(il, position), typeArguments, methodArguments);
                        if (code == OpCodes.Newobj && target?.DeclaringType is Type created)
                        {
                            if (created == guarded)
                            {
                                guardedConstructions++;
                            }
                            else if (IsOrDerivesFrom(created, bare))
                            {
                                findings.Add((UnguardedFactoryUsageKind.DirectConstruction, location));
                            }
                        }

                        if (target is MethodInfo { IsGenericMethod: true } generic && ConstructsThroughTypeArgument(generic, bare, guarded))
                        {
                            findings.Add((UnguardedFactoryUsageKind.GenericConstruction, location));
                        }

                        break;

                    case OperandType.InlineTok:
                        if (module.ResolveMember(BitConverter.ToInt32(il, position), typeArguments, methodArguments) is Type token && token == bare)
                        {
                            findings.Add((UnguardedFactoryUsageKind.TypeObtained, location));
                        }

                        break;

                    case OperandType.InlineString:
                        if (module.ResolveString(BitConverter.ToInt32(il, position)).Contains(reflectionName, StringComparison.Ordinal))
                        {
                            findings.Add((UnguardedFactoryUsageKind.ReflectionName, location));
                        }

                        break;
                }
            }
            catch (Exception exception) when (exception is ArgumentException or BadImageFormatException or TypeLoadException or FileNotFoundException)
            {
                readFailures.Add($"{location}: {code.Name} operand could not be resolved: {exception.Message}");
            }

            position += operandSize;
        }
    }

    private static int OperandSize(OperandType operand, byte[] il, int position) => operand switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or OperandType.InlineMethod
            or OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType
            or OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, position)),
        _ => -1,
    };

    private static bool ConstructsThroughTypeArgument(MethodInfo method, Type bare, Type guarded)
    {
        MethodInfo definition = method.GetGenericMethodDefinition();
        Type[] arguments  = method.GetGenericArguments();
        Type[] parameters = definition.GetGenericArguments();
        bool isActivator  = definition == ActivatorGeneric;

        for (int i = 0; i < arguments.Length; i++)
        {
            bool constructs = isActivator
                || parameters[i].GenericParameterAttributes.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint);
            if (constructs && arguments[i] != guarded && IsOrDerivesFrom(arguments[i], bare))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsOrDerivesFrom(Type type, Type bare) => type == bare || DerivesFrom(type, bare);

    private static bool DerivesFrom(Type type, Type bare)
    {
        for (Type? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current == bare)
            {
                return true;
            }
        }

        return false;
    }
}
