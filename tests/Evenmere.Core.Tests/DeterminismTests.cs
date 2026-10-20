using System.Reflection;
using Evenmere.Core.Numerics;

namespace Evenmere.Core.Tests;

public sealed class DeterminismTests
{
    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void CoreAssembly_Signatures_UseNoFloatingPointTypes()
    {
        List<string> offenders = [];

        foreach (Type type in typeof(Amount).Assembly.GetTypes())
        {
            foreach (FieldInfo field in type.GetFields(AllDeclared))
            {
                Check(field.FieldType, $"{type.Name}.{field.Name} (field)", offenders);
            }

            foreach (PropertyInfo property in type.GetProperties(AllDeclared))
            {
                Check(property.PropertyType, $"{type.Name}.{property.Name} (property)", offenders);
            }

            foreach (MethodBase method in type.GetMethods(AllDeclared).Cast<MethodBase>().Concat(type.GetConstructors(AllDeclared)))
            {
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    Check(parameter.ParameterType, $"{type.Name}.{method.Name}({parameter.Name})", offenders);
                }

                if (method is MethodInfo info)
                {
                    Check(info.ReturnType, $"{type.Name}.{method.Name} (return)", offenders);
                }
            }
        }

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void CoreAssembly_Types_DeclareNoMutableStaticState()
    {
        List<string> offenders = [];

        foreach (Type type in typeof(Amount).Assembly.GetTypes())
        {
            foreach (FieldInfo field in type.GetFields(AllDeclared))
            {
                if (field.IsStatic && !field.IsLiteral && !field.IsInitOnly)
                {
                    offenders.Add($"{type.Name}.{field.Name}");
                }
            }
        }

        offenders.ShouldBeEmpty();
    }

    private static void Check(Type type, string where, List<string> offenders)
    {
        Type element = type;
        while (element.HasElementType)
        {
            element = element.GetElementType()!; // HasElementType guarantees an element type.
        }

        if (element == typeof(float) || element == typeof(double) || element == typeof(decimal))
        {
            offenders.Add($"{where}: {element.Name}");
        }
    }
}
