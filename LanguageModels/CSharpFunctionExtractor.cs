using System.Reflection;
using System.Text.Json;

namespace LanguageModels;

public static class CSharpBackedFunctions
{
    public static Function[] Create(object[] objects)
    {
        return objects.SelectMany(CreateFunctionFor).ToArray();
    }

    static IEnumerable<Function> CreateFunctionFor(object o)
    {
        foreach (var methodInfo in o.GetType().GetMethods())
        {
            var attribute = methodInfo.GetCustomAttribute<DescriptionForLanguageModel>();
            if (attribute == null)
                continue;

            var functionName = FunctionNameFor(methodInfo);

            var functionFor = new Function(
                functionName, 
                attribute.Description,
                JsonDocument.Parse(InputSchemas.InputSchemaFor(methodInfo).ToJsonString()), 
                methodInfo.GetCustomAttribute<RequiresExplicitApproval>() != null,
                jsonArguments => Implementation(methodInfo, o, jsonArguments));
            yield return functionFor;
        }
    }

    public static string FunctionNameFor(MethodInfo methodInfo)
    {
        return (methodInfo.DeclaringType!.Name + "_" + methodInfo.Name).ToLower();
    }

    static async Task<string> Implementation(MethodInfo method, object obj, JsonDocument jsonArguments)
    {
        var arguments = method.GetParameters().Select(p => ArgumentFor(p, jsonArguments.RootElement)).ToArray();
        var result = method.Invoke(obj, arguments);

        // Check if taskObject is a Task
        if (result is Task task)
        {
            // Get the Awaiter
            MethodInfo getAwaiterMethod = task.GetType().GetMethod("GetAwaiter")!;
            object awaiter = getAwaiterMethod.Invoke(task, null)!;

            // Await the task using the awaiter's GetResult method
            MethodInfo getResultMethod = awaiter.GetType().GetMethod("GetResult")!;
            await task; // This is a non-reflection await

            // If it's a Task<T>, return the result
            if (task.GetType().IsGenericType)
                result = getResultMethod.Invoke(awaiter, null);
            else
                throw new NotSupportedException();
        }

        if (result is string s) return s;

        return JsonSerializer.Serialize(result);

        object? ArgumentFor(ParameterInfo p, JsonElement jsonArguments2)
        {
            if (!jsonArguments2.TryGetProperty(p.Name!.ToLower(), out var jsonArgumentValue))
                return null;
            
            if (jsonArgumentValue.ValueKind == JsonValueKind.Object)
            {
                var constructorInfo = p.ParameterType.GetConstructors().Single();
                var arguments = constructorInfo.GetParameters().Select(p => ArgumentFor(p, jsonArgumentValue)).ToArray();
                return constructorInfo.Invoke(arguments);
            }

            if (p.ParameterType == typeof(string)) return jsonArgumentValue.GetString();
            if (p.ParameterType == typeof(float)) return (float)jsonArgumentValue.GetDouble();
            if (p.ParameterType == typeof(decimal)) return jsonArgumentValue.GetDecimal();
            if (p.ParameterType == typeof(bool)) return jsonArgumentValue.GetBoolean();
            if (p.ParameterType == typeof(int)) return jsonArgumentValue.GetInt16();
            throw new NotSupportedException("Unsupported parameter: "+p.ParameterType.FullName);
        }
    }
}