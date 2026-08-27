using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Solas.SourceGenerators.GenericSerializers;
using Solas.SourceGenerators.Utils;

namespace Solas.SourceGenerators;

[Generator]
public sealed class SerializationGenerator : IIncrementalGenerator
{
    private static readonly Dictionary<string, IGenericSerializer> _genericSerializers = new ()
    {
        {"Array", new ArraySerializer()},
        {"List", new ListSerializer()},
    };
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var types = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is TypeDeclarationSyntax,
            static (ctx, _) => ctx.Node as TypeDeclarationSyntax
        ).Where(static t => t is not null);

        var compilationAndTypes = context.CompilationProvider.Combine(types.Collect());

        context.RegisterSourceOutput(compilationAndTypes, static (ctx, source) =>
        {
            var (compilation, syntaxes) = source;
            var assemblyName = compilation.AssemblyName ?? "UnknownAssembly";

            var dataInterface = compilation.GetTypeByMetadataName("Solas.Components.IData");
            var logicBaseType = compilation.GetTypeByMetadataName("Solas.Components.ILogic");
            var referenceableInterface = compilation.GetTypeByMetadataName("Solas.Interfaces.IReferenceable");

            var customSerializerInterface =
                compilation.GetTypeByMetadataName("Solas.Serialization.Core.ICustomSerializer`1");

            if (dataInterface == null) return;

            var userCustomSerializers = new HashSet<string>();
            var candidatesForGeneration = new List<INamedTypeSymbol>();
            var allSerializers = new List<(TypeMetadata Type, string SerializerName)>();

            foreach (var syntax in syntaxes)
            {
                var model = compilation.GetSemanticModel(syntax!.SyntaxTree);
                if (model.GetDeclaredSymbol(syntax) is not INamedTypeSymbol symbol) continue;

                if (customSerializerInterface != null)
                {
                    var serializerImpl = symbol.AllInterfaces.FirstOrDefault(i =>
                        SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, customSerializerInterface));

                    if (serializerImpl != null)
                    {
                        var targetType = serializerImpl.TypeArguments[0];
                        var targetTypeFullName = targetType.ToDisplayString();

                        if (userCustomSerializers.Add(targetTypeFullName))
                        {
                            var targetAssembly = targetType.ContainingAssembly?.Name ?? assemblyName;

                            var customMeta = new TypeMetadata {
                                Name = targetType.Name,
                                FullName = targetTypeFullName,
                                Namespace = targetType.ContainingNamespace.ToDisplayString(),
                                AssemblyName = targetAssembly,
                                IsStruct = targetType.TypeKind == TypeKind.Struct,
                                IsValueType = targetType.IsValueType
                            }

                        ;

                            allSerializers.Add((customMeta, symbol.ToDisplayString()));
                        }

                        continue;
                    }
                }

                var isData = symbol.ImplementsInterface(dataInterface);
                var isLogic = logicBaseType != null && symbol.InheritsFrom(logicBaseType);

                if ((isData || isLogic) && !symbol.IsAbstract && symbol.TypeKind is TypeKind.Class or TypeKind.Struct)
                {
                    var injectSource = DependencyInjectionBuilder.Generate(symbol, dataInterface, logicBaseType,
                        referenceableInterface);
                    if (injectSource != null)
                        ctx.AddSource($"{symbol.Name}.Inject.g.cs", SourceText.From(injectSource, Encoding.UTF8));

                    if (isData) candidatesForGeneration.Add(symbol);
                }
            }

            var processedTypes = new HashSet<string>(userCustomSerializers);
            var queue = new Queue<INamedTypeSymbol>(candidatesForGeneration);

            while (queue.Count > 0)
            {
                var symbol = queue.Dequeue();
                var fullTypeName = symbol.ToDisplayString();

                if (!processedTypes.Add(fullTypeName)) continue;
                var isFromCurrentAssembly = SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, compilation.Assembly);
                if (!isFromCurrentAssembly) continue;
                var metadata = new TypeMetadata
                {
                    Name = symbol.Name,
                    FullName = fullTypeName,
                    Namespace = symbol.ContainingNamespace.ToDisplayString(),
                    AssemblyName = assemblyName,
                    IsStruct = symbol.TypeKind == TypeKind.Struct,
                    IsValueType = symbol.IsValueType
                };

                var members = GetSerializableMembers(symbol, compilation);
                var generatedSource = GenerateSerializerCode(metadata, members);

                var sanitizedName = GetSanitizedTypeName(symbol);
                var serializerName = $"{sanitizedName}Serializer";

                ctx.AddSource($"{serializerName}.g.cs", SourceText.From(generatedSource, Encoding.UTF8));
                allSerializers.Add((metadata, $"SolasGenerated.{serializerName}"));

                foreach (var member in members)
                    EnqueueMemberType(member.Type, queue);
            }

            var registrySource = GenerateRegistryCode(allSerializers);
            ctx.AddSource("SerializationRegistration.g.cs", SourceText.From(registrySource, Encoding.UTF8));
        });
    }

    private static void EnqueueMemberType(ITypeSymbol type, Queue<INamedTypeSymbol> queue)
    {
        var memberType = type;
        if (memberType is IArrayTypeSymbol arrayType) memberType = arrayType.ElementType;

        if (memberType is INamedTypeSymbol namedMemberType)
        {
            if (namedMemberType.IsDataProperty(out var innerType))
            {
                if (innerType is INamedTypeSymbol namedInner && !namedInner.IsPrimitive() &&
                    namedInner.TypeKind is TypeKind.Class or TypeKind.Struct) queue.Enqueue(namedInner);
                return;
            }

            if (!namedMemberType.IsPrimitive() && namedMemberType.TypeKind is TypeKind.Class or TypeKind.Struct)
                queue.Enqueue(namedMemberType);
        }
    }

    private static string GetSanitizedTypeName(INamedTypeSymbol symbol)
    {
        if (!symbol.IsGenericType)
            return symbol.Name;

        var sb = new StringBuilder();
        sb.Append(symbol.Name);
        foreach (var arg in symbol.TypeArguments)
        {
            sb.Append('_');
            sb.Append(arg.Name);
        }

        return sb.ToString();
    }

    private static List<MemberMetadata> GetSerializableMembers(INamedTypeSymbol type, Compilation compilation)
    {
        var ignoreAttrSymbol = compilation.GetTypeByMetadataName("Solas.Attributes.SerializationIgnoreAttribute");
        var seenNames = new HashSet<string>();

        var typeHierarchy = new List<INamedTypeSymbol>();
        var current = type;
        while (current != null &&
               current.SpecialType != SpecialType.System_Object &&
               current.SpecialType != SpecialType.System_ValueType)
        {
            typeHierarchy.Add(current);
            current = current.BaseType;
        }

        var membersPerType = new List<List<MemberMetadata>>();

        foreach (var t in typeHierarchy)
        {
            var listForT = new List<MemberMetadata>();

            var fields = t.GetMembers().OfType<IFieldSymbol>()
                .Where(f => !f.IsStatic && !f.IsImplicitlyDeclared && f.DeclaredAccessibility == Accessibility.Public);

            foreach (var f in fields)
            {
                if (seenNames.Add(f.Name))
                {
                    if (!IsIgnored(f, ignoreAttrSymbol))
                    {
                        listForT.Add(CreateMemberMetadata(f.Name, f.Type, compilation));
                    }
                }
            }

            var properties = t.GetMembers().OfType<IPropertySymbol>()
                .Where(p => !p.IsStatic && p.GetMethod != null && p.SetMethod != null &&
                            p.DeclaredAccessibility == Accessibility.Public);

            foreach (var p in properties)
            {
                if (seenNames.Add(p.Name))
                {
                    if (!IsIgnored(p, ignoreAttrSymbol))
                    {
                        listForT.Add(CreateMemberMetadata(p.Name, p.Type, compilation));
                    }
                }
            }

            membersPerType.Add(listForT);
        }

        var members = new List<MemberMetadata>();
        for (int i = membersPerType.Count - 1; i >= 0; i--)
        {
            members.AddRange(membersPerType[i]);
        }

        return members;
    }

    private static bool IsIgnored(ISymbol symbol, INamedTypeSymbol? ignoreAttrSymbol)
    {
        if (HasSerializationIgnoreAttribute(symbol, ignoreAttrSymbol))
            return true;

        if (symbol is IPropertySymbol prop)
        {
            var overridden = prop.OverriddenProperty;
            while (overridden != null)
            {
                if (HasSerializationIgnoreAttribute(overridden, ignoreAttrSymbol))
                    return true;
                overridden = overridden.OverriddenProperty;
            }
        }

        return false;
    }

    private static bool HasSerializationIgnoreAttribute(ISymbol symbol, INamedTypeSymbol? ignoreAttrSymbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass == null) continue;

            if (ignoreAttrSymbol != null && SymbolEqualityComparer.Default.Equals(attrClass, ignoreAttrSymbol))
                return true;

            if (attrClass.ToDisplayString() == "Solas.Attributes.SerializationIgnoreAttribute")
                return true;

            if ((attrClass.Name is "SerializationIgnoreAttribute" or "SerializationIgnore") &&
                attrClass.ContainingNamespace?.ToDisplayString() == "Solas.Attributes")
                return true;
        }

        return false;
    }

    private static MemberMetadata CreateMemberMetadata(string name, ITypeSymbol type, Compilation compilation)
    {
        string genericType = "";
        ITypeSymbol elementType = type;
        if (type is IArrayTypeSymbol)
        {
            genericType = "Array";
            elementType =((IArrayTypeSymbol)type).ElementType;
        }
        else if (type is INamedTypeSymbol { IsGenericType: true } namedType)
        {
            var originalDef = namedType.OriginalDefinition.ToDisplayString();

            if (originalDef == "System.Collections.Generic.List<T>")
            {
                genericType = "List";
                elementType = namedType.TypeArguments[0]; 
            }
        }

        bool isValueType = elementType.IsValueType; 
        
        if (elementType.IsDataProperty(out var inner))
        {
            isValueType = inner!.IsValueType;
        }
        
        var checkType = type.IsDataProperty(out var unwrapped) ? unwrapped! : type;
        if (checkType is IArrayTypeSymbol arraySymbol)
        {
            checkType = arraySymbol.ElementType;
        }
        
        var dataInterface = compilation.GetTypeByMetadataName("Solas.Components.IData");
        var logicBaseType = compilation.GetTypeByMetadataName("Solas.Components.Logic");
        var referenceableInterface = compilation.GetTypeByMetadataName("Solas.Interfaces.IReferenceable");

        bool isReferenceLink = checkType.ImplementsInterface(dataInterface) ||
                               checkType.InheritsFrom(logicBaseType) ||
                               checkType.ImplementsInterface(referenceableInterface);
        _genericSerializers.TryGetValue(genericType, out var genericSerializer);

        var targetSymbol = elementType.IsDataProperty(out var innerProp) ? innerProp! : elementType;
        if (targetSymbol is IArrayTypeSymbol arr)
        {
            targetSymbol = arr.ElementType;
        }

        bool isEnum = targetSymbol.TypeKind == TypeKind.Enum ||
                      (targetSymbol is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableType &&
                       nullableType.TypeArguments[0].TypeKind == TypeKind.Enum);

        return new MemberMetadata{
            Name = name,
            TypeFullName = type.ToDisplayString(),
            GenericSerializer = genericSerializer,
            ElementTypeFullName = elementType.ToDisplayString(),
            IsPrimitive = elementType.IsPrimitive(),
            IsNullable = type.NullableAnnotation == NullableAnnotation.Annotated,
            IsValueType = isValueType,
            IsReferenceLink = isReferenceLink,
            Type = type,
            IsEnum = isEnum
        };
    }

    private static string GenerateSerializerCode(TypeMetadata type, List<MemberMetadata> members)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""
                      using System;
                      using System.IO;
                      using Solas;
                      using Solas.Serialization.Core;
                      """);
        sb.AppendLine($"using {type.Namespace};");
        sb.AppendLine();
        sb.AppendLine("namespace SolasGenerated;");
        sb.AppendLine();
        sb.AppendLine($"public sealed class {type.Name}Serializer : ICustomSerializer<{type.FullName}>");
        sb.AppendLine("{");
        sb.AppendLine($"    public void Write({type.FullName} value, FileStream stream, Serializer serializer, string name = null)");
        sb.AppendLine("    {");
        sb.AppendLine("        serializer.BeginObject(stream);");
        
        foreach (var member in members)
        {
            if (member.IsReferenceLink) continue;
            
            var isDataProperty = member.TypeFullName.StartsWith("Solas.ComponentUtils.DataProperty<");
            var accessPath = isDataProperty ? $"{member.Name}.Value" : member.Name;
            var writeExpr = member.GenericSerializer != null
                ? member.GenericSerializer.Write(member, accessPath)
                : (member.IsEnum
                    ? $"serializer.Write((int)value.{accessPath}, stream, \"{member.Name}\");"
                    : $"serializer.Write(value.{accessPath}, stream, \"{member.Name}\");");

            if (isDataProperty)
            {
                sb.AppendLine($"        serializer.Write(value.{member.Name} != null, stream, \"IsDataPropertyNull\");");
                sb.AppendLine($"        if (value.{member.Name} != null)");
                sb.AppendLine("        {");
                
                if (member.IsValueType)
                {
                    sb.AppendLine($"            {writeExpr}");
                }
                else 
                {
                    sb.AppendLine($"            serializer.Write(value.{accessPath} != null, stream, \"IsInnerPropertyNull\");");
                    sb.AppendLine($"            if (value.{accessPath} != null)");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                {writeExpr}");
                    sb.AppendLine("            }");
                }
                sb.AppendLine("        }");
            }
            else
            {
                if (member is { IsNullable: true, IsValueType: false })
                {
                    sb.AppendLine($"        serializer.Write(value.{member.Name} != null, stream, \"{member.Name}\");");
                    sb.AppendLine($"        if (value.{member.Name} != null)");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            {writeExpr}");
                    sb.AppendLine("        }");
                }
                else
                {
                    sb.AppendLine($"        {writeExpr}");
                }
            }
        }
        sb.AppendLine("        serializer.EndObject(stream);");
        sb.AppendLine("    }");
        sb.AppendLine();
        
        sb.AppendLine($"    public {type.FullName} Read(FileStream stream)");
        sb.AppendLine("    {");
        if (type.IsStruct)
        {
            sb.AppendLine($"        {type.FullName} result = default;");
        }
        else
        {
            sb.AppendLine($"        {type.FullName} result = new {type.FullName}();");
        }
        
        foreach (var member in members)
        {
            if (member.IsReferenceLink) continue;
            
            var isDataProperty = member.TypeFullName.StartsWith("Solas.ComponentUtils.DataProperty<");
            var accessPath = isDataProperty ? $"{member.Name}.Value" : member.Name;

            if (isDataProperty)
            {
                var unwrappedType = GetUnwrappedTypeName(member);
                
                sb.AppendLine("        if (Query.Serializer.ReadBool(stream))");
                sb.AppendLine("        {");
                sb.AppendLine($"            result.{member.Name} ??= new Solas.ComponentUtils.DataProperty<{unwrappedType}>();");
                
                if (member.IsValueType)
                {
                    sb.AppendLine($"            result.{accessPath} = {GenerateReadCall(member, unwrappedType)};");
                }
                else
                {
                    sb.AppendLine("            if (Query.Serializer.ReadBool(stream))");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                result.{accessPath} = {GenerateReadCall(member, unwrappedType)};");
                    sb.AppendLine("            }");
                }
                sb.AppendLine("        }");
            }
            else
            {
                if (member.IsNullable && !member.IsValueType)
                {
                    sb.AppendLine("        if (Query.Serializer.ReadBool(stream))");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            result.{member.Name} = {GenerateReadCall(member, member.TypeFullName)};");
                    sb.AppendLine("        }");
                }
                else
                {
                    sb.AppendLine($"        result.{member.Name} = {GenerateReadCall(member, member.TypeFullName)};");
                }
            }
        }
        
        sb.AppendLine("        return result;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }
    
    private static string GenerateReadCall(MemberMetadata member, string targetTypeFullName)
    {
        if (member.GenericSerializer != null)
        {
            return member.GenericSerializer.Read(member);
        }

        if (member.IsEnum)
        {
            return $"({targetTypeFullName})Query.Serializer.ReadInt32(stream)";
        }

        return member.IsPrimitive || GetPrimitiveMethodSuffix(targetTypeFullName) != "null"
            ? $"Query.Serializer.Read{GetPrimitiveMethodSuffix(targetTypeFullName)}(stream)"
            : $"Query.Serializer.Read<{targetTypeFullName}>(stream)";
    }
    
    private static string GetUnwrappedTypeName(MemberMetadata member)
    {
        var typeStr = member.TypeFullName;
        if (typeStr.StartsWith("Solas.ComponentUtils.DataProperty<") && typeStr.EndsWith(">"))
        {
            return typeStr.Substring(34, typeStr.Length - 35);
        }
        return typeStr;
    }

    internal static string GetPrimitiveMethodSuffix(string fullName)
    {
        return fullName switch
        {
            "byte" => "Byte",
            "bool" => "Bool",
            "char" => "Char",
            "string" => "String",
            "short" => "Int16",
            "int" => "Int32",
            "long" => "Int64",
            "ushort" => "UInt16",
            "uint" => "UInt32",
            "ulong" => "UInt64",
            "float" => "Float",
            "double" => "Double",
            "System.Guid" => "Guid",
            _ => "null"
        };
    }

    private static string GenerateRegistryCode(List<(TypeMetadata Type, string SerializerName)> serializers)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using Solas.Serialization.Core;");
        sb.AppendLine();
        sb.AppendLine("namespace SolasGenerated;");
        sb.AppendLine();
        sb.AppendLine("public class SerializationRegistration : ISerializeRegistration");
        sb.AppendLine("{");
        sb.AppendLine("    public void Add(Solas.Registries.Registry registry)");
        sb.AppendLine("    {");
        sb.AppendLine("        var serializer = (Serializer) registry;");
        foreach (var s in serializers)
        {
            var key = $"{s.Type.FullName}, {s.Type.AssemblyName}";
            sb.AppendLine($"        serializer.AddSerializer<{s.Type.FullName}>(new {s.SerializerName}());");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }
}