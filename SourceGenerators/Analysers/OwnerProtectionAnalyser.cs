using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Solas.SourceGenerators.Analysers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class OwnerProtectionAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "SOLAS0001";
    private const string Title = "Manual edit of 'Entity' property is not allowed";
    private const string MessageFormat = "'Entity' property can be modified only inside allowed Solas namespaces";
    private const string Category = "Architecture";
    
    private static readonly string[] _allowedNamespaces = 
    [
        "Solas.Components",
        "Solas.ComponentUtils",
        "Solas.Containers"
    ];

    private static readonly DiagnosticDescriptor _rule = new(
        DiagnosticId, Title, MessageFormat, Category, 
        DiagnosticSeverity.Error, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [_rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
        context.RegisterSyntaxNodeAction(AnalyzeWithExpression, SyntaxKind.WithExpression);
    }

    private void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        if (IsInAllowedNamespace(context)) return;

        var assignment = (AssignmentExpressionSyntax)context.Node;
        var symbolInfo = context.SemanticModel.GetSymbolInfo(assignment.Left);
        var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

        if (symbol is IPropertySymbol property && property.Name == "Entity")
        {
            if (IsComponentType(property.ContainingType))
            {
                context.ReportDiagnostic(Diagnostic.Create(_rule, assignment.GetLocation()));
            }
        }
    }

    private void AnalyzeWithExpression(SyntaxNodeAnalysisContext context)
    {
        if (IsInAllowedNamespace(context)) return;

        var withExpression = (WithExpressionSyntax)context.Node;
        
        foreach (var initializer in withExpression.Initializer.Expressions)
        {
            if (initializer is AssignmentExpressionSyntax assignment)
            {
                var symbolInfo = context.SemanticModel.GetSymbolInfo(assignment.Left);
                var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

                if (symbol is IPropertySymbol property && property.Name == "Entity")
                {
                    if (IsComponentType(property.ContainingType))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(_rule, initializer.GetLocation()));
                    }
                }
            }
        }
    }

    private static bool IsInAllowedNamespace(SyntaxNodeAnalysisContext context)
    {
        var enclosingSymbol = context.SemanticModel.GetEnclosingSymbol(context.Node.SpanStart);
        var ns = enclosingSymbol?.ContainingNamespace?.ToDisplayString();

        if (string.IsNullOrEmpty(ns)) return false;

        return _allowedNamespaces.Any(allowed => 
            ns.Equals(allowed, StringComparison.Ordinal) || 
            ns.StartsWith(allowed + ".", StringComparison.Ordinal));
    }

    private static bool IsComponentType(INamedTypeSymbol? typeSymbol)
    {
        if (typeSymbol == null) return false;

        if (typeSymbol.Name is "IData" or "ILogic")
            return true;

        return typeSymbol.AllInterfaces.Any(i => i.Name is "IData" or "ILogic");
    }
}