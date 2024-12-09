using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LanguageModels;

static class InputSchemas
{ 
    record InputSchemaProperty(string Name, DescriptionForLanguageModel? DescriptionForLanguageModel, Type Type, bool Required = true);

    public static JsonObject InputSchemaFor(MethodInfo methodInfo)
    {
        InputSchemaProperty PropertyFor(ParameterInfo p) => new(p.Name!.ToLower(), p.GetCustomAttribute<DescriptionForLanguageModel>(), p.ParameterType, true);

        return InputSchemaFor([..methodInfo.GetParameters().Select(PropertyFor)]);
    }
    
    static JsonObject InputSchemaFor(InputSchemaProperty[] inputSchemaProperties, JsonObject? parentdefs = null)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        var defs = parentdefs ?? new ();
        
        foreach (var inputSchemaProperty in inputSchemaProperties)
        {
            var propertyName = inputSchemaProperty.Name.ToLower();

            properties.Add(propertyName, PropertyEntryFor(inputSchemaProperty));
            if (inputSchemaProperty.Required)
                required.Add(propertyName);
        }
        
        var result = new JsonObject()
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required, 
            ["additionalProperties"] = false
        };
        if (parentdefs == null)
            result["$defs"] = defs;
        return result;

        string GetTypeRef(Type type)
        {
            if (!defs.ContainsKey(type.Name))
            {
                defs[type.Name] = new JsonObject(); // to avoid recursion
                defs[type.Name] = InputSchemaFor(type, defs);
            }

            return $"#/$defs/{type.Name}";
        }
        
        JsonNode PropertyEntryFor(InputSchemaProperty inputSchemaProperty)
        {
            var result = new JsonObject();
        
            if (inputSchemaProperty.DescriptionForLanguageModel != null)
                result["description"] = inputSchemaProperty.DescriptionForLanguageModel.Description;
        
            var type = TypeFor(inputSchemaProperty.Type);
            if (type == "array")
            {
                var elementType = inputSchemaProperty.Type.GetElementType()!;
                var elementTypeType = TypeFor(elementType);
                if (elementTypeType == "object")
                    result["items"] = new JsonObject { ["$ref"] = GetTypeRef(elementType) };
                else
                    result["items"] = new JsonObject { ["type"] = elementTypeType };
            }

            if (type == "object")
                return new JsonObject { ["$ref"] = GetTypeRef(inputSchemaProperty.Type) };
            
            result["type"] = type;
            if (inputSchemaProperty.Type.IsEnum)
                result["enum"] = new JsonArray(
                    Enum.GetValues(inputSchemaProperty.Type)
                        .Cast<object>()
                        .Select(t => JsonValue.Create(t.ToString()!))
                        .Cast<JsonNode>()
                        .ToArray());
            return result;
        }
    }

    static JsonObject InputSchemaFor(Type t, JsonObject? defs = null) =>
        InputSchemaFor(t
            .GetProperties()
            .Where(p => !((p.GetGetMethod(true) ?? p.GetSetMethod(true))?.IsStatic ?? false))
            .Select(p => new InputSchemaProperty(
                Name: p.Name.ToLower(),
                DescriptionForLanguageModel: p.GetCustomAttribute<DescriptionForLanguageModel>(),
                Type: p.PropertyType, 
                Required: true)
            ).Concat(t
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(field => field.GetCustomAttribute<JsonIncludeAttribute>() != null)
                .Select(p => new InputSchemaProperty(
                    Name: p.Name.ToLower(),
                    DescriptionForLanguageModel: p.GetCustomAttribute<DescriptionForLanguageModel>(),
                    Type: p.FieldType, 
                    Required: true)
                )
            )
            .ToArray(), defs);

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
        if (t.IsEnum)
            return "string";
        if (t.IsArray)
            return "array";
        return "object";
    }
}