using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Solas.SourceGenerators.Utils;

namespace Solas.SourceGenerators;

[Generator]
public sealed class LogicAddingRegistrationGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classes = context.SyntaxProvider.CreateSyntaxProvider(
            static (node, _) => node is ClassDeclarationSyntax,
            static (ctx, _) => ctx.Node as ClassDeclarationSyntax
        ).Where(static c => c is not null);

        var compilationAndClasses = context.CompilationProvider.Combine(classes.Collect());

        context.RegisterSourceOutput(compilationAndClasses, static (ctx, source) =>
        {
            var (compilation, syntaxes) = source;
            var assemblyName = compilation.AssemblyName ?? "UnknownAssembly";
            var logicBaseType = compilation.GetTypeByMetadataName("Solas.Components.ILogic");

            if (logicBaseType == null) return;

            var registeredLogics = new List<string>();

            foreach (var syntax in syntaxes)
            {
                var model = compilation.GetSemanticModel(syntax!.SyntaxTree);
                if (model.GetDeclaredSymbol(syntax) is not INamedTypeSymbol symbol) continue;

                if (symbol.InheritsFrom(logicBaseType) && !symbol.IsAbstract)
                    registeredLogics.Add(symbol.ToDisplayString());
            }
            
            if (registeredLogics.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine("using Solas.Registries;");
            sb.AppendLine();
            sb.AppendLine("namespace SolasGenerated;");
            sb.AppendLine();
            sb.AppendLine("public class LogicAddingRegistration : ILogicRegistration");
            sb.AppendLine("{");
            sb.AppendLine("    public void Add(Solas.Registries.Registry registry)");
            sb.AppendLine("    {");
            sb.AppendLine("        var trueRegistry = (Solas.Registries.LogicAddingRegistry) registry;");
            foreach (var logic in registeredLogics)
                sb.AppendLine($"        trueRegistry.Register<{logic}>(\"{logic}, {assemblyName}\");");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            ctx.AddSource("LogicAddingRegistration.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        });
    }
}