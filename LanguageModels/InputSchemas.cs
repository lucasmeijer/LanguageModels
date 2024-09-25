using System.Reflection;
using System.Text.Json.Nodes;

namespace LanguageModels;

static class InputSchemas
{ 
    record InputSchemaProperty(string Name, DescriptionForLanguageModel? DescriptionForLanguageModel, Type Type, bool Required = true);

    public static JsonObject InputSchemaFor(MethodInfo methodInfo)
    {
        InputSchemaProperty PropertyFor(ParameterInfo p) => new(p.Name!.ToLower(), p.GetCustomAttribute<DescriptionForLanguageModel>(), p.ParameterType, true);

        return InputSchemaFor([..methodInfo.GetParameters().Select(PropertyFor)]);
    }
    
    static JsonObject InputSchemaFor(InputSchemaProperty[] inputSchemaProperties)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        
        foreach (var inputSchemaProperty in inputSchemaProperties)
        {
            var propertyName = inputSchemaProperty.Name.ToLower();

            properties.Add(propertyName, PropertyEntryFor(inputSchemaProperty));
            if (inputSchemaProperty.Required)
                required.Add(propertyName);
        }
        
        return new()
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required 
        };

        JsonNode PropertyEntryFor(InputSchemaProperty inputSchemaProperty)
        {
            var result = new JsonObject();
        
            if (inputSchemaProperty.DescriptionForLanguageModel != null)
                result["description"] = inputSchemaProperty.DescriptionForLanguageModel.Description;
        
            var type = TypeFor(inputSchemaProperty.Type);
            if (type == "object")
                return InputSchemaFor(inputSchemaProperty.Type);
            
            result["type"] = type;
            return result;
        }
    }

    static JsonObject InputSchemaFor(Type t) =>
        InputSchemaFor(t
            .GetProperties()
            .Select(p => new InputSchemaProperty(
                Name: p.Name.ToLower(),
                DescriptionForLanguageModel: p.GetCustomAttribute<DescriptionForLanguageModel>(),
                Type: p.PropertyType, 
                Required: true)
            )
            .ToArray());

    static string TypeFor(Type t)
    {
        if (t == typeof(string))
            return "string";
        if (t == typeof(decimal) || t == typeof(float) || t == typeof(double))
            return "number";
        if (t == typeof(int))
            return "integer";
        if (t == typeof(bool))
            return "boolean";
        if (t.IsArray)
            throw new NotSupportedException("Arrays not yet supported");
        return "object";
    }
}